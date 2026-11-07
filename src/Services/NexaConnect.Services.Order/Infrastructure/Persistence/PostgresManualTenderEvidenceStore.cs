using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Contracts.POS;
using NexaConnect.Services.Order.Application.ManualTenders;
using NexaConnect.Services.Order.Domain;
using Npgsql;
namespace NexaConnect.Services.Order.Infrastructure.Persistence;
public sealed class PostgresManualTenderEvidenceStore(NpgsqlDataSource source):IManualTenderEvidenceStore
{
 public async Task<ManualRefundSaleV1?> ReadRefundSaleAsync(Guid organization, Guid restaurant, Guid branch, Guid order, CancellationToken ct)
 {
     await using var query = source.CreateCommand("""
         SELECT s.id, s.method, s.amount, btrim(s.currency), s.occurred_at_utc, o.receipt_snapshot::text
         FROM order_manual_tender_settlements s JOIN orders o
             ON o.id=s.order_id AND o.organization_id=s.organization_id AND o.branch_id=s.branch_id
         WHERE s.organization_id=$1 AND o.restaurant_id=$2 AND s.branch_id=$3 AND s.order_id=$4
             AND o.status='completed' AND o.payment_intent_id IS NULL
             AND o.total_amount=s.amount AND o.currency=s.currency
         """);
     foreach (Guid id in new[] { organization, restaurant, branch, order }) query.Parameters.AddWithValue(id);
     await using var rows = await query.ExecuteReaderAsync(ct);
     if (!await rows.ReadAsync(ct) || rows.IsDBNull(5)) return null;
     var sale = new ManualRefundSaleV1(organization, restaurant, branch, order, rows.GetGuid(0), rows.GetString(1),
         rows.GetDecimal(2), rows.GetString(3), rows.GetFieldValue<DateTimeOffset>(4));
     var receipt = JsonSerializer.Deserialize<PaidOrderReceipt>(rows.GetString(5));
     if (receipt is null || receipt.OrganizationId != organization || receipt.RestaurantId != restaurant
         || receipt.BranchId != branch || receipt.OrderId != order || receipt.Tender != sale.Method
         || receipt.TotalAmount != sale.Amount || receipt.Currency != sale.Currency
         || receipt.PaidAtUtc.UtcTicks / 10 != sale.PaidAtUtc.UtcTicks / 10)
         throw new InvalidOperationException("Original refund sale evidence is inconsistent.");
     return sale;
 }

 public async Task<OrderManualTenderSettledV1?> ReadAsync(Guid organization,Guid restaurant,Guid branch,Guid eventId,CancellationToken ct)
 {
  await using var q=source.CreateCommand("""
   SELECT m.payload::text,s.id,s.order_id,s.terminal_id,s.method,s.amount,btrim(s.currency),s.occurred_at_utc,o.receipt_snapshot::text
   FROM outbox_messages m JOIN order_manual_tender_settlements s ON s.order_id=m.aggregate_id
   JOIN orders o ON o.id=s.order_id AND o.organization_id=s.organization_id AND o.branch_id=s.branch_id
   WHERE m.id=$4 AND m.event_type='order.manual-tender-settled.v1' AND s.organization_id=$1 AND o.restaurant_id=$2 AND s.branch_id=$3
   """);
  foreach(var v in new[]{organization,restaurant,branch,eventId})q.Parameters.AddWithValue(v);
  await using var rows=await q.ExecuteReaderAsync(ct);if(!await rows.ReadAsync(ct))return null;
  var message=JsonSerializer.Deserialize<OrderManualTenderSettledV1>(rows.GetString(0),new JsonSerializerOptions(JsonSerializerDefaults.Web));
  var receipt=rows.IsDBNull(8)?null:JsonSerializer.Deserialize<PaidOrderReceipt>(rows.GetString(8));
  if(message is null||receipt is null||message.EventId!=eventId||message.OrganizationId!=organization||message.RestaurantId!=restaurant||message.BranchId!=branch
   ||message.SettlementId!=rows.GetGuid(1)||message.OrderId!=rows.GetGuid(2)||message.TerminalId!=rows.GetGuid(3)||message.Method!=rows.GetString(4)||message.Amount!=rows.GetDecimal(5)
   ||message.Currency!=rows.GetString(6)||message.OccurredAtUtc.UtcTicks/10!=rows.GetFieldValue<DateTimeOffset>(7).UtcTicks/10
   ||receipt.OrganizationId!=organization||receipt.RestaurantId!=restaurant||receipt.BranchId!=branch||receipt.OrderId!=message.OrderId||receipt.Tender!=message.Method||receipt.TotalAmount!=message.Amount||receipt.Currency!=message.Currency||receipt.PaidAtUtc!=message.OccurredAtUtc)
   throw new InvalidOperationException("Original tender evidence inconsistent.");
  if(await rows.ReadAsync(ct))throw new InvalidOperationException("Original tender evidence ambiguous.");return message;
 }
}
