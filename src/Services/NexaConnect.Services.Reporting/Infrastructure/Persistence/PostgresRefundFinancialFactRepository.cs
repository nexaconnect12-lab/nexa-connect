using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NexaConnect.Services.Reporting.Application;
using NexaConnect.Services.Reporting.Domain;
using Npgsql;

namespace NexaConnect.Services.Reporting.Infrastructure.Persistence;

public sealed class PostgresRefundFinancialFactRepository(NpgsqlDataSource dataSource) : IRefundFinancialFactRepository
{
    public async Task<bool> ProjectAsync(RefundFinancialFact fact, CancellationToken cancellationToken)
    {
        fact.Validate();
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(fact))));
        try
        {
            await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var receipt = new NpgsqlCommand(
                "INSERT INTO refund_fact_event_receipts(event_id,payload_hash) VALUES($1,$2) ON CONFLICT DO NOTHING", connection, transaction))
            {
                receipt.Parameters.AddWithValue(fact.SourceEventId);
                receipt.Parameters.AddWithValue(hash);
                if (await receipt.ExecuteNonQueryAsync(cancellationToken) == 0)
                {
                    await using var existing = new NpgsqlCommand(
                        "SELECT payload_hash FROM refund_fact_event_receipts WHERE event_id=$1", connection, transaction);
                    existing.Parameters.AddWithValue(fact.SourceEventId);
                    if (!string.Equals((string?)await existing.ExecuteScalarAsync(cancellationToken), hash, StringComparison.Ordinal))
                        throw new ArgumentException("Refund financial event identity conflict.");
                    await transaction.CommitAsync(cancellationToken);
                    return false;
                }
            }

            await using (var insert = new NpgsqlCommand("""
                INSERT INTO refund_facts(source_event_id,refund_id,organization_id,restaurant_id,branch_id,order_id,
                  payment_intent_id,amount,currency,reason_code,refunded_at_utc,cumulative_refunded_amount,
                  captured_amount,receipt_number,projected_at_utc)
                VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,clock_timestamp())
                """, connection, transaction))
            {
                insert.Parameters.AddWithValue(fact.SourceEventId); insert.Parameters.AddWithValue(fact.RefundId);
                insert.Parameters.AddWithValue(fact.OrganizationId); insert.Parameters.AddWithValue(fact.RestaurantId);
                insert.Parameters.AddWithValue(fact.BranchId); insert.Parameters.AddWithValue(fact.OrderId);
                insert.Parameters.AddWithValue(fact.PaymentIntentId); insert.Parameters.AddWithValue(fact.Amount);
                insert.Parameters.AddWithValue(fact.Currency); insert.Parameters.AddWithValue(fact.ReasonCode);
                insert.Parameters.AddWithValue(fact.RefundedAtUtc); insert.Parameters.AddWithValue(fact.CumulativeRefundedAmount);
                insert.Parameters.AddWithValue(fact.CapturedAmount); insert.Parameters.AddWithValue(fact.ReceiptNumber);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var checkpoint = new NpgsqlCommand("""
                INSERT INTO projection_checkpoints(projector_name,source_stream,position,last_event_id,last_event_occurred_at_utc,updated_at_utc)
                VALUES('payment-refund-financial','payment.refunded.v1',1,$1,$2,clock_timestamp())
                ON CONFLICT(projector_name,source_stream) DO UPDATE SET
                  position=projection_checkpoints.position+1,last_event_id=EXCLUDED.last_event_id,
                  last_event_occurred_at_utc=EXCLUDED.last_event_occurred_at_utc,updated_at_utc=clock_timestamp()
                """, connection, transaction))
            {
                checkpoint.Parameters.AddWithValue(fact.SourceEventId);
                checkpoint.Parameters.AddWithValue(fact.RefundedAtUtc);
                await checkpoint.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ArgumentException("Refund financial identity is already associated with another event.", exception);
        }
    }
}
