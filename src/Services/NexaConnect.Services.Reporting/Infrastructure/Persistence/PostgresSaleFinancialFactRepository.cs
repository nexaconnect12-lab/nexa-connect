using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NexaConnect.Services.Reporting.Application;
using NexaConnect.Services.Reporting.Domain;
using Npgsql;

namespace NexaConnect.Services.Reporting.Infrastructure.Persistence;

public sealed class PostgresSaleFinancialFactRepository(NpgsqlDataSource dataSource) : ISaleFinancialFactRepository
{
    public async Task<bool> ProjectAsync(SaleFinancialFact fact, CancellationToken cancellationToken)
    {
        fact = fact.Canonicalize();
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(fact))));
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var receipt = new NpgsqlCommand("INSERT INTO sale_fact_event_receipts(event_id,payload_hash) VALUES($1,$2) ON CONFLICT DO NOTHING", connection, transaction))
            {
                receipt.Parameters.AddWithValue(fact.SourceEventId); receipt.Parameters.AddWithValue(hash);
                if (await receipt.ExecuteNonQueryAsync(cancellationToken) == 0)
                {
                    await using var existing = new NpgsqlCommand("SELECT payload_hash FROM sale_fact_event_receipts WHERE event_id=$1", connection, transaction);
                    existing.Parameters.AddWithValue(fact.SourceEventId);
                    if ((string?)await existing.ExecuteScalarAsync(cancellationToken) != hash)
                        throw new ArgumentException("Sale financial event identity conflict.");
                    await transaction.CommitAsync(cancellationToken);
                    return false;
                }
            }
            await using (var sale = new NpgsqlCommand("""
                INSERT INTO sales_facts(order_id,source_event_id,organization_id,restaurant_id,branch_id,channel,service_type,
                  currency,subtotal_amount,discount_amount,service_charge_amount,tax_amount,total_amount,order_status,
                  ordered_at_utc,completed_at_utc,projected_at_utc,source_event_version)
                VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,0,$10,$11,$12,'completed',$13,$14,clock_timestamp(),1)
                """, connection, transaction))
            {
                sale.Parameters.AddWithValue(fact.OrderId); sale.Parameters.AddWithValue(fact.SourceEventId);
                sale.Parameters.AddWithValue(fact.OrganizationId); sale.Parameters.AddWithValue(fact.RestaurantId);
                sale.Parameters.AddWithValue(fact.BranchId); sale.Parameters.AddWithValue(fact.Channel);
                sale.Parameters.AddWithValue(fact.ServiceType); sale.Parameters.AddWithValue(fact.Currency);
                sale.Parameters.AddWithValue(fact.SubtotalAmount); sale.Parameters.AddWithValue(fact.ServiceChargeAmount);
                sale.Parameters.AddWithValue(fact.TaxAmount); sale.Parameters.AddWithValue(fact.TotalAmount);
                sale.Parameters.AddWithValue(fact.OrderedAtUtc); sale.Parameters.AddWithValue(fact.PaidAtUtc);
                await sale.ExecuteNonQueryAsync(cancellationToken);
            }
            await using (var payment = new NpgsqlCommand("""
                INSERT INTO payment_facts(payment_intent_id,source_event_id,organization_id,restaurant_id,branch_id,order_id,
                  payment_method,payment_origin,currency,paid_amount,refunded_amount,payment_status,paid_at_utc,projected_at_utc,source_event_version)
                VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,0,'paid',$11,clock_timestamp(),1)
                """, connection, transaction))
            {
                payment.Parameters.AddWithValue(fact.PaymentId); payment.Parameters.AddWithValue(fact.SourceEventId);
                payment.Parameters.AddWithValue(fact.OrganizationId); payment.Parameters.AddWithValue(fact.RestaurantId);
                payment.Parameters.AddWithValue(fact.BranchId); payment.Parameters.AddWithValue(fact.OrderId);
                payment.Parameters.AddWithValue(fact.Method); payment.Parameters.AddWithValue(fact.PaymentOrigin);
                payment.Parameters.AddWithValue(fact.Currency); payment.Parameters.AddWithValue(fact.TotalAmount);
                payment.Parameters.AddWithValue(fact.PaidAtUtc);
                await payment.ExecuteNonQueryAsync(cancellationToken);
            }
            await using (var checkpoint = new NpgsqlCommand("""
                INSERT INTO projection_checkpoints(projector_name,source_stream,position,last_event_id,last_event_occurred_at_utc,updated_at_utc)
                VALUES('order-sale-financial','order.sale-completed.v1',1,$1,$2,clock_timestamp())
                ON CONFLICT(projector_name,source_stream) DO UPDATE SET position=projection_checkpoints.position+1,
                  last_event_id=EXCLUDED.last_event_id,last_event_occurred_at_utc=EXCLUDED.last_event_occurred_at_utc,updated_at_utc=clock_timestamp()
                """, connection, transaction))
            {
                checkpoint.Parameters.AddWithValue(fact.SourceEventId); checkpoint.Parameters.AddWithValue(fact.PaidAtUtc);
                await checkpoint.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        { throw new ArgumentException("Sale or payment identity is already associated with another event.", exception); }
    }
}
