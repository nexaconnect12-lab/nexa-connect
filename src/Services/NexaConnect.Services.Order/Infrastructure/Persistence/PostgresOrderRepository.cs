using System.Text.Json;
using Npgsql;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Order.Application.Workflow;
using NexaConnect.Services.Order.Domain;

namespace NexaConnect.Services.Order.Infrastructure.Persistence;

public sealed class PostgresOrderRepository(NpgsqlDataSource dataSource)
    : IOrderRepository, ITransactionalOrderRepository, IIdempotentOrderRepository
{
    public async Task SaveAsync(OrderAggregate order, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await SaveOrderAsync(connection, null, order, cancellationToken);
    }

    public async Task SaveWithEventAsync(OrderAggregate order, IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SaveOrderAsync(connection, transaction, order, cancellationToken);
        var payload = JsonSerializer.SerializeToDocument(integrationEvent, integrationEvent.GetType()).RootElement;
        await using var command = new NpgsqlCommand("""
            INSERT INTO outbox_messages (id,event_type,contract_version,aggregate_type,aggregate_id,payload,correlation_id,occurred_at_utc)
            VALUES (@id,@type,@version,@aggregate_type,@aggregate_id,@payload::jsonb,@correlation_id,@occurred_at)
            ON CONFLICT (id) DO NOTHING
            """, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("type", integrationEvent.GetType().Name);
        command.Parameters.AddWithValue("version", 1);
        command.Parameters.AddWithValue("aggregate_type", "Order");
        command.Parameters.AddWithValue("aggregate_id", order.Id);
        command.Parameters.AddWithValue("payload", payload.GetRawText());
        command.Parameters.AddWithValue("correlation_id", integrationEvent.CorrelationId.ToString());
        command.Parameters.AddWithValue("occurred_at", integrationEvent.OccurredAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<OrderAggregate?> FindByIdempotencyKeyAsync(Guid restaurantId, string key, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT resource_id FROM idempotency_records WHERE operation_scope = @scope AND idempotency_key = @key AND expires_at_utc > now()");
        command.Parameters.AddWithValue("scope", $"order:{restaurantId:N}");
        command.Parameters.AddWithValue("key", key);
        var resource = await command.ExecuteScalarAsync(cancellationToken);
        return resource is Guid id ? await GetAsync(id, cancellationToken) : null;
    }

    public async Task<OrderAggregate?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT restaurant_id, branch_id, currency, status, order_number, channel, service_type FROM orders WHERE id=@id", connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var restaurant = reader.GetGuid(0); var branch = reader.GetGuid(1); var currency = reader.GetString(2).Trim();
        var status = reader.GetString(3); var orderNumber = reader.GetString(4); var channel = reader.GetString(5); var serviceType = reader.GetString(6);
        await reader.CloseAsync();
        await using var linesCommand = new NpgsqlCommand("SELECT product_id, name_snapshot, unit_price, quantity, COALESCE(notes,'') FROM order_lines WHERE order_id=@id ORDER BY line_number", connection);
        linesCommand.Parameters.AddWithValue("id", id);
        var lines = new List<OrderLine>();
        await using var linesReader = await linesCommand.ExecuteReaderAsync(cancellationToken);
        while (await linesReader.ReadAsync(cancellationToken)) lines.Add(new OrderLine(linesReader.GetGuid(0), linesReader.GetString(1), linesReader.GetDecimal(2), (int)linesReader.GetDecimal(3), linesReader.GetString(4)));
        var order = OrderAggregate.Create(id, restaurant, branch, lines, currency, restaurant, channel, serviceType, orderNumber);
        ApplyStatus(order, status);
        return order;
    }

    private static async Task SaveOrderAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, OrderAggregate order, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO orders (id,restaurant_id,branch_id,order_number,currency,channel,service_type,subtotal_amount,total_amount,status,created_at_utc,created_by,updated_at_utc,updated_by)
            VALUES (@id,@restaurant,@branch,@number,@currency,@channel,@service,@subtotal,@total,@status,@now,'order-service',@now,'order-service')
            ON CONFLICT (id) DO UPDATE SET status=EXCLUDED.status,total_amount=EXCLUDED.total_amount,updated_at_utc=EXCLUDED.updated_at_utc,updated_by=EXCLUDED.updated_by,concurrency_version=orders.concurrency_version+1
            """, connection, transaction);
        var now = DateTime.UtcNow;
        command.Parameters.AddWithValue("id", order.Id); command.Parameters.AddWithValue("restaurant", order.RestaurantId); command.Parameters.AddWithValue("branch", order.BranchId);
        command.Parameters.AddWithValue("number", order.OrderNumber); command.Parameters.AddWithValue("currency", order.Currency); command.Parameters.AddWithValue("channel", order.Channel); command.Parameters.AddWithValue("service", order.ServiceType);
        command.Parameters.AddWithValue("subtotal", order.TotalAmount); command.Parameters.AddWithValue("total", order.TotalAmount); command.Parameters.AddWithValue("status", ToDbStatus(order.Status)); command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await using var delete = new NpgsqlCommand("DELETE FROM order_lines WHERE order_id=@id", connection, transaction); delete.Parameters.AddWithValue("id", order.Id); await delete.ExecuteNonQueryAsync(cancellationToken);
        for (var i = 0; i < order.Lines.Count; i++)
        {
            var line = order.Lines[i];
            await using var lineCommand = new NpgsqlCommand("INSERT INTO order_lines (id,restaurant_id,branch_id,order_id,line_number,product_id,sku_snapshot,name_snapshot,quantity,unit_price,line_total,status,created_at_utc,created_by,updated_at_utc,updated_by) VALUES (@id,@restaurant,@branch,@order,@number,@product,@sku,@name,@quantity,@unit,@total,'active',@now,'order-service',@now,'order-service')", connection, transaction);
            lineCommand.Parameters.AddWithValue("id", Guid.NewGuid()); lineCommand.Parameters.AddWithValue("restaurant", order.RestaurantId); lineCommand.Parameters.AddWithValue("branch", order.BranchId); lineCommand.Parameters.AddWithValue("order", order.Id); lineCommand.Parameters.AddWithValue("number", i + 1); lineCommand.Parameters.AddWithValue("product", line.ProductId); lineCommand.Parameters.AddWithValue("sku", line.ProductId.ToString("N")); lineCommand.Parameters.AddWithValue("name", line.Name); lineCommand.Parameters.AddWithValue("quantity", (decimal)line.Quantity); lineCommand.Parameters.AddWithValue("unit", line.UnitPrice); lineCommand.Parameters.AddWithValue("total", line.Total); lineCommand.Parameters.AddWithValue("now", now);
            await lineCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        if (!string.IsNullOrWhiteSpace(order.IdempotencyKey))
        {
            await using var idem = new NpgsqlCommand("INSERT INTO idempotency_records (operation_scope,idempotency_key,request_hash,response_status,response_body,resource_id,created_at_utc,expires_at_utc) VALUES (@scope,@key,'order',201,NULL,@resource,@now,@expires) ON CONFLICT DO NOTHING", connection, transaction);
            idem.Parameters.AddWithValue("scope", $"order:{order.RestaurantId:N}"); idem.Parameters.AddWithValue("key", order.IdempotencyKey); idem.Parameters.AddWithValue("resource", order.Id); idem.Parameters.AddWithValue("now", now); idem.Parameters.AddWithValue("expires", now.AddDays(1)); await idem.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static string ToDbStatus(OrderStatus status) => status switch { OrderStatus.Paid => "completed", OrderStatus.PaymentFailed or OrderStatus.Rejected => "cancelled", OrderStatus.KitchenAccepted => "accepted", OrderStatus.InventoryReserved => "accepted", _ => status.ToString().ToLowerInvariant() };
    private static void ApplyStatus(OrderAggregate order, string status) { if (status == "submitted") order.Submit(); else if (status == "accepted") { order.Submit(); order.MarkInventoryReserved(); order.MarkKitchenAccepted(); } else if (status == "completed") { order.Submit(); order.MarkInventoryReserved(); order.MarkKitchenAccepted(); order.MarkPaid(); } else if (status == "cancelled") order.Reject(); }
}
