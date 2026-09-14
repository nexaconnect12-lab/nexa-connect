using System.Data;
using Npgsql;
using NpgsqlTypes;
using NexaConnect.Services.POS.Application.CashReviews;
using NexaConnect.Services.POS.Application.CashSessions;
using NexaConnect.Services.POS.Domain.CashReviews;

namespace NexaConnect.Services.POS.Infrastructure.Persistence;

public sealed class PostgresCashReviewStore(NpgsqlDataSource dataSource) : ICashReviewStore
{
    public async Task<bool> StoreMatchesScopeAsync(Guid restaurantId, Guid branchId, Guid storeId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS(
                SELECT 1 FROM stores
                WHERE id = $1 AND restaurant_id = $2 AND branch_id = $3);
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(storeId);
        command.Parameters.AddWithValue(restaurantId);
        command.Parameters.AddWithValue(branchId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<IReadOnlyList<CashReviewListItem>> ListAsync(CashReviewScope scope,
        DateTimeOffset fromUtc, DateTimeOffset toUtc, DateTimeOffset? beforeClosedAtUtc, Guid? beforeId,
        int take, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT session.id, session.shift_id, session.store_id, shift.terminal_id,
                   shift.shift_number, shift.employee_identity_subject_id, btrim(session.currency),
                   session.expected_closing_amount, session.actual_closing_amount, session.variance_amount,
                   session.closed_at_utc, session.concurrency_version,
                   CASE WHEN session.variance_amount = 0 THEN 'balanced'
                        WHEN state.reviewed_session_version IS DISTINCT FROM session.concurrency_version
                            THEN 'review_required'
                        ELSE state.status END,
                   COALESCE(state.concurrency_version, 0), state.reviewed_at_utc
            FROM cash_sessions session
            JOIN shifts shift ON shift.id = session.shift_id AND shift.store_id = session.store_id
            JOIN stores store ON store.id = session.store_id
            LEFT JOIN cash_session_review_states state ON state.cash_session_id = session.id
            WHERE session.status = 'closed' AND session.store_id = $1
              AND store.restaurant_id = $2 AND store.branch_id = $3
              AND session.closed_at_utc >= $4 AND session.closed_at_utc < $5
              AND session.expected_closing_amount IS NOT NULL
              AND session.actual_closing_amount IS NOT NULL AND session.variance_amount IS NOT NULL
              AND ($6::timestamptz IS NULL OR (session.closed_at_utc, session.id) < ($6, $7))
            ORDER BY session.closed_at_utc DESC, session.id DESC
            LIMIT $8;
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(scope.StoreId);
        command.Parameters.AddWithValue(scope.RestaurantId);
        command.Parameters.AddWithValue(scope.BranchId);
        command.Parameters.AddWithValue(fromUtc);
        command.Parameters.AddWithValue(toUtc);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.TimestampTz,
            Value = (object?)beforeClosedAtUtc ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid,
            Value = (object?)beforeId ?? DBNull.Value });
        command.Parameters.AddWithValue(take);
        var items = new List<CashReviewListItem>();
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) items.Add(ReadItem(reader));
        return items;
    }

    public async Task<CashReviewDetail?> GetAsync(CashReviewScope scope, Guid cashSessionId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);
        CashReviewDetail? detail = await GetCoreAsync(connection, transaction, scope, cashSessionId,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return detail;
    }

    public async Task<CashReviewDetail?> ResolveAsync(CashReviewScope scope, Guid cashSessionId,
        CashReviewDecision decision, string actorSubjectId, Guid authorizationDecisionId,
        long expectedSessionVersion, long expectedReviewVersion, Guid idempotencyKey, string payloadHash,
        DateTimeOffset occurredAtUtc, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        (Guid CashSessionId, string PayloadHash)? existing = await FindReplayAsync(
            connection, transaction, idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            ValidateReplay(existing.Value, cashSessionId, payloadHash);
            await transaction.CommitAsync(cancellationToken);
            return await GetAsync(scope, cashSessionId, cancellationToken);
        }

        const string lockSql = """
            SELECT session.concurrency_version, session.variance_amount,
                   state.reviewed_session_version, state.status, COALESCE(state.concurrency_version, 0)
            FROM cash_sessions session
            JOIN stores store ON store.id = session.store_id
            LEFT JOIN cash_session_review_states state ON state.cash_session_id = session.id
            WHERE session.id = $1 AND session.status = 'closed' AND session.store_id = $2
              AND store.restaurant_id = $3 AND store.branch_id = $4
              AND session.expected_closing_amount IS NOT NULL
              AND session.actual_closing_amount IS NOT NULL AND session.variance_amount IS NOT NULL
            FOR UPDATE OF session;
            """;
        long sessionVersion;
        decimal variance;
        long? reviewedSessionVersion;
        string? currentStatus;
        long currentReviewVersion;
        await using (var command = new NpgsqlCommand(lockSql, connection, transaction))
        {
            command.Parameters.AddWithValue(cashSessionId);
            command.Parameters.AddWithValue(scope.StoreId);
            command.Parameters.AddWithValue(scope.RestaurantId);
            command.Parameters.AddWithValue(scope.BranchId);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await reader.CloseAsync();
                await transaction.CommitAsync(cancellationToken);
                return null;
            }
            sessionVersion = reader.GetInt64(0);
            variance = reader.GetDecimal(1);
            reviewedSessionVersion = reader.IsDBNull(2) ? null : reader.GetInt64(2);
            currentStatus = reader.IsDBNull(3) ? null : reader.GetString(3);
            currentReviewVersion = reader.GetInt64(4);
        }

        // A concurrent identical request may have committed while this transaction waited for the
        // cash-session lock. Recheck before applying the now-stale expected versions.
        existing = await FindReplayAsync(connection, transaction, idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            ValidateReplay(existing.Value, cashSessionId, payloadHash);
            await transaction.CommitAsync(cancellationToken);
            return await GetAsync(scope, cashSessionId, cancellationToken);
        }

        if (variance == 0) throw new InvalidOperationException("A balanced cash session does not require review.");
        if (sessionVersion != expectedSessionVersion || currentReviewVersion != expectedReviewVersion)
            throw new InvalidOperationException("Cash review concurrency conflict; refresh before deciding.");
        bool appliesToCurrentSession = reviewedSessionVersion == sessionVersion;
        if (appliesToCurrentSession && currentStatus == "approved")
            throw new InvalidOperationException("This cash-session version is already approved.");
        if (appliesToCurrentSession && currentStatus == "investigating" && decision.Code == "investigate")
            throw new InvalidOperationException("This cash-session version is already under investigation.");

        long nextReviewVersion = currentReviewVersion + 1;
        string nextStatus = decision.Code == "approve" ? "approved" : "investigating";
        const string stateSql = """
            INSERT INTO cash_session_review_states
                (cash_session_id, reviewed_session_version, status, reviewed_by,
                 authorization_decision_id, reviewed_at_utc, concurrency_version)
            VALUES ($1, $2, $3, $4, $5, $6, $7)
            ON CONFLICT (cash_session_id) DO UPDATE
            SET reviewed_session_version = EXCLUDED.reviewed_session_version,
                status = EXCLUDED.status, reviewed_by = EXCLUDED.reviewed_by,
                authorization_decision_id = EXCLUDED.authorization_decision_id,
                reviewed_at_utc = EXCLUDED.reviewed_at_utc,
                concurrency_version = EXCLUDED.concurrency_version
            WHERE cash_session_review_states.concurrency_version = $8;
            """;
        await using (var command = new NpgsqlCommand(stateSql, connection, transaction))
        {
            command.Parameters.AddWithValue(cashSessionId);
            command.Parameters.AddWithValue(sessionVersion);
            command.Parameters.AddWithValue(nextStatus);
            command.Parameters.AddWithValue(actorSubjectId);
            command.Parameters.AddWithValue(authorizationDecisionId);
            command.Parameters.AddWithValue(occurredAtUtc);
            command.Parameters.AddWithValue(nextReviewVersion);
            command.Parameters.AddWithValue(currentReviewVersion);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("Cash review concurrency conflict; refresh before deciding.");
        }

        const string historySql = """
            INSERT INTO cash_session_review_history
                (id, cash_session_id, session_version, decision, reason, reviewer_subject_id,
                 authorization_decision_id, payload_hash, review_version, occurred_at_utc)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10);
            """;
        await using (var command = new NpgsqlCommand(historySql, connection, transaction))
        {
            command.Parameters.AddWithValue(idempotencyKey);
            command.Parameters.AddWithValue(cashSessionId);
            command.Parameters.AddWithValue(sessionVersion);
            command.Parameters.AddWithValue(decision.Code);
            command.Parameters.AddWithValue(decision.Reason);
            command.Parameters.AddWithValue(actorSubjectId);
            command.Parameters.AddWithValue(authorizationDecisionId);
            command.Parameters.AddWithValue(payloadHash);
            command.Parameters.AddWithValue(nextReviewVersion);
            command.Parameters.AddWithValue(occurredAtUtc);
            try
            {
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                await transaction.RollbackAsync(cancellationToken);
                await using NpgsqlConnection replayConnection = await dataSource.OpenConnectionAsync(cancellationToken);
                (Guid CashSessionId, string PayloadHash)? committed = await FindReplayAsync(
                    replayConnection, null, idempotencyKey, cancellationToken);
                if (committed is null) throw;
                ValidateReplay(committed.Value, cashSessionId, payloadHash);
                return await GetAsync(scope, cashSessionId, cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(scope, cashSessionId, cancellationToken);
    }

    private static async Task<(Guid CashSessionId, string PayloadHash)?> FindReplayAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid idempotencyKey,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT cash_session_id, payload_hash
            FROM cash_session_review_history WHERE id = $1;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(idempotencyKey);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (reader.GetGuid(0), reader.GetString(1))
            : null;
    }

    private static void ValidateReplay((Guid CashSessionId, string PayloadHash) replay,
        Guid cashSessionId, string payloadHash)
    {
        if (replay.CashSessionId != cashSessionId ||
            !string.Equals(replay.PayloadHash, payloadHash, StringComparison.Ordinal))
            throw new CashReviewDuplicateOperationException(
                "The review idempotency key was already used with a different request.");
    }

    private static async Task<CashReviewDetail?> GetCoreAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, CashReviewScope scope, Guid cashSessionId,
        CancellationToken cancellationToken)
    {
        const string sessionSql = """
            SELECT session.id, session.shift_id, session.store_id, shift.terminal_id,
                   shift.shift_number, shift.employee_identity_subject_id, btrim(session.currency),
                   session.expected_closing_amount, session.actual_closing_amount, session.variance_amount,
                   session.closed_at_utc, session.concurrency_version,
                   CASE WHEN session.variance_amount = 0 THEN 'balanced'
                        WHEN state.reviewed_session_version IS DISTINCT FROM session.concurrency_version
                            THEN 'review_required'
                        ELSE state.status END,
                   COALESCE(state.concurrency_version, 0), state.reviewed_at_utc
            FROM cash_sessions session
            JOIN shifts shift ON shift.id = session.shift_id AND shift.store_id = session.store_id
            JOIN stores store ON store.id = session.store_id
            LEFT JOIN cash_session_review_states state ON state.cash_session_id = session.id
            WHERE session.id = $1 AND session.status = 'closed' AND session.store_id = $2
              AND store.restaurant_id = $3 AND store.branch_id = $4
              AND session.expected_closing_amount IS NOT NULL
              AND session.actual_closing_amount IS NOT NULL AND session.variance_amount IS NOT NULL;
            """;
        CashReviewListItem item;
        await using (var command = new NpgsqlCommand(sessionSql, connection, transaction))
        {
            command.Parameters.AddWithValue(cashSessionId);
            command.Parameters.AddWithValue(scope.StoreId);
            command.Parameters.AddWithValue(scope.RestaurantId);
            command.Parameters.AddWithValue(scope.BranchId);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            item = ReadItem(reader);
        }

        const string movementSql = """
            SELECT id, movement_type, amount, reason_code, occurred_at_utc
            FROM cash_movements WHERE cash_session_id = $1 ORDER BY occurred_at_utc, id;
            """;
        var movements = new List<CashMovementSummary>();
        await using (var command = new NpgsqlCommand(movementSql, connection, transaction))
        {
            command.Parameters.AddWithValue(cashSessionId);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                movements.Add(new CashMovementSummary(reader.GetGuid(0), reader.GetString(1), reader.GetDecimal(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4)));
        }

        const string historySql = """
            SELECT id, session_version, decision, reason, reviewer_subject_id,
                   authorization_decision_id, review_version, occurred_at_utc
            FROM cash_session_review_history
            WHERE cash_session_id = $1 ORDER BY review_version, id;
            """;
        var history = new List<CashReviewHistoryEntry>();
        await using (var command = new NpgsqlCommand(historySql, connection, transaction))
        {
            command.Parameters.AddWithValue(cashSessionId);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                history.Add(new CashReviewHistoryEntry(reader.GetGuid(0), reader.GetInt64(1), reader.GetString(2),
                    reader.GetString(3), reader.GetString(4), reader.GetGuid(5), reader.GetInt64(6),
                    reader.GetFieldValue<DateTimeOffset>(7)));
        }
        return new CashReviewDetail(item, movements, history);
    }

    private static CashReviewListItem ReadItem(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3), reader.GetString(4),
        reader.GetString(5), reader.GetString(6), reader.GetDecimal(7), reader.GetDecimal(8),
        reader.GetDecimal(9), reader.GetFieldValue<DateTimeOffset>(10), reader.GetInt64(11),
        reader.GetString(12), reader.GetInt64(13),
        reader.IsDBNull(14) ? null : reader.GetFieldValue<DateTimeOffset>(14));
}
