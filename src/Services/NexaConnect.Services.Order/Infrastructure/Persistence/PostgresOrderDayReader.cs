using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.Order.Application.Orders;
using Npgsql;

namespace NexaConnect.Services.Order.Infrastructure.Persistence;

public sealed class PostgresOrderDayReader(NpgsqlDataSource source) : IOrderDayReader
{
    public async Task<OrderDaySummary> ReadAsync(EndOfDayWindow window, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var result = await QueryAsync(window, connection, transaction, ct);
        await transaction.CommitAsync(ct); return result;
    }
    public static async Task<OrderDaySummary> QueryAsync(EndOfDayWindow window, NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct, Action<string[]>? retain = null)
    {
        await using var command = new NpgsqlCommand("""
            WITH scoped_orders AS (
              SELECT *, COALESCE((receipt_snapshot->>'PaidAtUtc')::timestamptz,completed_at_utc,updated_at_utc) AS paid_at
              FROM orders WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 AND created_at_utc<$5
            )
            SELECT COALESCE(sum(total_amount) FILTER(WHERE status='completed' AND created_at_utc>=$4),0),
              count(*) FILTER(WHERE status='completed' AND created_at_utc>=$4)::int,
              count(*) FILTER(WHERE status NOT IN ('completed','cancelled'))::int,
              count(*) FILTER(WHERE status='completed' AND
                (created_at_utc>=$4 OR (paid_at>=$4 AND paid_at<$5)) AND
                (receipt_snapshot IS NULL OR NOT EXISTS(SELECT 1 FROM order_sale_publications p WHERE p.order_id=scoped_orders.id)))::int,
              COALESCE(array_agg(DISTINCT btrim(currency)) FILTER(WHERE status='completed' AND
                (created_at_utc>=$4 OR (paid_at>=$4 AND paid_at<$5))), ARRAY[]::text[])
            FROM scoped_orders
            """, connection, transaction);
        Add(command, window);
        decimal gross; int completed, unresolved, gaps; string[] currencies;
        await using (var rows = await command.ExecuteReaderAsync(ct))
        {
            await rows.ReadAsync(ct); gross = rows.GetDecimal(0); completed = rows.GetInt32(1);
            unresolved = rows.GetInt32(2); gaps = rows.GetInt32(3); currencies = rows.GetFieldValue<string[]>(4);
        }
        await using var tenders = new NpgsqlCommand("""
            SELECT receipt_snapshot->>'Tender', btrim(currency), sum(total_amount)
            FROM orders WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 AND status='completed'
              AND (receipt_snapshot->>'PaidAtUtc')::timestamptz >= $4
              AND (receipt_snapshot->>'PaidAtUtc')::timestamptz < $5
            GROUP BY receipt_snapshot->>'Tender', btrim(currency) ORDER BY 1,2
            """, connection, transaction);
        Add(tenders, window); var totals = new List<TenderTotal>();
        await using (var rows = await tenders.ExecuteReaderAsync(ct))
            while (await rows.ReadAsync(ct)) totals.Add(new(rows.GetString(0), rows.GetString(1), rows.GetDecimal(2)));
        await using var evidence=new NpgsqlCommand("""
            SELECT jsonb_build_array(o.id,o.status,o.created_at_utc,o.completed_at_utc,o.updated_at_utc,
              o.total_amount,o.currency,o.receipt_snapshot,p.event_id,p.payload)::text
            FROM orders o LEFT JOIN order_sale_publications p ON p.order_id=o.id
            WHERE o.organization_id=$1 AND o.restaurant_id=$2 AND o.branch_id=$3
              AND ((o.created_at_utc<$5 AND o.status NOT IN ('completed','cancelled'))
                OR (o.status='completed' AND ((o.created_at_utc>=$4 AND o.created_at_utc<$5)
                OR ((o.receipt_snapshot->>'PaidAtUtc')::timestamptz>=$4 AND (o.receipt_snapshot->>'PaidAtUtc')::timestamptz<$5))))
            ORDER BY o.id LIMIT 10001
            """,connection,transaction);
        Add(evidence,window);
        string? version=await NexaConnect.Infrastructure.Persistence.BoundedEvidenceHash.ReadAsync(evidence,
            $"order-day-v1|{window.OrganizationId:D}|{window.RestaurantId:D}|{window.BranchId:D}|{window.FromUtc.UtcTicks}|{window.ToUtc.UtcTicks}",ct,retain);

        return new(window, DateTimeOffset.UtcNow, gross, completed, unresolved, gaps, totals, currencies,version);
    }
    private static void Add(NpgsqlCommand command, EndOfDayWindow window)
    {
        command.Parameters.AddWithValue(window.OrganizationId); command.Parameters.AddWithValue(window.RestaurantId);
        command.Parameters.AddWithValue(window.BranchId); command.Parameters.AddWithValue(window.FromUtc.ToUniversalTime());
        command.Parameters.AddWithValue(window.ToUtc.ToUniversalTime());
    }
}
