using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Order.Application.ManualTenders;
using NexaConnect.Services.Order.Domain;
using Npgsql;
namespace NexaConnect.Services.Order.Infrastructure.Persistence;
public sealed class PostgresManualTenderEvidenceStore(NpgsqlDataSource source):IManualTenderEvidenceStore
{
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
