extern alias POS;

using Npgsql;
using Shift = POS::NexaConnect.Services.POS.Domain.Shifts.Shift;
using ShiftStatus = POS::NexaConnect.Services.POS.Domain.Shifts.ShiftStatus;
using ShiftConflictException = POS::NexaConnect.Services.POS.Application.Shifts.ShiftConflictException;
using CashStore = POS::NexaConnect.Services.POS.Infrastructure.Persistence.PostgresCashSessionStore;
using ShiftStore = POS::NexaConnect.Services.POS.Infrastructure.Persistence.PostgresShiftStore;

namespace NexaConnect.IntegrationTests;

public sealed class PosPostgresStoreTests : IAsyncLifetime
{
    private readonly string? _configuredConnectionString =
        Environment.GetEnvironmentVariable("NEXACONNECT_POS_INTEGRATION_DB");
    private NpgsqlDataSource? _dataSource;
    private string? _schema;
    private ShiftStore? _store;

    [PosPostgresFact]
    public async Task Terminal_scope_requires_active_matching_store_and_branch()
    {
        RequireDatabase();
        Guid restaurantId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();
        await InsertStoreAndTerminalAsync(restaurantId, branchId, storeId, terminalId, "active", "active");

        Assert.True(await _store!.TerminalMatchesAsync(
            branchId, storeId, terminalId, restaurantId, CancellationToken.None));
        Assert.False(await _store.TerminalMatchesAsync(
            Guid.NewGuid(), storeId, terminalId, restaurantId, CancellationToken.None));

        await SetTerminalStatusAsync(terminalId, "suspended");
        Assert.False(await _store.TerminalMatchesAsync(
            branchId, storeId, terminalId, restaurantId, CancellationToken.None));
    }

