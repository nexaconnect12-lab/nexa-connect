using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace NexaConnect.POS;

internal interface ILocalPayloadProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] protectedBytes);
}

internal sealed class WindowsLocalPayloadProtector : ILocalPayloadProtector
{
    public byte[] Protect(byte[] plaintext) => WindowsDataProtection.Protect(plaintext);
    public byte[] Unprotect(byte[] protectedBytes) => WindowsDataProtection.Unprotect(protectedBytes);
}

public sealed record LocalPosScope(Guid OrganizationId, Guid BranchId, Guid StoreId, Guid TerminalId)
{
    public static LocalPosScope From(PosClientConfiguration configuration) =>
        new(configuration.OrganizationId, configuration.BranchId, configuration.StoreId, configuration.TerminalId);

    internal void Validate()
    {
        if (OrganizationId == Guid.Empty || BranchId == Guid.Empty || StoreId == Guid.Empty || TerminalId == Guid.Empty)
            throw new InvalidDataException("The local POS database requires a complete terminal scope.");
    }
}

internal sealed class LocalPosDatabase
{
    internal const int CurrentSchemaVersion = 2;
    private readonly string directory;
    private readonly string databasePath;
    private readonly ILocalPayloadProtector protector;
    private readonly string connectionString;

    internal LocalPosDatabase(string? storageDirectory = null, ILocalPayloadProtector? payloadProtector = null,
        LocalPosScope? scope = null)
    {
        directory = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexaConnect", "POS");
        databasePath = Path.Combine(directory, "pos-state.db");
        protector = payloadProtector ?? new WindowsLocalPayloadProtector();
        Directory.CreateDirectory(directory);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();

        try
        {
            using SqliteConnection connection = OpenConnection();
            MigrateSchema(connection);
            VerifyIntegrity(connection);
            MigrateLegacyFiles(connection);
            if (scope is not null) BindScope(connection, scope);
        }
        catch (Exception exception) when (exception is SqliteException or JsonException or IOException
            or System.Security.Cryptography.CryptographicException or InvalidOperationException)
        {
            throw new InvalidDataException(
                $"The local POS database at '{databasePath}' cannot be opened safely. Preserve it and any legacy recovery files for reconciliation.",
                exception);
        }
    }

    internal SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            connection.Open();
            Configure(connection);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    internal byte[] Protect(byte[] plaintext) => protector.Protect(plaintext);

    internal byte[] Unprotect(byte[] protectedBytes) => protector.Unprotect(protectedBytes);

    private static void Configure(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000; PRAGMA synchronous=FULL;";
        command.ExecuteNonQuery();
        using SqliteCommand journal = connection.CreateCommand();
        journal.CommandText = "PRAGMA journal_mode=WAL;";
        journal.ExecuteScalar();
    }

