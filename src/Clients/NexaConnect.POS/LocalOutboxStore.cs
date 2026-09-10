using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace NexaConnect.POS;

public enum LocalOutboxState
{
    Queued,
    Sending,
    Rejected,
    Completed
}

public sealed record LocalOutboxOperation(
    Guid OperationId,
    string OperationType,
    string RelativeUri,
    string Method,
    string PayloadJson,
    DateTimeOffset CreatedAtUtc,
    int Attempts,
    DateTimeOffset? LastAttemptAtUtc,
    int? TerminalFailureStatusCode = null,
    DateTimeOffset? TerminalFailureAtUtc = null,
    Guid? TerminalId = null,
    LocalOutboxState State = LocalOutboxState.Queued,
    DateTimeOffset? CompletedAtUtc = null);

public sealed class LocalOutboxStore
{
    private readonly LocalPosDatabase database;

    public LocalOutboxStore(LocalPosScope scope, string? storageDirectory = null)
        : this(storageDirectory, null, scope)
    {
    }

    internal LocalOutboxStore(string? storageDirectory = null, ILocalPayloadProtector? payloadProtector = null,
        LocalPosScope? scope = null)
    {
        database = new LocalPosDatabase(storageDirectory, payloadProtector, scope);
        RecoverInterruptedSends();
    }