    [PosPostgresFact]
    public async Task Create_find_and_optimistic_close_persist_shift_state()
    {
        RequireDatabase();
        Guid restaurantId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();
        await InsertStoreAndTerminalAsync(restaurantId, branchId, storeId, terminalId, "active", "active");
        Guid authorizationDecisionId = Guid.NewGuid();
        Shift shift = Shift.Open(
            Guid.NewGuid(), storeId, terminalId, "integration-user", "SHIFT-DB-1",
            authorizationDecisionId, DateTimeOffset.UtcNow);

        await _store!.CreateAsync(shift, CancellationToken.None);
        var snapshot = await _store.FindOpenAsync(shift.Id, CancellationToken.None);
        Assert.NotNull(snapshot);
        Assert.Equal(ShiftStatus.Open, snapshot.Status);
        Assert.Equal(branchId, snapshot.BranchId);

        shift.Close("integration-user", Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.True(await _store.TryCloseAsync(shift, CancellationToken.None));
        Assert.Null(await _store.FindOpenAsync(shift.Id, CancellationToken.None));
        Assert.False(await _store.TryCloseAsync(shift, CancellationToken.None));
    }

    [PosPostgresFact]
    public async Task Duplicate_open_terminal_is_mapped_to_conflict()
    {
        RequireDatabase();
        Guid restaurantId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();
        await InsertStoreAndTerminalAsync(restaurantId, branchId, storeId, terminalId, "active", "active");

        Shift first = Shift.Open(Guid.NewGuid(), storeId, terminalId, "user-1", "SHIFT-DB-2", Guid.NewGuid(), DateTimeOffset.UtcNow);
        Shift second = Shift.Open(Guid.NewGuid(), storeId, terminalId, "user-2", "SHIFT-DB-3", Guid.NewGuid(), DateTimeOffset.UtcNow);
        await _store!.CreateAsync(first, CancellationToken.None);

        await Assert.ThrowsAsync<ShiftConflictException>(
            () => _store.CreateAsync(second, CancellationToken.None));
    }

    [PosPostgresFact]
    public async Task Cash_movement_replay_with_client_operation_id_is_idempotent()
    {
        RequireDatabase();
        Guid restaurantId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();
        await InsertStoreAndTerminalAsync(restaurantId, branchId, storeId, terminalId, "active", "active");
        Shift shift = Shift.Open(Guid.NewGuid(), storeId, terminalId, "cashier-1", "SHIFT-CASH-1", Guid.NewGuid(), DateTimeOffset.UtcNow);
        await _store!.CreateAsync(shift, CancellationToken.None);
        var cashStore = new CashStore(_dataSource!);
        Guid cashSessionId = await cashStore.OpenAsync(shift.Id, storeId, "USD", 10m, CancellationToken.None);
        Guid operationId = Guid.NewGuid();

        Assert.True(await cashStore.RecordMovementAsync(
            cashSessionId, "sale", 5m, "cashier-1", null, operationId, terminalId, "hash-1", CancellationToken.None));
        Assert.False(await cashStore.RecordMovementAsync(
            cashSessionId, "sale", 5m, "cashier-1", null, operationId, terminalId, "hash-1", CancellationToken.None));
        await Assert.ThrowsAsync<POS::NexaConnect.Services.POS.Application.CashSessions.DuplicateSyncOperationException>(() =>
            cashStore.RecordMovementAsync(cashSessionId, "sale", 6m, "cashier-1", null, operationId, terminalId, "hash-2", CancellationToken.None));

        Assert.Equal(1, await CountCashMovementsAsync(cashSessionId));
    }

    [PosPostgresFact]
    public async Task Concurrent_cash_movement_replay_commits_once()
    {
        RequireDatabase();
        var setup = await CreateOpenCashSessionAsync("SHIFT-CASH-CONCURRENT");
        Guid operationId = Guid.NewGuid();

        bool[] results = await Task.WhenAll(
            setup.Store.RecordMovementAsync(setup.CashSessionId, "sale", 5m, "cashier-1", null,
                operationId, setup.TerminalId, "concurrent-hash", CancellationToken.None),
            setup.Store.RecordMovementAsync(setup.CashSessionId, "sale", 5m, "cashier-1", null,
                operationId, setup.TerminalId, "concurrent-hash", CancellationToken.None));

        Assert.Single(results, value => value);
        Assert.Single(results, value => !value);
        Assert.Equal(1, await CountCashMovementsAsync(setup.CashSessionId));
        Assert.Equal(1, await CountSyncOperationsAsync(operationId));
    }

    [PosPostgresFact]
    public async Task Failed_cash_movement_rolls_back_sync_operation()
    {
        RequireDatabase();
        var setup = await CreateOpenCashSessionAsync("SHIFT-CASH-ROLLBACK");
        await setup.Store.CloseAsync(setup.CashSessionId, 10m, CancellationToken.None);
        Guid operationId = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Store.RecordMovementAsync(
            setup.CashSessionId, "sale", 5m, "cashier-1", null,
            operationId, setup.TerminalId, "rollback-hash", CancellationToken.None));

        Assert.Equal(0, await CountCashMovementsAsync(setup.CashSessionId));
        Assert.Equal(0, await CountSyncOperationsAsync(operationId));
    }

