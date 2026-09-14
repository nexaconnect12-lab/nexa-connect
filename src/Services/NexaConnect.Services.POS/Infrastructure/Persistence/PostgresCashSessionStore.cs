using System.Data;
using Npgsql;
using NexaConnect.Services.POS.Application.CashSessions;

namespace NexaConnect.Services.POS.Infrastructure.Persistence;

public sealed class PostgresCashSessionStore(NpgsqlDataSource dataSource) : ICashSessionStore
{
    public async Task<Guid> OpenAsync(
        Guid shiftId,
        Guid storeId,
        string currency,
        decimal openingAmount,
        CancellationToken cancellationToken)
    {
        Guid id = Guid.NewGuid();
        const string sql = """
            INSERT INTO cash_sessions
                (id, store_id, shift_id, currency, opening_amount, status,
                 opened_at_utc, created_at_utc, updated_at_utc)
            SELECT $1, $2, shift.id, $3, $4, 'open', now(), now(), now()
            FROM shifts shift
            WHERE shift.id = $5 AND shift.store_id = $2 AND shift.status = 'open'
            ON CONFLICT (shift_id) DO NOTHING;
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(storeId);
        command.Parameters.AddWithValue(currency.ToUpperInvariant());
        command.Parameters.AddWithValue(openingAmount);
        command.Parameters.AddWithValue(shiftId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            const string existingSql = "SELECT status FROM cash_sessions WHERE shift_id = $1 AND store_id = $2;";
            await using var existing = new NpgsqlCommand(existingSql, connection);
            existing.Parameters.AddWithValue(shiftId);
            existing.Parameters.AddWithValue(storeId);
            object? status = await existing.ExecuteScalarAsync(cancellationToken);
            if (status is string existingStatus)
            {
                throw new InvalidOperationException(existingStatus == "closed"
                    ? "This shift already has a closed cash session. Close the shift and open a new shift before opening another cash session."
                    : "This shift already has an open cash session. Restore or reconcile that session before continuing.");
            }
            throw new InvalidOperationException("The shift is not open or does not belong to the store.");
        }

        return id;
    }

    public async Task<bool> RecordMovementAsync(
        Guid cashSessionId,
        string movementType,
        decimal amount,
        string recordedBy,
        string? reasonCode,
        Guid? clientOperationId,
        Guid? terminalId,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (clientOperationId is not null)
        {
            SyncOperationStatus status = await RecordSyncOperationAsync(
                connection,
                transaction,
                cashSessionId,
                clientOperationId.Value,
                terminalId!.Value,
                recordedBy,
                payloadHash,
                cancellationToken);
            if (status == SyncOperationStatus.Completed)
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }
        }

        const string sql = """
            INSERT INTO cash_movements
                (id, cash_session_id, movement_type, amount, reason_code, occurred_at_utc, recorded_by)
            SELECT $1, session.id, $2, $3, $4, now(), $5
            FROM cash_sessions session
            WHERE session.id = $6 AND session.status = 'open';
            """;
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue(Guid.NewGuid());
            command.Parameters.AddWithValue(movementType);
            command.Parameters.AddWithValue(amount);
            command.Parameters.AddWithValue((object?)reasonCode ?? DBNull.Value);
            command.Parameters.AddWithValue(recordedBy);
            command.Parameters.AddWithValue(cashSessionId);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("The cash session is not open.");
            }
        }

        const string advanceSessionSql = """
            UPDATE cash_sessions
            SET updated_at_utc = now(), concurrency_version = concurrency_version + 1
            WHERE id = $1 AND status = 'open';
            """;
        await using (var advanceSession = new NpgsqlCommand(advanceSessionSql, connection, transaction))
        {
            advanceSession.Parameters.AddWithValue(cashSessionId);
            if (await advanceSession.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("The cash session changed while recording the movement.");
        }

        if (clientOperationId is not null)
        {
            const string completeSql = """
                WITH session_terminal AS (
                    SELECT shift.terminal_id
                    FROM cash_sessions session
                    JOIN shifts shift ON shift.id = session.shift_id AND shift.store_id = session.store_id
                    WHERE session.id = $2
                )
                UPDATE sync_operations
                SET status = 'completed', response_status = 202, completed_at_utc = now()
                WHERE client_operation_id = $1
                  AND terminal_id = (SELECT terminal_id FROM session_terminal);
                """;
            await using (var complete = new NpgsqlCommand(completeSql, connection, transaction))
            {
                complete.Parameters.AddWithValue(clientOperationId.Value);
                complete.Parameters.AddWithValue(cashSessionId);
                if (await complete.ExecuteNonQueryAsync(cancellationToken) != 1)
                {
                    throw new InvalidOperationException("The POS sync operation could not be completed.");
                }
            }

            const string updateTerminalSql = """
                WITH session_terminal AS (
                    SELECT shift.terminal_id
                    FROM cash_sessions session
                    JOIN shifts shift ON shift.id = session.shift_id AND shift.store_id = session.store_id
                    WHERE session.id = $1
                )
                UPDATE terminals
                SET last_seen_at_utc = now(), last_sync_at_utc = now(), updated_at_utc = now(),
                    concurrency_version = concurrency_version + 1
                WHERE id = (SELECT terminal_id FROM session_terminal);
                """;
            await using var updateTerminal = new NpgsqlCommand(updateTerminalSql, connection, transaction);
            updateTerminal.Parameters.AddWithValue(cashSessionId);
            if (await updateTerminal.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("The POS terminal synchronization checkpoint could not be updated.");
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static async Task<SyncOperationStatus> RecordSyncOperationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid cashSessionId,
        Guid clientOperationId,
        Guid terminalId,
        string recordedBy,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        const string insertSql = """
            WITH session_terminal AS (
                SELECT shift.terminal_id
                FROM cash_sessions session
                JOIN shifts shift ON shift.id = session.shift_id AND shift.store_id = session.store_id
                WHERE session.id = $1
                  AND shift.terminal_id = $4
                  AND shift.employee_identity_subject_id = $5
            )
            INSERT INTO sync_operations
                (id, terminal_id, client_operation_id, operation_type, payload_hash, status, received_at_utc)
            SELECT $6, terminal_id, $2, 'cash-movement.recorded', $3, 'received', now()
            FROM session_terminal
            ON CONFLICT (terminal_id, client_operation_id) DO NOTHING;
            """;
        await using (var insert = new NpgsqlCommand(insertSql, connection, transaction))
        {
            insert.Parameters.AddWithValue(cashSessionId);
            insert.Parameters.AddWithValue(clientOperationId);
            insert.Parameters.AddWithValue(payloadHash);
            insert.Parameters.AddWithValue(terminalId);
            insert.Parameters.AddWithValue(recordedBy);
            insert.Parameters.AddWithValue(Guid.NewGuid());
            if (await insert.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                // The row may already exist, or the cash session may be unknown. The locked read below distinguishes them.
            }
        }

        const string readSql = """
            SELECT operation.payload_hash, operation.status
            FROM sync_operations operation
            JOIN cash_sessions session ON session.id = $1
            JOIN shifts shift ON shift.id = session.shift_id AND shift.store_id = session.store_id
            WHERE operation.terminal_id = shift.terminal_id
              AND operation.client_operation_id = $2
              AND shift.terminal_id = $3
              AND shift.employee_identity_subject_id = $4
            FOR UPDATE OF operation;
            """;
        await using var read = new NpgsqlCommand(readSql, connection, transaction);
        read.Parameters.AddWithValue(cashSessionId);
        read.Parameters.AddWithValue(clientOperationId);
        read.Parameters.AddWithValue(terminalId);
        read.Parameters.AddWithValue(recordedBy);
        await using NpgsqlDataReader reader = await read.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new CashSessionReplayAuthorizationException();
        }

        string storedHash = reader.GetString(0);
        string status = reader.GetString(1);
        if (!string.Equals(storedHash, payloadHash, StringComparison.Ordinal))
        {
            throw new DuplicateSyncOperationException("The client operation id was already used with a different cash movement payload.");
        }

        return string.Equals(status, "completed", StringComparison.Ordinal)
            ? SyncOperationStatus.Completed
            : SyncOperationStatus.Received;
    }

    public async Task CloseAsync(
        Guid cashSessionId,
        decimal actualClosingAmount,
        long expectedConcurrencyVersion,
        string subject,
        Guid terminalId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE cash_sessions session
            SET status = 'closed', actual_closing_amount = $2,
                expected_closing_amount = opening_amount + COALESCE(
                    (SELECT SUM(CASE WHEN movement_type IN ('sale', 'pay_in', 'float_adjustment')
                                     THEN amount ELSE -amount END)
                     FROM cash_movements WHERE cash_session_id = session.id), 0),
                variance_amount = $2 - (opening_amount + COALESCE(
                    (SELECT SUM(CASE WHEN movement_type IN ('sale', 'pay_in', 'float_adjustment')
                                     THEN amount ELSE -amount END)
                     FROM cash_movements WHERE cash_session_id = session.id), 0)),
                closed_at_utc = now(), updated_at_utc = now(), concurrency_version = concurrency_version + 1
            WHERE session.id = $1 AND session.status = 'open' AND session.concurrency_version = $3
              AND EXISTS (
                  SELECT 1 FROM shifts shift
                  WHERE shift.id = session.shift_id AND shift.store_id = session.store_id
                    AND shift.terminal_id = $4 AND shift.employee_identity_subject_id = $5);
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(cashSessionId);
        command.Parameters.AddWithValue(actualClosingAmount);
        command.Parameters.AddWithValue(expectedConcurrencyVersion);
        command.Parameters.AddWithValue(terminalId);
        command.Parameters.AddWithValue(subject);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException(
                "The cash session is missing, closed, belongs to another terminal or cashier, or changed after reconciliation was reviewed.");
        }
    }

    public async Task<CashSessionSummary?> GetSummaryAsync(
        Guid cashSessionId,
        string subject,
        Guid terminalId,
        CancellationToken cancellationToken)
    {
        const string sessionSql = """
            SELECT session.shift_id, session.store_id, shift.terminal_id, btrim(session.currency),
                   session.opening_amount,
                   COALESCE(SUM(CASE WHEN movement.movement_type IN ('sale', 'pay_in', 'float_adjustment')
                                     THEN movement.amount ELSE -movement.amount END), 0),
                   session.opening_amount + COALESCE(SUM(
                       CASE WHEN movement.movement_type IN ('sale', 'pay_in', 'float_adjustment')
                            THEN movement.amount ELSE -movement.amount END), 0),
                   session.actual_closing_amount, session.variance_amount, session.status,
                   session.opened_at_utc, session.closed_at_utc, session.concurrency_version
            FROM cash_sessions session
            JOIN shifts shift ON shift.id = session.shift_id AND shift.store_id = session.store_id
            LEFT JOIN cash_movements movement ON movement.cash_session_id = session.id
            WHERE session.id = $1 AND shift.terminal_id = $2
              AND shift.employee_identity_subject_id = $3
            GROUP BY session.id, shift.terminal_id;
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);
        await using var sessionCommand = new NpgsqlCommand(sessionSql, connection, transaction);
        sessionCommand.Parameters.AddWithValue(cashSessionId);
        sessionCommand.Parameters.AddWithValue(terminalId);
        sessionCommand.Parameters.AddWithValue(subject);
        await using NpgsqlDataReader reader = await sessionCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await reader.CloseAsync();
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        Guid shiftId = reader.GetGuid(0);
        Guid storeId = reader.GetGuid(1);
        Guid persistedTerminalId = reader.GetGuid(2);
        string currency = reader.GetString(3);
        decimal openingAmount = reader.GetDecimal(4);
        decimal netMovementAmount = reader.GetDecimal(5);
        decimal expectedClosingAmount = reader.GetDecimal(6);
        decimal? actualClosingAmount = reader.IsDBNull(7) ? null : reader.GetDecimal(7);
        decimal? varianceAmount = reader.IsDBNull(8) ? null : reader.GetDecimal(8);
        string status = reader.GetString(9);
        DateTimeOffset openedAtUtc = reader.GetFieldValue<DateTimeOffset>(10);
        DateTimeOffset? closedAtUtc = reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11);
        long concurrencyVersion = reader.GetInt64(12);
        await reader.CloseAsync();

        const string movementsSql = """
            SELECT id, movement_type, amount, reason_code, occurred_at_utc
            FROM cash_movements
            WHERE cash_session_id = $1
            ORDER BY occurred_at_utc, id;
            """;
        await using var movementCommand = new NpgsqlCommand(movementsSql, connection, transaction);
        movementCommand.Parameters.AddWithValue(cashSessionId);
        await using NpgsqlDataReader movementReader = await movementCommand.ExecuteReaderAsync(cancellationToken);
        var movements = new List<CashMovementSummary>();
        while (await movementReader.ReadAsync(cancellationToken))
        {
            movements.Add(new CashMovementSummary(
                movementReader.GetGuid(0), movementReader.GetString(1), movementReader.GetDecimal(2),
                movementReader.IsDBNull(3) ? null : movementReader.GetString(3),
                movementReader.GetFieldValue<DateTimeOffset>(4)));
        }
        await movementReader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);

        return new CashSessionSummary(cashSessionId, shiftId, storeId, persistedTerminalId, currency,
            openingAmount, netMovementAmount, expectedClosingAmount, actualClosingAmount, varianceAmount,
            status, openedAtUtc, closedAtUtc, concurrencyVersion, movements);
    }

    private enum SyncOperationStatus
    {
        Received,
        Completed
    }
}