    private static void MigrateSchema(SqliteConnection connection)
    {
        using SqliteCommand versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version;";
        int version = Convert.ToInt32(versionCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (version > CurrentSchemaVersion)
            throw new InvalidOperationException($"Local POS schema {version} is newer than supported schema {CurrentSchemaVersion}.");
        if (version == CurrentSchemaVersion) return;

        if (version == 1)
        {
            using SqliteTransaction upgrade = connection.BeginTransaction();
            using SqliteCommand upgradeCommand = connection.CreateCommand();
            upgradeCommand.Transaction = upgrade;
            upgradeCommand.CommandText = "PRAGMA user_version=2;";
            upgradeCommand.ExecuteNonQuery();
            upgrade.Commit();
            return;
        }

        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE local_state
            (
                state_key TEXT PRIMARY KEY,
                protected_payload BLOB NOT NULL,
                updated_at_utc TEXT NOT NULL
            ) STRICT;
            CREATE TABLE outbox_operations
            (
                operation_id TEXT PRIMARY KEY,
                operation_type TEXT NOT NULL,
                relative_uri TEXT NOT NULL,
                http_method TEXT NOT NULL,
                protected_payload BLOB NOT NULL,
                terminal_id TEXT NULL,
                state TEXT NOT NULL CHECK(state IN ('queued','sending','rejected','completed')),
                attempts INTEGER NOT NULL CHECK(attempts >= 0),
                created_at_utc TEXT NOT NULL,
                last_attempt_at_utc TEXT NULL,
                terminal_failure_status_code INTEGER NULL,
                terminal_failure_at_utc TEXT NULL,
                completed_at_utc TEXT NULL,
                CHECK((state = 'rejected') = (terminal_failure_status_code IS NOT NULL)),
                CHECK((state = 'completed') = (completed_at_utc IS NOT NULL))
            ) STRICT;
            CREATE INDEX ix_outbox_operations_state_created
                ON outbox_operations(state, created_at_utc, operation_id);
            PRAGMA user_version=2;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void VerifyIntegrity(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        string? result = Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (!string.Equals(result, "ok", StringComparison.Ordinal))
            throw new InvalidDataException("The local POS database failed its integrity check.");
    }

    private void MigrateLegacyFiles(SqliteConnection connection)
    {
        string shiftPath = Path.Combine(directory, "state.json");
        string cashPath = Path.Combine(directory, "cash-session.json");
        string settlementPath = Path.Combine(directory, "pending-settlement.bin");
        string checkoutPath = Path.Combine(directory, "pending-checkout.bin");
        string outboxPath = Path.Combine(directory, "outbox.json");
        string[] paths = [shiftPath, cashPath, settlementPath, checkoutPath, outboxPath];
        if (!paths.Any(File.Exists)) return;

        using (SqliteCommand conflict = connection.CreateCommand())
        {
            conflict.CommandText = """
                SELECT EXISTS(SELECT 1 FROM local_state)
                    OR EXISTS(SELECT 1 FROM outbox_operations);
                """;
            if (Convert.ToInt32(conflict.ExecuteScalar(), CultureInfo.InvariantCulture) != 0)
                throw new InvalidDataException(
                    "SQLite state and legacy POS recovery files both exist. Preserve both and reconcile before startup.");
        }

        using SqliteTransaction transaction = connection.BeginTransaction();
        ImportJsonState<LocalShiftState>(connection, transaction, shiftPath, "active-shift");
        ImportJsonState<LocalCashSessionState>(connection, transaction, cashPath, "cash-session");
        ImportProtectedState<LocalPendingSettlementState>(connection, transaction, settlementPath, "pending-settlement");
        ImportProtectedState<PendingCheckout>(connection, transaction, checkoutPath, "pending-checkout");
        ImportLegacyOutbox(connection, transaction, outboxPath);
        transaction.Commit();

        foreach (string path in paths.Where(File.Exists)) File.Delete(path);
    }

    private void ImportJsonState<T>(SqliteConnection connection, SqliteTransaction transaction,
        string path, string key)
    {
        if (!File.Exists(path)) return;
        byte[] plaintext = File.ReadAllBytes(path);
        if (JsonSerializer.Deserialize<T>(plaintext) is null)
            throw new InvalidDataException($"Legacy POS state '{Path.GetFileName(path)}' is empty.");
        InsertStateIfAbsent(connection, transaction, key, protector.Protect(plaintext));
    }

    private void ImportProtectedState<T>(SqliteConnection connection, SqliteTransaction transaction,
        string path, string key)
    {
        if (!File.Exists(path)) return;
        byte[] protectedPayload = File.ReadAllBytes(path);
        if (JsonSerializer.Deserialize<T>(protector.Unprotect(protectedPayload)) is null)
            throw new InvalidDataException($"Legacy POS state '{Path.GetFileName(path)}' is empty.");
        InsertStateIfAbsent(connection, transaction, key, protectedPayload);
    }

    private void ImportLegacyOutbox(SqliteConnection connection, SqliteTransaction transaction, string path)
    {
        if (!File.Exists(path)) return;
        List<LocalOutboxOperation> operations = JsonSerializer.Deserialize<List<LocalOutboxOperation>>(File.ReadAllBytes(path))
            ?? throw new InvalidDataException("The legacy POS outbox is empty.");
        foreach (LocalOutboxOperation operation in operations)
        {
            LocalOutboxStore.Validate(operation.OperationType, operation.RelativeUri, operation.Method,
                operation.PayloadJson, operation.TerminalId);
            LocalOutboxStore.Insert(connection, transaction, operation with
            {
                State = operation.TerminalFailureStatusCode is null
                    ? LocalOutboxState.Queued
                    : LocalOutboxState.Rejected
            }, protector.Protect(Encoding.UTF8.GetBytes(operation.PayloadJson)));
        }
    }

    private static void InsertStateIfAbsent(SqliteConnection connection, SqliteTransaction transaction,
        string key, byte[] protectedPayload)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO local_state(state_key, protected_payload, updated_at_utc)
            VALUES($key, $payload, $updated)
            ON CONFLICT(state_key) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$payload", protectedPayload);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private void BindScope(SqliteConnection connection, LocalPosScope scope)
    {
        scope.Validate();
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT protected_payload FROM local_state WHERE state_key='terminal-scope';";
        object? existing = read.ExecuteScalar();
        if (existing is null)
        {
            InsertStateIfAbsent(connection, transaction, "terminal-scope",
                protector.Protect(JsonSerializer.SerializeToUtf8Bytes(scope)));
            transaction.Commit();
            return;
        }

        LocalPosScope? saved = JsonSerializer.Deserialize<LocalPosScope>(protector.Unprotect((byte[])existing));
        if (saved != scope)
            throw new InvalidDataException(
                "The local POS database belongs to a different organization, branch, store, or terminal. Restore the original configuration and reconcile before continuing.");
        transaction.Commit();
    }
}