    [PosPostgresFact]
    public async Task Cash_movement_replay_rejects_wrong_terminal_or_shift_subject()
    {
        RequireDatabase();
        var setup = await CreateOpenCashSessionAsync("SHIFT-CASH-SCOPE");

        await Assert.ThrowsAsync<POS::NexaConnect.Services.POS.Application.CashSessions.CashSessionReplayAuthorizationException>(() =>
            setup.Store.RecordMovementAsync(setup.CashSessionId, "sale", 5m, "cashier-1", null,
                Guid.NewGuid(), Guid.NewGuid(), "scope-hash", CancellationToken.None));
        await Assert.ThrowsAsync<POS::NexaConnect.Services.POS.Application.CashSessions.CashSessionReplayAuthorizationException>(() =>
            setup.Store.RecordMovementAsync(setup.CashSessionId, "sale", 5m, "another-cashier", null,
                Guid.NewGuid(), setup.TerminalId, "subject-hash", CancellationToken.None));

        Assert.Equal(0, await CountCashMovementsAsync(setup.CashSessionId));
    }

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(_configuredConnectionString) || !IsSafeEnvironment())
        {
            return;
        }

        _schema = $"pos_it_{Guid.NewGuid():N}";
        var builder = new NpgsqlConnectionStringBuilder(_configuredConnectionString)
        {
            SearchPath = _schema
        };
        _dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();
        await using (var command = new NpgsqlCommand($"CREATE SCHEMA \"{_schema}\";", connection))
        {
            await command.ExecuteNonQueryAsync();
        }

        await using (var command = new NpgsqlCommand(SchemaSql, connection))
        {
            await command.ExecuteNonQueryAsync();
        }

        _store = new ShiftStore(_dataSource);
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is null || _schema is null)
        {
            return;
        }

        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();
        await using (var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE;", connection))
        {
            await command.ExecuteNonQueryAsync();
        }

        await _dataSource.DisposeAsync();
    }

    private void RequireDatabase()
    {
        if (_dataSource is null || !IsSafeEnvironment())
        {
            throw new InvalidOperationException(
                "POS PostgreSQL test configuration disappeared after test discovery.");
        }
    }

    private static bool IsSafeEnvironment()
    {
        string? environment = Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        return string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environment, "Test", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environment, "Testing", StringComparison.OrdinalIgnoreCase);
    }

    private async Task InsertStoreAndTerminalAsync(
        Guid restaurantId,
        Guid branchId,
        Guid storeId,
        Guid terminalId,
        string storeStatus,
        string terminalStatus)
    {
        await using NpgsqlConnection connection = await _dataSource!.OpenConnectionAsync();
        await using (var store = new NpgsqlCommand("""
            INSERT INTO stores
                (id, restaurant_id, branch_id, code, name, operational_status,
                 created_at_utc, created_by, updated_at_utc, updated_by)
            VALUES ($1, $2, $3, $4, $5, $6, now(), 'integration', now(), 'integration');
            """, connection))
        {
            store.Parameters.AddWithValue(storeId);
            store.Parameters.AddWithValue(restaurantId);
            store.Parameters.AddWithValue(branchId);
            store.Parameters.AddWithValue($"store-{storeId:N}");
            store.Parameters.AddWithValue("Integration Store");
            store.Parameters.AddWithValue(storeStatus);
            await store.ExecuteNonQueryAsync();
        }

        await using (var terminal = new NpgsqlCommand("""
            INSERT INTO terminals
                (id, restaurant_id, store_id, code, device_type, registration_status,
                 registered_at_utc, created_at_utc, updated_at_utc)
            VALUES ($1, $2, $3, $4, 'pos', $5, now(), now(), now());
            """, connection))
        {
            terminal.Parameters.AddWithValue(terminalId);
            terminal.Parameters.AddWithValue(restaurantId);
            terminal.Parameters.AddWithValue(storeId);
            terminal.Parameters.AddWithValue($"terminal-{terminalId:N}");
            terminal.Parameters.AddWithValue(terminalStatus);
            await terminal.ExecuteNonQueryAsync();
        }
    }

    private async Task SetTerminalStatusAsync(Guid terminalId, string status)
    {
        await using NpgsqlConnection connection = await _dataSource!.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE terminals SET registration_status = $2, revoked_at_utc = NULL WHERE id = $1;",
            connection);
        command.Parameters.AddWithValue(terminalId);
        command.Parameters.AddWithValue(status);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> CountCashMovementsAsync(Guid cashSessionId)
    {
        await using NpgsqlConnection connection = await _dataSource!.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM cash_movements WHERE cash_session_id = $1;",
            connection);
        command.Parameters.AddWithValue(cashSessionId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<int> CountSyncOperationsAsync(Guid operationId)
    {
        await using NpgsqlConnection connection = await _dataSource!.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM sync_operations WHERE client_operation_id = $1;",
            connection);
        command.Parameters.AddWithValue(operationId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<(CashStore Store, Guid CashSessionId, Guid TerminalId)> CreateOpenCashSessionAsync(string shiftNumber)
    {
        Guid restaurantId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();
        await InsertStoreAndTerminalAsync(restaurantId, branchId, storeId, terminalId, "active", "active");
        Shift shift = Shift.Open(Guid.NewGuid(), storeId, terminalId, "cashier-1", shiftNumber, Guid.NewGuid(), DateTimeOffset.UtcNow);
        await _store!.CreateAsync(shift, CancellationToken.None);
        var cashStore = new CashStore(_dataSource!);
        Guid cashSessionId = await cashStore.OpenAsync(shift.Id, storeId, "USD", 10m, CancellationToken.None);
        return (cashStore, cashSessionId, terminalId);
    }

    private const string SchemaSql = """
        CREATE TABLE stores
        (
            id uuid PRIMARY KEY, restaurant_id uuid NOT NULL, branch_id uuid NOT NULL,
            code text NOT NULL, name text NOT NULL, operational_status text NOT NULL,
            configuration jsonb NOT NULL DEFAULT '{}', created_at_utc timestamptz NOT NULL,
            created_by text NOT NULL, updated_at_utc timestamptz NOT NULL, updated_by text NOT NULL,
            concurrency_version bigint NOT NULL DEFAULT 1
        );
        CREATE TABLE terminals
        (
            id uuid PRIMARY KEY, restaurant_id uuid NOT NULL, store_id uuid NOT NULL,
            code text NOT NULL, device_type text NOT NULL, registration_status text NOT NULL,
            registered_at_utc timestamptz NULL, revoked_at_utc timestamptz NULL,
            last_seen_at_utc timestamptz NULL, last_sync_at_utc timestamptz NULL,
            created_at_utc timestamptz NOT NULL, updated_at_utc timestamptz NOT NULL,
            concurrency_version bigint NOT NULL DEFAULT 1
        );
        CREATE TABLE shifts
        (
            id uuid PRIMARY KEY, store_id uuid NOT NULL, terminal_id uuid NOT NULL,
            employee_identity_subject_id text NOT NULL, shift_number text NOT NULL,
            status text NOT NULL, opened_at_utc timestamptz NOT NULL, closed_at_utc timestamptz NULL,
            opened_by text NOT NULL, closed_by text NULL, created_at_utc timestamptz NOT NULL,
            updated_at_utc timestamptz NOT NULL, concurrency_version bigint NOT NULL DEFAULT 1,
            authorization_decision_id uuid NOT NULL, close_authorization_decision_id uuid NULL,
            CONSTRAINT uq_shifts_store_shift_number UNIQUE (store_id, shift_number)
        );
        CREATE UNIQUE INDEX uq_shifts_terminal_open ON shifts (terminal_id) WHERE status IN ('open', 'closing');
        CREATE TABLE cash_sessions
        (
            id uuid PRIMARY KEY, store_id uuid NOT NULL, shift_id uuid NOT NULL,
            currency char(3) NOT NULL, opening_amount numeric(19,4) NOT NULL,
            expected_closing_amount numeric(19,4) NULL, actual_closing_amount numeric(19,4) NULL,
            variance_amount numeric(19,4) NULL, status text NOT NULL,
            opened_at_utc timestamptz NOT NULL, closed_at_utc timestamptz NULL,
            created_at_utc timestamptz NOT NULL, updated_at_utc timestamptz NOT NULL,
            concurrency_version bigint NOT NULL DEFAULT 1
        );
        CREATE TABLE cash_movements
        (
            id uuid PRIMARY KEY, cash_session_id uuid NOT NULL, movement_type text NOT NULL,
            amount numeric(19,4) NOT NULL, order_id uuid NULL, payment_id uuid NULL,
            reason_code text NULL, occurred_at_utc timestamptz NOT NULL, recorded_by text NOT NULL
        );
        CREATE TABLE sync_operations
        (
            id uuid PRIMARY KEY, terminal_id uuid NOT NULL, client_operation_id uuid NOT NULL,
            operation_type text NOT NULL, payload_hash text NOT NULL, status text NOT NULL,
            response_status integer NULL, response_reference_id uuid NULL, error_code text NULL,
            received_at_utc timestamptz NOT NULL, completed_at_utc timestamptz NULL,
            CONSTRAINT uq_sync_operations_terminal_client_operation UNIQUE (terminal_id, client_operation_id)
        );
        """;
}

public sealed class PosPostgresFactAttribute : FactAttribute
{
    public PosPostgresFactAttribute()
    {
        string? environment = Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        bool safeEnvironment = string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environment, "Test", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environment, "Testing", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NEXACONNECT_POS_INTEGRATION_DB"))
            || !safeEnvironment)
        {
            Skip = "Requires NEXACONNECT_POS_INTEGRATION_DB and a Development/Test/Testing environment.";
        }
    }
}
