using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Infrastructure.Persistence;
using NexaConnect.Services.Order.Application.Orders;
using NexaConnect.Services.Order.Domain;
using Npgsql;

namespace NexaConnect.Services.Order.Infrastructure.Persistence;

public sealed class PostgresOrderCutoffStore(NpgsqlDataSource source) : IOrderCutoffStore
{
    private readonly PostgresSnapshotRetention<OrderDaySummary> retention = new(source);
    public Task<SourceCutoff<OrderDaySummary>> CaptureAsync(SourceCutoffCommand command, string actor, CancellationToken ct) =>
        retention.CaptureAsync(command, actor, (c,t,token) => Snapshot(command.Window,c,t,token),ct);
    public async Task<SourceCutoffRead<OrderDaySummary>?> ReadAsync(EndOfDayWindow window, Guid id, CancellationToken ct)
    {
        var saved = await retention.ReadAsync(window,id,ct); if(saved is null) return null;
        await using var c=await source.OpenConnectionAsync(ct);
        await using var t=await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct);
        var current=await Snapshot(window,c,t,ct); await t.CommitAsync(ct);
        return new(saved, saved.EvidenceVersion.Length==64 && saved.EvidenceVersion==current.EvidenceVersion);
    }
    private static async Task<SourceCutoff<OrderDaySummary>> Snapshot(EndOfDayWindow w,NpgsqlConnection c,NpgsqlTransaction t,CancellationToken ct)
    {
        string[] retainedRows=[];var summary=await PostgresOrderDayReader.QueryAsync(w,c,t,ct,e=>retainedRows=e);
        var events=new List<OrderSaleCompletedV1>(); int count=0,gaps=0;
        await using var query=new NpgsqlCommand("""
            SELECT o.receipt_snapshot::text,o.created_at_utc,o.payment_intent_id,s.id,o.channel,o.service_type,
              o.workflow_correlation_id,p.payload::text
            FROM orders o LEFT JOIN order_manual_tender_settlements s ON s.order_id=o.id
            LEFT JOIN order_sale_publications p ON p.order_id=o.id
            WHERE o.organization_id=$1 AND o.restaurant_id=$2 AND o.branch_id=$3 AND o.status='completed'
              AND ((o.created_at_utc>=$4 AND o.created_at_utc<$5)
                OR (COALESCE((o.receipt_snapshot->>'PaidAtUtc')::timestamptz,o.updated_at_utc)>=$4
                  AND COALESCE((o.receipt_snapshot->>'PaidAtUtc')::timestamptz,o.updated_at_utc)<$5))
            ORDER BY o.id LIMIT 10001
            """,c,t);
        foreach(var value in new object[]{w.OrganizationId,w.RestaurantId,w.BranchId,w.FromUtc.ToUniversalTime(),w.ToUtc.ToUniversalTime()})query.Parameters.AddWithValue(value);
        await using(var rows=await query.ExecuteReaderAsync(ct))
        while(await rows.ReadAsync(ct))
        {
            if(++count>10000)throw new InvalidOperationException("Order cutoff inventory exceeds bound.");
            try
            {
                if(rows.IsDBNull(0)||rows.IsDBNull(7)){gaps++;continue;}
                var receipt=JsonSerializer.Deserialize<PaidOrderReceipt>(rows.GetString(0))??throw new JsonException();
                var expected=SaleCompletionEvents.Create(receipt,rows.GetFieldValue<DateTimeOffset>(1),rows.IsDBNull(2)?null:rows.GetGuid(2),
                    rows.IsDBNull(3)?null:rows.GetGuid(3),rows.GetString(4),rows.GetString(5),rows.IsDBNull(6)?null:rows.GetGuid(6));
                var retained=JsonSerializer.Deserialize<OrderSaleCompletedV1>(rows.GetString(7));
                if(retained!=expected)throw new InvalidOperationException();
                events.Add(expected);
            }
            catch(Exception e)when(e is JsonException or ArgumentException or InvalidOperationException or NullReferenceException){gaps++;}
        }
        summary=summary with{EvidenceGaps=Math.Max(summary.EvidenceGaps,gaps)};
        return new(Guid.Empty,Guid.Empty,0,w,DateTimeOffset.UtcNow,summary.EvidenceVersion??"",summary,events,[],retainedRows);
    }
}
