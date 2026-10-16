using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Payment.Application.Refunds;
using Npgsql;

namespace NexaConnect.Services.Payment.Infrastructure;

public static class PostgresRefundFinancialPublication
{
    public static void Retain(NpgsqlConnection connection, NpgsqlTransaction transaction, PaymentRefund refund, PaymentRefundedV1 value)
    {
        RefundFinancialEvidence.Validate(refund, value);
        using var insert = new NpgsqlCommand("""
            INSERT INTO refund_financial_publications(refund_id,event_id,organization_id,branch_id,refunded_at_utc,payload)
            VALUES($1,$2,$3,$4,$5,$6::jsonb)
            """, connection, transaction);
        insert.Parameters.AddWithValue(refund.Id); insert.Parameters.AddWithValue(value.EventId);
        insert.Parameters.AddWithValue(refund.OrganizationId); insert.Parameters.AddWithValue(refund.BranchId);
        insert.Parameters.AddWithValue(value.OccurredAtUtc); insert.Parameters.AddWithValue(JsonSerializer.Serialize(value));
        insert.ExecuteNonQuery();
    }
}
