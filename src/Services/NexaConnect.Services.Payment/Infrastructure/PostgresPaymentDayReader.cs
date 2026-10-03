using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.Payment.Application.Refunds;
using Npgsql;

namespace NexaConnect.Services.Payment.Infrastructure;

public sealed class PostgresPaymentDayReader(NpgsqlDataSource source) : IPaymentDayReader
{
    public async Task<PaymentDaySummary> ReadAsync(EndOfDayWindow window, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
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
            """, connection);
        command.Parameters.AddWithValue(window.OrganizationId); command.Parameters.AddWithValue(window.RestaurantId);
        command.Parameters.AddWithValue(window.BranchId); command.Parameters.AddWithValue(window.FromUtc.ToUniversalTime());
        command.Parameters.AddWithValue(window.ToUtc.ToUniversalTime());
        await using var reader = await command.ExecuteReaderAsync(ct); await reader.ReadAsync(ct);
        return new(window, DateTimeOffset.UtcNow, reader.GetDecimal(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetFieldValue<string[]>(4));
    }
}
