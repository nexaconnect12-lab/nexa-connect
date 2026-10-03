using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.POS.Application.CashReviews;
using Npgsql;

namespace NexaConnect.Services.POS.Infrastructure.Persistence;

public sealed class PostgresPosDayReader(NpgsqlDataSource source) : IPosDayReader
{
    public async Task<PosDaySummary> ReadAsync(EndOfDayWindow window, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
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
            """, connection);
        command.Parameters.AddWithValue(window.OrganizationId); command.Parameters.AddWithValue(window.RestaurantId);
        command.Parameters.AddWithValue(window.BranchId); command.Parameters.AddWithValue(window.FromUtc.ToUniversalTime());
        command.Parameters.AddWithValue(window.ToUtc.ToUniversalTime());
        await using var reader = await command.ExecuteReaderAsync(ct); await reader.ReadAsync(ct);
        return new(window, DateTimeOffset.UtcNow, reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetDecimal(3), reader.GetFieldValue<string[]>(4));
    }
}
