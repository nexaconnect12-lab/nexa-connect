using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.POS.Application.CashReviews;
using NexaConnect.Services.POS.Domain.CashReviews;
using Npgsql;

namespace NexaConnect.Services.POS.Infrastructure.Persistence;

public sealed class PostgresCashClosePublicationStore(NpgsqlDataSource dataSource) : ICashClosePublicationStore
{
    public async Task<IReadOnlyList<CashCloseCandidate>> FindAsync(Guid? after, CancellationToken ct)
    {
        const string sql = """
            SELECT s.id,st.restaurant_id,st.branch_id FROM cash_sessions s
            JOIN stores st ON st.id=s.store_id
            LEFT JOIN cash_session_review_states r ON r.cash_session_id=s.id
            LEFT JOIN cash_close_publications p ON p.cash_session_id=s.id
            WHERE s.status='closed' AND s.closed_at_utc IS NOT NULL AND s.actual_closing_amount IS NOT NULL
              AND ($1::uuid IS NULL OR s.id>$1)
              AND (p.cash_session_id IS NULL OR p.financial_version<s.concurrency_version
                   OR p.review_version<COALESCE(r.concurrency_version,0))
            ORDER BY s.id LIMIT 100
            """;
        await using var c = await dataSource.OpenConnectionAsync(ct);
        await using var q = new NpgsqlCommand(sql, c);
        q.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid, Value = (object?)after ?? DBNull.Value });
        await using var r = await q.ExecuteReaderAsync(ct);
        var result = new List<CashCloseCandidate>();
        while (await r.ReadAsync(ct)) result.Add(new(r.GetGuid(0), r.GetGuid(1), r.GetGuid(2)));
        return result;
    }

    public async Task<bool> PublishAsync(CashCloseCandidate candidate, Guid organizationId, Guid correlationId, CancellationToken ct)
    {
        await using var c = await dataSource.OpenConnectionAsync(ct);
        await using var t = await c.BeginTransactionAsync(ct);
        // Review decisions and late settlements also lock this row; then read a fresh statement snapshot.
        await using (var q = new NpgsqlCommand("SELECT id FROM cash_sessions WHERE id=$1 FOR UPDATE", c, t))
        { q.Parameters.AddWithValue(candidate.SessionId); if (await q.ExecuteScalarAsync(ct) is null) return false; }
        const string sql = """
            SELECT s.store_id,s.shift_id,s.closed_at_utc,btrim(s.currency),s.concurrency_version,
                   s.opening_amount+COALESCE((SELECT sum(CASE WHEN movement_type IN ('sale','pay_in','float_adjustment')
                       THEN amount ELSE -amount END) FROM cash_movements WHERE cash_session_id=s.id),0),
                   s.actual_closing_amount,COALESCE(r.concurrency_version,0),r.reviewed_session_version,r.status,
                   p.organization_id,p.financial_version,p.review_version,COALESCE(p.snapshot_version,0),clock_timestamp()
            FROM cash_sessions s JOIN stores st ON st.id=s.store_id
            LEFT JOIN cash_session_review_states r ON r.cash_session_id=s.id
            LEFT JOIN cash_close_publications p ON p.cash_session_id=s.id
            WHERE s.id=$1 AND s.status='closed' AND st.restaurant_id=$2 AND st.branch_id=$3
              AND s.closed_at_utc IS NOT NULL AND s.actual_closing_amount IS NOT NULL
            """;
        PosCashCloseSnapshotV1 snapshot;
        await using (var q = new NpgsqlCommand(sql, c, t))
        {
            q.Parameters.AddWithValue(candidate.SessionId); q.Parameters.AddWithValue(candidate.RestaurantId); q.Parameters.AddWithValue(candidate.BranchId);
            await using var r = await q.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return false;
            long financial = r.GetInt64(4), review = r.GetInt64(7);
            if (!r.IsDBNull(10))
            {
                if (r.GetGuid(10) != organizationId) throw new InvalidOperationException("Cash-close ownership changed.");
                if (r.GetInt64(11) == financial && r.GetInt64(12) == review) return false;
            }
            decimal expected = r.GetDecimal(5), counted = r.GetDecimal(6);
            string status = CashClosePublication.ReviewStatus(counted - expected, financial,
                r.IsDBNull(8) ? null : r.GetInt64(8), r.IsDBNull(9) ? null : r.GetString(9));
            snapshot = new(Guid.NewGuid(), correlationId, r.GetFieldValue<DateTimeOffset>(14), organizationId, candidate.RestaurantId,
                candidate.BranchId, r.GetGuid(0), candidate.SessionId, r.GetGuid(1), r.GetFieldValue<DateTimeOffset>(2),
                r.GetString(3), expected, counted, counted - expected, checked(r.GetInt64(13) + 1), financial, review, status);
        }
        const string checkpoint = """
            INSERT INTO cash_close_publications(cash_session_id,organization_id,financial_version,review_version,snapshot_version)
            VALUES($1,$2,$3,$4,$5) ON CONFLICT(cash_session_id) DO UPDATE SET
            financial_version=EXCLUDED.financial_version,review_version=EXCLUDED.review_version,snapshot_version=EXCLUDED.snapshot_version
            """;
        await using (var q = new NpgsqlCommand(checkpoint, c, t))
        {
            q.Parameters.AddWithValue(snapshot.CashSessionId); q.Parameters.AddWithValue(organizationId);
            q.Parameters.AddWithValue(snapshot.FinancialVersion); q.Parameters.AddWithValue(snapshot.ReviewVersion); q.Parameters.AddWithValue(snapshot.SnapshotVersion);
            await q.ExecuteNonQueryAsync(ct);
        }
        const string outbox = """
            INSERT INTO outbox_messages(id,event_type,contract_version,aggregate_type,aggregate_id,payload,correlation_id,occurred_at_utc)
            VALUES($1,'pos.cash-close.snapshot.v1',1,'cash-session',$2,$3::jsonb,$4,$5)
            """;
        await using (var q = new NpgsqlCommand(outbox, c, t))
        {
            q.Parameters.AddWithValue(snapshot.EventId); q.Parameters.AddWithValue(snapshot.CashSessionId);
            q.Parameters.AddWithValue(JsonSerializer.Serialize(snapshot)); q.Parameters.AddWithValue(correlationId.ToString("D")); q.Parameters.AddWithValue(snapshot.OccurredAtUtc);
            await q.ExecuteNonQueryAsync(ct);
        }
        await t.CommitAsync(ct);
        return true;
    }
}
