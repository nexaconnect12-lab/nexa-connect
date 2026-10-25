using NexaConnect.Services.Order.Application.Orders;
using NexaConnect.Services.Order.Domain.Qr;
using NexaConnect.Contracts.IntegrationEvents;
using System.Text.Json;
using Npgsql;

namespace NexaConnect.Services.Order.Infrastructure.Persistence;

public sealed class PostgresQrCollectionRepository(NpgsqlDataSource source) : IQrCollectionRepository
{
    public async Task<QrCollectionBinding?> BindingAsync(Guid orderId, CancellationToken ct)
    {
        await using var command = source.CreateCommand("SELECT organization_id,branch_id,terminal_id,store_id,shift_id,settlement_key,actor FROM order_qr_collections WHERE order_id=$1");
        command.Parameters.AddWithValue(orderId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(orderId, reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
            reader.GetGuid(3), reader.GetGuid(4), reader.GetGuid(5), reader.GetString(6)) : null;
    }

    public async Task<bool> ClaimAsync(QrCollectionBinding binding, Guid decision, Guid correlation, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var check = new NpgsqlCommand("SELECT id FROM orders WHERE id=$1 AND organization_id=$2 AND branch_id=$3 AND channel='qr' AND status IN('accepted','kitchen_accepted') AND payment_intent_id IS NULL FOR UPDATE", connection, transaction))
        {
            check.Parameters.AddWithValue(binding.OrderId); check.Parameters.AddWithValue(binding.OrganizationId); check.Parameters.AddWithValue(binding.BranchId);
            if (await check.ExecuteScalarAsync(ct) is null) return false;
        }
        await using var command = new NpgsqlCommand("INSERT INTO order_qr_collections(order_id,organization_id,branch_id,terminal_id,store_id,shift_id,settlement_key,actor,decision_id,correlation_id) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10) ON CONFLICT DO NOTHING", connection, transaction);
        command.Parameters.AddWithValue(binding.OrderId); command.Parameters.AddWithValue(binding.OrganizationId); command.Parameters.AddWithValue(binding.BranchId);
        command.Parameters.AddWithValue(binding.TerminalId); command.Parameters.AddWithValue(binding.StoreId); command.Parameters.AddWithValue(binding.ShiftId);
        command.Parameters.AddWithValue(binding.SettlementKey); command.Parameters.AddWithValue(binding.Actor); command.Parameters.AddWithValue(decision); command.Parameters.AddWithValue(correlation);
        bool created = await command.ExecuteNonQueryAsync(ct) == 1;
        if (created)
        {
            var audit = new PlatformAuditEventV1(Guid.NewGuid(), correlation, DateTimeOffset.UtcNow, binding.Actor,
                binding.OrganizationId, "order.qr.collection-started", "order", binding.OrderId.ToString("D"), "succeeded");
            await using var insert = new NpgsqlCommand("INSERT INTO outbox_messages(id,event_type,contract_version,aggregate_type,aggregate_id,payload,correlation_id,occurred_at_utc) VALUES($1,'order.audit.v1',1,'Order',$2,$3::jsonb,$4,$5)", connection, transaction);
            insert.Parameters.AddWithValue(audit.EventId); insert.Parameters.AddWithValue(binding.OrderId);
            insert.Parameters.AddWithValue(JsonSerializer.Serialize(audit)); insert.Parameters.AddWithValue(correlation.ToString()); insert.Parameters.AddWithValue(audit.OccurredAtUtc);
            await insert.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return await BindingAsync(binding.OrderId, ct) == binding;
    }
}
