using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NexaConnect.POS;
using NexaConnect.PosLocalStateInspector;

namespace NexaConnect.UnitTests;

public sealed class PosLocalSqliteStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "nexaconnect-pos-sqlite-" + Guid.NewGuid().ToString("N"));
    private readonly TestProtector protector = new();

    [Fact]
    public void State_survives_restart_and_atomic_recovery_clear_preserves_shift()
    {
        var shift = new LocalShiftState(Guid.NewGuid(), "SHIFT-SQLITE", DateTimeOffset.UtcNow);
        var cash = new LocalCashSessionState(Guid.NewGuid(), shift.ShiftId, DateTimeOffset.UtcNow);
        var settlement = new LocalPendingSettlementState(Guid.NewGuid(), 175m, "THB", Guid.NewGuid(), "cash", OutcomeUncertain: true);
        PosClientConfiguration configuration = PosCheckoutIntegrationTests.Configuration();
        var cashReview = LocalPendingCashReviewState.Create(configuration, Guid.NewGuid(), "approve",
            "Count and receipt checked", 4, 1);
        PendingCheckout checkout = PendingCheckout.Create(PosCheckoutIntegrationTests.Configuration(), [new(Guid.NewGuid(), 2)]);
        var first = new LocalPosStore(directory, protector);
        first.SaveActiveShift(shift);
        first.SaveCashSession(cash);
        first.SavePendingCheckout(checkout);
        first.SavePendingSettlement(settlement);
        first.SavePendingCashReview(cashReview, configuration);

        var restarted = new LocalPosStore(directory, protector);
        Assert.Equal(shift, restarted.LoadActiveShift());
        Assert.Equal(cash, restarted.LoadCashSession());
        PendingCheckout restoredCheckout = restarted.LoadPendingCheckout()!;
        Assert.Equal(checkout.OrderId, restoredCheckout.OrderId);
        Assert.Equal(checkout.SettlementKey, restoredCheckout.SettlementKey);
        Assert.Equal(checkout.Lines, restoredCheckout.Lines);
        Assert.Equal(settlement, restarted.LoadPendingSettlement());
        Assert.Equal(cashReview, restarted.LoadPendingCashReview(configuration));

        restarted.ClearCheckoutAndSettlement();

        Assert.Null(restarted.LoadPendingCheckout());
        Assert.Null(restarted.LoadPendingSettlement());
        Assert.Equal(shift, restarted.LoadActiveShift());
        Assert.Equal(cash, restarted.LoadCashSession());
        Assert.Equal(cashReview, restarted.LoadPendingCashReview(configuration));
        Assert.DoesNotContain("SHIFT-SQLITE", Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(directory, "pos-state.db"))));
        Assert.Equal("2", Scalar("PRAGMA user_version;"));
        Assert.Equal("wal", Scalar("PRAGMA journal_mode;"));
    }

    [Fact]
    public void Outbox_recovers_interrupted_send_and_retains_completed_acknowledgement()
    {
        Guid terminalId = Guid.NewGuid();
        Guid cashSessionId = Guid.NewGuid();
        var first = new LocalOutboxStore(directory, protector);
        LocalOutboxOperation operation = first.Enqueue(
            "cash-movement", $"api/pos/v1/cash-sessions/{cashSessionId:D}/movements", "POST",
            "{\"amount\":125.00}", terminalId);
        first.MarkAttempted(operation.OperationId);

        var restarted = new LocalOutboxStore(directory, protector);
        LocalOutboxOperation recovered = Assert.Single(restarted.Load());
        Assert.Equal(LocalOutboxState.Queued, recovered.State);
        Assert.Equal(1, recovered.Attempts);
        Assert.Equal(operation.OperationId, recovered.OperationId);
        Assert.Equal(terminalId, recovered.TerminalId);

        restarted.MarkAttempted(operation.OperationId);
        restarted.Remove(operation.OperationId);
        Assert.Empty(restarted.Load());
        Assert.Equal("completed", Scalar("SELECT state FROM outbox_operations WHERE operation_id=$id;", operation.OperationId));
    }

    [Fact]
    public void Rejected_outbox_operation_requires_explicit_retry()
    {
        var store = new LocalOutboxStore(directory, protector);
        Guid cashSessionId = Guid.NewGuid();
        LocalOutboxOperation operation = store.Enqueue(
            "cash-movement", $"api/pos/v1/cash-sessions/{cashSessionId:D}/movements", "POST", "{}", Guid.NewGuid());
        store.MarkAttempted(operation.OperationId);
        store.MarkTerminalFailure(operation.OperationId, 403);

        LocalOutboxOperation rejected = Assert.Single(store.Load());
        Assert.Equal(LocalOutboxState.Rejected, rejected.State);
        Assert.Equal(403, rejected.TerminalFailureStatusCode);
        Assert.Equal(1, store.RetryTerminalFailures());
        Assert.Equal(LocalOutboxState.Queued, Assert.Single(store.Load()).State);
    }

    [Fact]
    public void Outbox_rejects_unapproved_routes_and_malformed_payloads_before_persistence()
    {
        var store = new LocalOutboxStore(directory, protector);

        Assert.Throws<ArgumentException>(() => store.Enqueue(
            "cash-movement", "https://remote.example/api/pos/v1/cash-sessions/value/movements",
            "POST", "{}", Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => store.Enqueue(
            "cash-movement", $"api/pos/v1/cash-sessions/{Guid.NewGuid():D}/movements",
            "POST", "{broken", Guid.NewGuid()));
        Assert.Empty(store.Load());
    }

    [Fact]
    public void Legacy_files_migrate_once_and_are_removed_after_commit()
    {
        Directory.CreateDirectory(directory);
        var shift = new LocalShiftState(Guid.NewGuid(), "LEGACY-SHIFT", DateTimeOffset.UtcNow);
        File.WriteAllText(Path.Combine(directory, "state.json"), JsonSerializer.Serialize(shift));
        var legacy = new LocalOutboxOperation(Guid.NewGuid(), "cash-movement",
            $"api/pos/v1/cash-sessions/{Guid.NewGuid():D}/movements", "POST", "{\"reasonCode\":\"legacy\"}",
            DateTimeOffset.UtcNow, 0, null, TerminalId: Guid.NewGuid());
        File.WriteAllText(Path.Combine(directory, "outbox.json"), JsonSerializer.Serialize(new[] { legacy }));

        var stateStore = new LocalPosStore(directory, protector);
        var outboxStore = new LocalOutboxStore(directory, protector);

        Assert.Equal(shift, stateStore.LoadActiveShift());
        Assert.Equal(legacy.OperationId, Assert.Single(outboxStore.Load()).OperationId);
        Assert.False(File.Exists(Path.Combine(directory, "state.json")));
        Assert.False(File.Exists(Path.Combine(directory, "outbox.json")));
    }

    [Fact]
    public void Corrupt_sqlite_database_fails_closed_and_is_preserved()
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "pos-state.db");
        byte[] corrupt = Enumerable.Repeat((byte)0xA5, 256).ToArray();
        File.WriteAllBytes(path, corrupt);

        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => new LocalPosStore(directory, protector));

        Assert.Contains("Preserve it", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(corrupt, File.ReadAllBytes(path));
    }

    [Fact]
    public void Coexisting_legacy_and_sqlite_state_fails_closed_without_deleting_either()
    {
        var store = new LocalPosStore(directory, protector);
        store.SaveActiveShift(new(Guid.NewGuid(), "SQLITE-SHIFT", DateTimeOffset.UtcNow));
        string legacyPath = Path.Combine(directory, "state.json");
        File.WriteAllText(legacyPath, JsonSerializer.Serialize(
            new LocalShiftState(Guid.NewGuid(), "LEGACY-SHIFT", DateTimeOffset.UtcNow)));

        Assert.Throws<InvalidDataException>(() => new LocalPosStore(directory, protector));

        Assert.True(File.Exists(legacyPath));
        Assert.True(File.Exists(Path.Combine(directory, "pos-state.db")));
    }

    [Fact]
    public void Database_rejects_a_different_terminal_scope()
    {
        var original = new LocalPosScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _ = new LocalPosStore(directory, protector, original);
        var changedTerminal = original with { TerminalId = Guid.NewGuid() };

        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => new LocalPosStore(directory, protector, changedTerminal));

        Assert.Contains("different organization, branch, store, or terminal", error.InnerException?.Message ?? error.Message);
    }

    [Fact]
    public void Read_only_inspector_reports_only_acceptance_counts()
    {
        var state = new LocalPosStore(directory, protector);
        var outbox = new LocalOutboxStore(directory, protector);
        LocalStateInspectionResult empty = LocalStateInspection.Read(Path.Combine(directory, "pos-state.db"));
        Assert.True(empty.IntegrityOk);
        Assert.Equal(2, empty.SchemaVersion);
        Assert.Equal(0, empty.OperationalStateCount);
        Assert.Equal(0, empty.PendingCashReviewCount);
        Assert.Equal(0, empty.UnresolvedOutboxCount);

        state.SaveActiveShift(new(Guid.NewGuid(), "SHIFT-INSPECT", DateTimeOffset.UtcNow));
        PosClientConfiguration configuration = PosCheckoutIntegrationTests.Configuration();
        state.SavePendingCashReview(LocalPendingCashReviewState.Create(configuration, Guid.NewGuid(),
            "investigate", "Recount requested", 2, 0), configuration);
        outbox.Enqueue("cash-movement", $"api/pos/v1/cash-sessions/{Guid.NewGuid():D}/movements", "POST", "{}", Guid.NewGuid());
        LocalStateInspectionResult active = LocalStateInspection.Read(Path.Combine(directory, "pos-state.db"));
        Assert.Equal(2, active.OperationalStateCount);
        Assert.Equal(1, active.PendingCashReviewCount);
        Assert.Equal(1, active.UnresolvedOutboxCount);
        Assert.Equal(0, active.InterruptedSendCount);
        string inspectionJson = JsonSerializer.Serialize(active);
        Assert.DoesNotContain("Recount requested", inspectionJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Pending_cash_review_survives_restart_and_rejects_scope_or_payload_changes()
    {
        PosClientConfiguration configuration = PosCheckoutIntegrationTests.Configuration();
        var pending = LocalPendingCashReviewState.Create(configuration, Guid.NewGuid(), "investigate",
            "Recount requested", 7, 2);
        var first = new LocalPosStore(directory, protector, LocalPosScope.From(configuration));
        first.SavePendingCashReview(pending, configuration);

        var restarted = new LocalPosStore(directory, protector, LocalPosScope.From(configuration));
        Assert.Equal(pending, restarted.LoadPendingCashReview(configuration));
        Assert.True(pending.SameRequest(pending with { IdempotencyKey = Guid.NewGuid() }));
        Assert.False(pending.SameRequest(pending with { Decision = "approve" }));
        Assert.Throws<InvalidDataException>(() => restarted.LoadPendingCashReview(
            configuration with { StoreId = Guid.NewGuid() }));
        Assert.Throws<InvalidDataException>(() => restarted.SavePendingCashReview(
            pending with { Reason = " " }, configuration));

        restarted.ClearPendingCashReview();
        Assert.Null(restarted.LoadPendingCashReview(configuration));
    }

    [Fact]
    public void Schema_one_database_upgrades_to_two_without_losing_state()
    {
        var shift = new LocalShiftState(Guid.NewGuid(), "SHIFT-V1", DateTimeOffset.UtcNow);
        var first = new LocalPosStore(directory, protector);
        first.SaveActiveShift(shift);
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(directory, "pos-state.db")};Pooling=False"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version=1;";
            command.ExecuteNonQuery();
        }

        var upgraded = new LocalPosStore(directory, protector);

        Assert.Equal(shift, upgraded.LoadActiveShift());
        Assert.Equal("2", Scalar("PRAGMA user_version;"));
    }

    [Fact]
    public void Corrupt_pending_cash_review_fails_closed_and_remains_present()
    {
        PosClientConfiguration configuration = PosCheckoutIntegrationTests.Configuration();
        var store = new LocalPosStore(directory, protector, LocalPosScope.From(configuration));
        store.SavePendingCashReview(LocalPendingCashReviewState.Create(configuration, Guid.NewGuid(),
            "approve", "Evidence checked", 3, 1), configuration);
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(directory, "pos-state.db")};Pooling=False"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "UPDATE local_state SET protected_payload=x'010203' WHERE state_key='pending-cash-review';";
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        Assert.Throws<InvalidDataException>(() => store.LoadPendingCashReview(configuration));
        Assert.Equal("1", Scalar("SELECT count(*) FROM local_state WHERE state_key='pending-cash-review';"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    private string Scalar(string sql, Guid? operationId = null)
    {
        using var connection = new SqliteConnection($"Data Source={Path.Combine(directory, "pos-state.db")};Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        if (operationId is Guid id) command.Parameters.AddWithValue("$id", id.ToString("D"));
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)!;
    }

    private sealed class TestProtector : ILocalPayloadProtector
    {
        public byte[] Protect(byte[] plaintext) => plaintext.Select(value => (byte)(value ^ 0x5A)).ToArray();
        public byte[] Unprotect(byte[] protectedBytes) => Protect(protectedBytes);
    }
}
