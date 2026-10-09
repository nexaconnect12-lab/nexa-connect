using System.Text.Json;
using NexaConnect.Services.Order.Application.Orders;
using NexaConnect.Services.Order.Domain;
using Npgsql;

namespace NexaConnect.Services.Order.Infrastructure.Persistence;

/// <summary>Reads committed commercial evidence under the order transaction; never rebuilds a receipt.</summary>
public static class PostgresSalePublication
{
    public static async Task<int> RequeueAsync(NpgsqlConnection connection,NpgsqlTransaction transaction,Guid id,
        Guid organization,Guid branch,Guid runId,string actor,CancellationToken cancellationToken)
    {
        await using var replay = new NpgsqlCommand("""
            INSERT INTO outbox_messages(id,event_type,contract_version,aggregate_type,aggregate_id,payload,correlation_id,occurred_at_utc)
            SELECT event_id,'order.sale-completed.v1',1,'Order',order_id,payload,payload->>'CorrelationId',paid_at_utc
            FROM order_sale_publications WHERE order_id=$1 AND organization_id=$2 AND branch_id=$3
            ON CONFLICT(id) DO UPDATE SET published_at_utc=NULL,retry_count=0,next_attempt_at_utc=NULL,last_error_category=NULL
            WHERE outbox_messages.event_type='order.sale-completed.v1' AND outbox_messages.aggregate_id=EXCLUDED.aggregate_id
              AND outbox_messages.payload=EXCLUDED.payload AND outbox_messages.contract_version=EXCLUDED.contract_version
              AND outbox_messages.aggregate_type=EXCLUDED.aggregate_type AND outbox_messages.correlation_id=EXCLUDED.correlation_id
              AND outbox_messages.occurred_at_utc=EXCLUDED.occurred_at_utc
            """,connection,transaction);
        replay.Parameters.AddWithValue(id); replay.Parameters.AddWithValue(organization); replay.Parameters.AddWithValue(branch);
        int affected = await replay.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
        {
            await using var retained = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM order_sale_publications WHERE order_id=$1)",connection,transaction);
            retained.Parameters.AddWithValue(id);
            if ((bool)(await retained.ExecuteScalarAsync(cancellationToken))!)
                throw new InvalidOperationException("Retained sale publication conflicts with outbox evidence.");
            return 0;
        }
        await using var audit = new NpgsqlCommand("INSERT INTO order_sale_replay_audit(run_id,order_id,organization_id,branch_id,actor) VALUES($1,$2,$3,$4,$5)",connection,transaction);
        audit.Parameters.AddWithValue(runId); audit.Parameters.AddWithValue(id); audit.Parameters.AddWithValue(organization);
        audit.Parameters.AddWithValue(branch); audit.Parameters.AddWithValue(actor);
        await audit.ExecuteNonQueryAsync(cancellationToken);
        return affected;
    }

    public static async Task<bool> EnsureAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid orderId, CancellationToken cancellationToken)
    {
        string? receiptJson; DateTimeOffset orderedAt; Guid? intent, settlement, correlation;
        string channel, service;
        await using (var read = new NpgsqlCommand("""
            SELECT o.receipt_snapshot::text,o.created_at_utc,o.payment_intent_id,s.id,
              o.channel,o.service_type,o.workflow_correlation_id
            FROM orders o LEFT JOIN order_manual_tender_settlements s ON s.order_id=o.id
            WHERE o.id=$1 AND o.status='completed' FOR UPDATE OF o
            """, connection, transaction))
        {
            read.Parameters.AddWithValue(orderId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0)) return false;
            receiptJson = reader.GetString(0); orderedAt = reader.GetFieldValue<DateTimeOffset>(1);
            intent = reader.IsDBNull(2) ? null : reader.GetGuid(2); settlement = reader.IsDBNull(3) ? null : reader.GetGuid(3);
            channel = reader.GetString(4); service = reader.GetString(5);
            correlation = reader.IsDBNull(6) ? null : reader.GetGuid(6);
        }
        var receipt = JsonSerializer.Deserialize<PaidOrderReceipt>(receiptJson)
            ?? throw new InvalidOperationException("Receipt evidence is unreadable.");
        var value = SaleCompletionEvents.Create(receipt, orderedAt, intent, settlement, channel, service, correlation);
        string payload = JsonSerializer.Serialize(value);
        await using var insert = new NpgsqlCommand("""
            INSERT INTO order_sale_publications(order_id,event_id,organization_id,branch_id,paid_at_utc,payload)
            VALUES($1,$2,$3,$4,$5,$6::jsonb) ON CONFLICT(order_id) DO NOTHING
            """, connection, transaction);
        insert.Parameters.AddWithValue(orderId); insert.Parameters.AddWithValue(value.EventId);
        insert.Parameters.AddWithValue(value.OrganizationId); insert.Parameters.AddWithValue(value.BranchId);
        insert.Parameters.AddWithValue(value.PaidAtUtc); insert.Parameters.AddWithValue(payload);
        if (await insert.ExecuteNonQueryAsync(cancellationToken) == 0) return false;
        await using var enqueue = new NpgsqlCommand("""
            INSERT INTO outbox_messages(id,event_type,contract_version,aggregate_type,aggregate_id,payload,correlation_id,occurred_at_utc)
            VALUES($1,'order.sale-completed.v1',1,'Order',$2,$3::jsonb,$4,$5)
            """, connection, transaction);
        enqueue.Parameters.AddWithValue(value.EventId); enqueue.Parameters.AddWithValue(orderId);
        enqueue.Parameters.AddWithValue(payload); enqueue.Parameters.AddWithValue(value.CorrelationId.ToString("D"));
        enqueue.Parameters.AddWithValue(value.PaidAtUtc);
        await enqueue.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }
}