    public IReadOnlyList<LocalOutboxOperation> Load()
    {
        try
        {
            using SqliteConnection connection = database.OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT operation_id, operation_type, relative_uri, http_method, protected_payload,
                       created_at_utc, attempts, last_attempt_at_utc,
                       terminal_failure_status_code, terminal_failure_at_utc, terminal_id, state, completed_at_utc
                FROM outbox_operations
                WHERE state <> 'completed'
                ORDER BY created_at_utc, operation_id;
                """;
            using SqliteDataReader reader = command.ExecuteReader();
            var operations = new List<LocalOutboxOperation>();
            while (reader.Read()) operations.Add(Read(reader));
            return operations;
        }
        catch (Exception exception) when (exception is not InvalidDataException)
        {
            throw new InvalidDataException(
                "The POS offline queue cannot be read. Preserve the local POS database and reconcile before continuing.",
                exception);
        }
    }

    public LocalOutboxOperation Enqueue(
        string operationType,
        string relativeUri,
        string method,
        string payloadJson,
        Guid? terminalId = null)
    {
        Validate(operationType, relativeUri, method, payloadJson, terminalId);
        var operation = new LocalOutboxOperation(
            Guid.NewGuid(), operationType, relativeUri, method, payloadJson,
            DateTimeOffset.UtcNow, 0, null, TerminalId: terminalId);
        using SqliteConnection connection = database.OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();
        Insert(connection, transaction, operation, database.Protect(Encoding.UTF8.GetBytes(payloadJson)));
        transaction.Commit();
        return operation;
    }

    public void MarkAttempted(Guid operationId) => UpdateState(operationId, "sending",
        "attempts=attempts+1,last_attempt_at_utc=$now,state='sending'," +
        "terminal_failure_status_code=NULL,terminal_failure_at_utc=NULL,completed_at_utc=NULL",
        expectedStates: ["queued"]);

    public void MarkRetryable(Guid operationId) => UpdateState(operationId, "queued",
        "state='queued',terminal_failure_status_code=NULL,terminal_failure_at_utc=NULL,completed_at_utc=NULL",
        expectedStates: ["sending"]);

    public void Remove(Guid operationId) => UpdateState(operationId, "completed",
        "state='completed',terminal_failure_status_code=NULL,terminal_failure_at_utc=NULL,completed_at_utc=$now",
        expectedStates: ["queued", "sending"]);

    public void MarkTerminalFailure(Guid operationId, int statusCode)
    {
        if (statusCode is not (400 or 403 or 409))
            throw new ArgumentOutOfRangeException(nameof(statusCode), "Only permanent POS boundary failures may be marked rejected.");
        UpdateState(operationId, "rejected",
            "state='rejected',terminal_failure_status_code=$status,terminal_failure_at_utc=$now,completed_at_utc=NULL",
            statusCode, ["queued", "sending"]);
    }

    public int RetryTerminalFailures()
    {
        using SqliteConnection connection = database.OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE outbox_operations
            SET state='queued', terminal_failure_status_code=NULL, terminal_failure_at_utc=NULL
            WHERE state='rejected';
            """;
        int count = command.ExecuteNonQuery();
        transaction.Commit();
        return count;
    }

    public int PurgeCompletedBefore(DateTimeOffset cutoffUtc)
    {
        using SqliteConnection connection = database.OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM outbox_operations WHERE state='completed' AND completed_at_utc < $cutoff;";
        command.Parameters.AddWithValue("$cutoff", Format(cutoffUtc));
        int count = command.ExecuteNonQuery();
        transaction.Commit();
        return count;
    }

    internal static void Insert(SqliteConnection connection, SqliteTransaction transaction,
        LocalOutboxOperation operation, byte[] protectedPayload)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO outbox_operations
                (operation_id, operation_type, relative_uri, http_method, protected_payload, terminal_id,
                 state, attempts, created_at_utc, last_attempt_at_utc, terminal_failure_status_code,
                 terminal_failure_at_utc, completed_at_utc)
            VALUES
                ($id, $type, $uri, $method, $payload, $terminal,
                 $state, $attempts, $created, $lastAttempt, $failureStatus, $failureAt, $completedAt)
            ON CONFLICT(operation_id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$id", operation.OperationId.ToString("D"));
        command.Parameters.AddWithValue("$type", operation.OperationType);
        command.Parameters.AddWithValue("$uri", operation.RelativeUri);
        command.Parameters.AddWithValue("$method", operation.Method);
        command.Parameters.AddWithValue("$payload", protectedPayload);
        command.Parameters.AddWithValue("$terminal", DbValue(operation.TerminalId?.ToString("D")));
        command.Parameters.AddWithValue("$state", StateText(operation.State));
        command.Parameters.AddWithValue("$attempts", operation.Attempts);
        command.Parameters.AddWithValue("$created", Format(operation.CreatedAtUtc));
        command.Parameters.AddWithValue("$lastAttempt", DbValue(operation.LastAttemptAtUtc is null ? null : Format(operation.LastAttemptAtUtc.Value)));
        command.Parameters.AddWithValue("$failureStatus", DbValue(operation.TerminalFailureStatusCode));
        command.Parameters.AddWithValue("$failureAt", DbValue(operation.TerminalFailureAtUtc is null ? null : Format(operation.TerminalFailureAtUtc.Value)));
        command.Parameters.AddWithValue("$completedAt", DbValue(operation.CompletedAtUtc is null ? null : Format(operation.CompletedAtUtc.Value)));
        command.ExecuteNonQuery();
    }

    private LocalOutboxOperation Read(SqliteDataReader reader)
    {
        try
        {
            Guid operationId = Guid.Parse(reader.GetString(0));
            byte[] plaintext = database.Unprotect((byte[])reader.GetValue(4));
            string payload = Encoding.UTF8.GetString(plaintext);
            Guid? terminalId = reader.IsDBNull(10) ? null : Guid.Parse(reader.GetString(10));
            var operation = new LocalOutboxOperation(
                operationId,
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                payload,
                Parse(reader.GetString(5)),
                reader.GetInt32(6),
                reader.IsDBNull(7) ? null : Parse(reader.GetString(7)),
                reader.IsDBNull(8) ? null : reader.GetInt32(8),
                reader.IsDBNull(9) ? null : Parse(reader.GetString(9)),
                terminalId,
                ParseState(reader.GetString(11)),
                reader.IsDBNull(12) ? null : Parse(reader.GetString(12)));
            Validate(operation.OperationType, operation.RelativeUri, operation.Method,
                operation.PayloadJson, operation.TerminalId);
            return operation;
        }
        catch (Exception exception)
        {
            throw new InvalidDataException(
                "A protected POS outbox operation cannot be read. Preserve the local database for reconciliation.",
                exception);
        }
    }

    private void RecoverInterruptedSends()
    {
        using SqliteConnection connection = database.OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE outbox_operations SET state='queued' WHERE state='sending';";
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private void UpdateState(Guid operationId, string targetState, string assignments,
        int? statusCode = null, string[]? expectedStates = null)
    {
        if (operationId == Guid.Empty) throw new ArgumentException("Operation id is required.", nameof(operationId));
        expectedStates ??= [];
        using SqliteConnection connection = database.OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        string expected = string.Join(",", expectedStates.Select((_, index) => $"$expected{index}"));
        command.CommandText = $"UPDATE outbox_operations SET {assignments} WHERE operation_id=$id AND state IN ({expected});";
        command.Parameters.AddWithValue("$id", operationId.ToString("D"));
        command.Parameters.AddWithValue("$now", Format(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$status", DbValue(statusCode));
        for (int index = 0; index < expectedStates.Length; index++)
            command.Parameters.AddWithValue($"$expected{index}", expectedStates[index]);
        int count = command.ExecuteNonQuery();
        if (count != 1)
            throw new InvalidOperationException($"Outbox operation cannot transition to {targetState} from its current state.");
        transaction.Commit();
    }

    internal static void Validate(string operationType, string relativeUri, string method,
        string payloadJson, Guid? terminalId)
    {
        if (!string.Equals(operationType, "cash-movement", StringComparison.Ordinal))
            throw new ArgumentException("Only cash-movement operations are supported by this outbox.", nameof(operationType));
        const string prefix = "api/pos/v1/cash-sessions/";
        const string suffix = "/movements";
        if (!relativeUri.StartsWith(prefix, StringComparison.Ordinal) ||
            !relativeUri.EndsWith(suffix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(relativeUri[prefix.Length..^suffix.Length], "D", out Guid cashSessionId) ||
            cashSessionId == Guid.Empty)
            throw new ArgumentException("A cash-session movement API URI with a non-empty UUID is required.", nameof(relativeUri));
        if (!string.Equals(method, "POST", StringComparison.Ordinal))
            throw new ArgumentException("Only POST operations are supported by this outbox.", nameof(method));
        if (string.IsNullOrWhiteSpace(payloadJson) || payloadJson.Length > 64 * 1024)
            throw new ArgumentException("A bounded JSON payload is required.", nameof(payloadJson));
        try
        {
            using JsonDocument _ = JsonDocument.Parse(payloadJson, new JsonDocumentOptions { MaxDepth = 32 });
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("A valid JSON payload is required.", nameof(payloadJson), exception);
        }
        if (terminalId is Guid value && value == Guid.Empty)
            throw new ArgumentException("Terminal id must be non-empty when supplied.", nameof(terminalId));
    }

    private static object DbValue(object? value) => value ?? DBNull.Value;
    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static string StateText(LocalOutboxState state) => state.ToString().ToLowerInvariant();
    private static LocalOutboxState ParseState(string state) => state switch
    {
        "queued" => LocalOutboxState.Queued,
        "sending" => LocalOutboxState.Sending,
        "rejected" => LocalOutboxState.Rejected,
        "completed" => LocalOutboxState.Completed,
        _ => throw new InvalidDataException("Unknown POS outbox state.")
    };
}
