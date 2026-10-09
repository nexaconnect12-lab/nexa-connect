using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.Payment.Application.Refunds;
using Npgsql;

namespace NexaConnect.Services.Payment.Infrastructure;

public sealed class PostgresPaymentDayReader(NpgsqlDataSource source) : IPaymentDayReader
{
    public async Task<PaymentDaySummary> ReadAsync(EndOfDayWindow window, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var tx=await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct);
        var result = await QueryAsync(window, connection, tx, ct);
        await tx.CommitAsync(ct); return result;
    }
    public static async Task<PaymentDaySummary> QueryAsync(EndOfDayWindow window, NpgsqlConnection connection, NpgsqlTransaction tx, CancellationToken ct, Action<string[]>? retain = null)
    {
        await using var command = new NpgsqlCommand("""
            WITH scoped_refunds AS (
              SELECT * FROM refunds WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3
                AND requested_at_utc<$5
            )
            SELECT COALESCE(sum(amount) FILTER(WHERE status='completed' AND completed_at_utc>=$4 AND completed_at_utc<$5),0),
              (SELECT count(*)::int FROM payment_intents WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3
                AND created_at_utc<$5 AND status NOT IN ('captured','failed','cancelled','expired','voided')),
              count(*) FILTER(WHERE status NOT IN ('completed','failed'))::int,
              count(*) FILTER(WHERE status='completed' AND completed_at_utc>=$4 AND completed_at_utc<$5 AND
                (receipt_snapshot IS NULL OR NOT EXISTS(SELECT 1 FROM refund_financial_publications p WHERE p.refund_id=scoped_refunds.id)))::int,
              COALESCE(array_agg(DISTINCT btrim(currency)) FILTER(WHERE status='completed' AND completed_at_utc>=$4 AND completed_at_utc<$5),ARRAY[]::text[])
            FROM scoped_refunds
            """, connection,tx);
        command.Parameters.AddWithValue(window.OrganizationId); command.Parameters.AddWithValue(window.RestaurantId);
        command.Parameters.AddWithValue(window.BranchId); command.Parameters.AddWithValue(window.FromUtc.ToUniversalTime());
        command.Parameters.AddWithValue(window.ToUtc.ToUniversalTime());
        PaymentDaySummary result;
        await using(var reader=await command.ExecuteReaderAsync(ct))
        {await reader.ReadAsync(ct);result=new(window,DateTimeOffset.UtcNow,reader.GetDecimal(0),reader.GetInt32(1),reader.GetInt32(2),reader.GetInt32(3),reader.GetFieldValue<string[]>(4));}
        await using var evidence=new NpgsqlCommand("""
            SELECT value FROM (
              SELECT 'intent' AS kind,i.id,jsonb_build_array('intent',i.id,i.status,i.concurrency_version,i.amount,i.currency,i.created_at_utc)::text AS value
              FROM payment_intents i WHERE i.organization_id=$1 AND i.restaurant_id=$2 AND i.branch_id=$3
                AND i.created_at_utc<$5 AND i.status NOT IN ('captured','failed','cancelled','expired','voided')
              UNION ALL
              SELECT 'refund',r.id,jsonb_build_array('refund',r.id,r.status,r.concurrency_version,r.amount,r.currency,r.requested_at_utc,
                r.completed_at_utc,r.receipt_snapshot,p.event_id,p.payload)::text
              FROM refunds r LEFT JOIN refund_financial_publications p ON p.refund_id=r.id
              WHERE r.organization_id=$1 AND r.restaurant_id=$2 AND r.branch_id=$3 AND r.requested_at_utc<$5
                AND (r.status NOT IN ('completed','failed') OR (r.status='completed' AND r.completed_at_utc>=$4 AND r.completed_at_utc<$5))
            ) rows ORDER BY kind,id LIMIT 10001
            """,connection,tx);
        foreach(var value in new object[]{window.OrganizationId,window.RestaurantId,window.BranchId,window.FromUtc.ToUniversalTime(),window.ToUtc.ToUniversalTime()})evidence.Parameters.AddWithValue(value);
        string? version=await NexaConnect.Infrastructure.Persistence.BoundedEvidenceHash.ReadAsync(evidence,
            $"payment-day-v1|{window.OrganizationId:D}|{window.RestaurantId:D}|{window.BranchId:D}|{window.FromUtc.UtcTicks}|{window.ToUtc.UtcTicks}",ct,retain);
        return result with{ObservedAtUtc=DateTimeOffset.UtcNow,EvidenceVersion=version};
    }
}
