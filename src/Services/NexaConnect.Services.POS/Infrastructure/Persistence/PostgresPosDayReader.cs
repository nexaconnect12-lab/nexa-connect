using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.POS.Application.CashReviews;
using Npgsql;

namespace NexaConnect.Services.POS.Infrastructure.Persistence;

public sealed class PostgresPosDayReader(NpgsqlDataSource source) : IPosDayReader
{
    public async Task<PosDaySummary> ReadAsync(EndOfDayWindow window, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var tx=await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct);
        var result = await QueryAsync(window, connection, tx, ct);
        await tx.CommitAsync(ct); return result;
    }
    public static async Task<PosDaySummary> QueryAsync(EndOfDayWindow window, NpgsqlConnection connection, NpgsqlTransaction tx, CancellationToken ct, Action<string[]>? retain = null)
    {
        await using var command = new NpgsqlCommand("""
            WITH scoped_stores AS (SELECT id FROM stores WHERE restaurant_id=$2 AND branch_id=$3),
            cash AS (
              SELECT s.*, s.actual_closing_amount-s.opening_amount-COALESCE((SELECT sum(
                CASE WHEN m.movement_type IN ('sale','pay_in','float_adjustment') THEN m.amount ELSE -m.amount END)
                FROM cash_movements m WHERE m.cash_session_id=s.id),0) AS variance,
                r.status AS review_status,r.reviewed_session_version
              FROM cash_sessions s JOIN scoped_stores store ON store.id=s.store_id
              LEFT JOIN cash_session_review_states r ON r.cash_session_id=s.id WHERE s.opened_at_utc<$5
            )
            SELECT (SELECT count(*)::int FROM shifts s JOIN scoped_stores store ON store.id=s.store_id
                WHERE s.status IN ('open','closing') AND s.opened_at_utc<$5),
              count(*) FILTER(WHERE status IN ('open','counting'))::int,
              count(*) FILTER(WHERE status='closed' AND closed_at_utc<$5 AND variance<>0 AND
                (reviewed_session_version IS DISTINCT FROM concurrency_version OR review_status IS DISTINCT FROM 'approved'))::int,
              COALESCE(sum(variance) FILTER(WHERE status='closed' AND closed_at_utc>=$4 AND closed_at_utc<$5),0),
              COALESCE(array_agg(DISTINCT btrim(currency)) FILTER(WHERE status='closed' AND closed_at_utc>=$4 AND closed_at_utc<$5),ARRAY[]::text[])
            FROM cash WHERE $1::uuid IS NOT NULL
            """, connection,tx);
        command.Parameters.AddWithValue(window.OrganizationId); command.Parameters.AddWithValue(window.RestaurantId);
        command.Parameters.AddWithValue(window.BranchId); command.Parameters.AddWithValue(window.FromUtc.ToUniversalTime());
        command.Parameters.AddWithValue(window.ToUtc.ToUniversalTime());
        PosDaySummary result;
        await using(var reader=await command.ExecuteReaderAsync(ct))
        {await reader.ReadAsync(ct);result=new(window,DateTimeOffset.UtcNow,reader.GetInt32(0),reader.GetInt32(1),reader.GetInt32(2),reader.GetDecimal(3),reader.GetFieldValue<string[]>(4));}
        await using var evidence=new NpgsqlCommand("""
            WITH stores_in_scope AS (SELECT id FROM stores WHERE restaurant_id=$2 AND branch_id=$3),
            sessions AS (
              SELECT c.*,r.status AS review_status,r.reviewed_session_version,r.concurrency_version AS review_version,
                c.actual_closing_amount-c.opening_amount-COALESCE((SELECT sum(CASE WHEN m.movement_type IN ('sale','pay_in','float_adjustment') THEN m.amount ELSE -m.amount END)
                  FROM cash_movements m WHERE m.cash_session_id=c.id),0) AS current_variance
              FROM cash_sessions c JOIN stores_in_scope s ON s.id=c.store_id LEFT JOIN cash_session_review_states r ON r.cash_session_id=c.id
              WHERE c.opened_at_utc<$5
            ) SELECT value FROM (
              SELECT 'shift' AS kind,s.id,jsonb_build_array('shift',s.id,s.status,s.concurrency_version,s.opened_at_utc)::text AS value
                FROM shifts s JOIN stores_in_scope x ON x.id=s.store_id WHERE s.opened_at_utc<$5 AND s.status IN ('open','closing')
              UNION ALL
              SELECT 'cash',c.id,jsonb_build_array('cash',c.id,c.status,c.concurrency_version,c.currency,c.opening_amount,
                c.actual_closing_amount,c.closed_at_utc,c.current_variance,c.review_status,c.reviewed_session_version,c.review_version,
                (SELECT jsonb_agg(jsonb_build_array(m.id,m.movement_type,m.amount,m.occurred_at_utc) ORDER BY m.id) FROM cash_movements m WHERE m.cash_session_id=c.id))::text
              FROM sessions c WHERE c.status IN ('open','counting') OR (c.status='closed' AND c.closed_at_utc<$5 AND
                (c.closed_at_utc>=$4 OR (c.current_variance<>0 AND (c.reviewed_session_version IS DISTINCT FROM c.concurrency_version OR c.review_status IS DISTINCT FROM 'approved'))))
            ) rows WHERE $1::uuid IS NOT NULL ORDER BY kind,id LIMIT 10001
            """,connection,tx);
        foreach(var value in new object[]{window.OrganizationId,window.RestaurantId,window.BranchId,window.FromUtc.ToUniversalTime(),window.ToUtc.ToUniversalTime()})evidence.Parameters.AddWithValue(value);
        string? version=await NexaConnect.Infrastructure.Persistence.BoundedEvidenceHash.ReadAsync(evidence,
            $"pos-day-v1|{window.OrganizationId:D}|{window.RestaurantId:D}|{window.BranchId:D}|{window.FromUtc.UtcTicks}|{window.ToUtc.UtcTicks}",ct,retain);
        return result with{ObservedAtUtc=DateTimeOffset.UtcNow,EvidenceVersion=version};
    }
}
