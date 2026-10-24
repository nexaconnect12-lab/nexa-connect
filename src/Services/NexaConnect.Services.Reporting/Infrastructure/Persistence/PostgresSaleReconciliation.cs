using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NexaConnect.Services.Reporting.Application;
using NexaConnect.Services.Reporting.Domain;
using Npgsql;

namespace NexaConnect.Services.Reporting.Infrastructure.Persistence;

public sealed record SaleReconciliationResult(int Expected, int Matched, int Missing, int Conflicting);

public sealed class PostgresSaleReconciliation(NpgsqlDataSource dataSource)
{
    public async Task<SaleReconciliationResult> CheckAsync(IReadOnlyList<SaleFinancialFact> expected, CancellationToken cancellationToken)
    {
        int matched = 0, missing = 0, conflicting = 0;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken);
        foreach (var value in expected)
        {
            var fact = value.Canonicalize();
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(fact))));
            await using var query = new NpgsqlCommand("""
                SELECT EXISTS(SELECT 1 FROM sales_facts WHERE order_id=$1),
                  EXISTS(SELECT 1 FROM sales_facts s JOIN payment_facts p ON p.order_id=s.order_id
                    JOIN sale_fact_event_receipts r ON r.event_id=s.source_event_id
                    WHERE s.order_id=$1 AND s.source_event_id=$2 AND p.source_event_id=$2 AND r.payload_hash=$3
                    AND s.organization_id=$4 AND p.organization_id=$4 AND s.restaurant_id=$5 AND p.restaurant_id=$5
                    AND s.branch_id=$6 AND p.branch_id=$6 AND s.currency=$7 AND p.currency=$7
                    AND s.channel=$8 AND s.service_type=$9 AND s.subtotal_amount=$10 AND s.discount_amount=0
                    AND s.service_charge_amount=$11 AND s.tax_amount=$12 AND s.total_amount=$13 AND p.paid_amount=$13
                    AND s.ordered_at_utc=$14 AND s.completed_at_utc=$15 AND p.paid_at_utc=$15
                    AND p.payment_intent_id=$16 AND p.payment_origin=$17 AND p.payment_method=$18
                    AND s.order_status='completed' AND p.payment_status='paid')
                """, connection, transaction);
            query.Parameters.AddWithValue(fact.OrderId); query.Parameters.AddWithValue(fact.SourceEventId);
            query.Parameters.AddWithValue(hash); query.Parameters.AddWithValue(fact.OrganizationId);
            query.Parameters.AddWithValue(fact.RestaurantId); query.Parameters.AddWithValue(fact.BranchId);
            query.Parameters.AddWithValue(fact.Currency); query.Parameters.AddWithValue(fact.Channel);
            query.Parameters.AddWithValue(fact.ServiceType); query.Parameters.AddWithValue(fact.SubtotalAmount);
            query.Parameters.AddWithValue(fact.ServiceChargeAmount); query.Parameters.AddWithValue(fact.TaxAmount);
            query.Parameters.AddWithValue(fact.TotalAmount); query.Parameters.AddWithValue(fact.OrderedAtUtc);
            query.Parameters.AddWithValue(fact.PaidAtUtc); query.Parameters.AddWithValue(fact.PaymentId);
            query.Parameters.AddWithValue(fact.PaymentOrigin); query.Parameters.AddWithValue(fact.Method);
            await using var reader = await query.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            if (reader.GetBoolean(1)) matched++; else if (!reader.GetBoolean(0)) missing++; else conflicting++;
        }
        await transaction.CommitAsync(cancellationToken);
        return new(expected.Count, matched, missing, conflicting);
    }
}
