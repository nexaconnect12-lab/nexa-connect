using NexaConnect.Services.Order.Application.Orders;
using Npgsql;
using NpgsqlTypes;

namespace NexaConnect.Services.Order.Infrastructure.Persistence;

public sealed class PostgresOrderListRepository(NpgsqlDataSource source) : IOrderListRepository
{
    public async Task<IReadOnlyList<OrderListRow>> ReadAsync(OrderListScope scope, string channel, string state,
        OrderListCursor? cursor, int fetchLimit, CancellationToken ct)
    {
        // One bounded read snapshot; no expiry, claim, admission, audit or financial writes.
        await using var command = source.CreateCommand("""
            SELECT o.id,o.order_number,o.created_at_utc,o.channel,o.status,btrim(o.currency),o.total_amount,
                a.table_code,a.order_id IS NOT NULL,o.pricing_snapshot IS NOT NULL,o.payment_intent_id IS NOT NULL,
                c.order_id IS NOT NULL,(SELECT status FROM order_cancellations WHERE order_id=o.id),
                o.organization_id,o.restaurant_id,o.branch_id
            FROM orders o
            LEFT JOIN order_qr_acceptances a ON a.order_id=o.id AND a.organization_id=o.organization_id
                AND a.restaurant_id=o.restaurant_id AND a.branch_id=o.branch_id
            LEFT JOIN order_qr_collections c ON c.order_id=o.id AND c.organization_id=o.organization_id AND c.branch_id=o.branch_id
            WHERE o.organization_id=$1 AND o.restaurant_id=$2 AND o.branch_id=$3
                AND ($4='all' OR o.channel=$4)
                AND ($5='all' OR ($5='paid' AND o.status='completed') OR ($5='open' AND o.status NOT IN('completed','cancelled')))
                AND ($6::timestamptz IS NULL OR (o.created_at_utc,o.id)<($6,$7))
            ORDER BY o.created_at_utc DESC,o.id DESC LIMIT $8
            """);
        command.Parameters.AddWithValue(scope.OrganizationId);
        command.Parameters.AddWithValue(scope.RestaurantId);
        command.Parameters.AddWithValue(scope.BranchId);
        command.Parameters.AddWithValue(channel);
        command.Parameters.AddWithValue(state);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.TimestampTz, Value = cursor is null ? DBNull.Value : cursor.CreatedAtUtc });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = cursor is null ? DBNull.Value : cursor.OrderId });
        command.Parameters.AddWithValue(fetchLimit);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<OrderListRow>();
        while (await reader.ReadAsync(ct))
        {
            string status = reader.GetString(4) switch
            {
                "draft" => "Draft", "submitted" => "Submitted", "inventory_reserved" => "InventoryReserved",
                "accepted" or "kitchen_accepted" => "KitchenAccepted", "completed" => "Paid",
                "payment_pending" => "PaymentPending", "payment_review" => "PaymentReview",
                "cancellation_pending" => "CancellationPending", "cancellation_review" => "CancellationReview",
                "cancelled" => !reader.IsDBNull(12) && reader.GetString(12) == "completed" ? "Cancelled" : "Rejected",
                "preparing" => "Preparing", "ready" => "Ready",
                _ => throw new InvalidDataException("Unknown persisted Order state.")
            };
            rows.Add(new(reader.GetGuid(0), new(reader.GetGuid(13), reader.GetGuid(14), reader.GetGuid(15)), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2),
                reader.GetString(3), status, reader.GetString(5), reader.GetDecimal(6), reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetBoolean(8), reader.GetBoolean(9), reader.GetBoolean(10), reader.GetBoolean(11)));
        }
        return rows;
    }
}

public sealed class UnavailableOrderListRepository : IOrderListRepository
{
    public Task<IReadOnlyList<OrderListRow>> ReadAsync(OrderListScope scope, string channel, string state,
        OrderListCursor? cursor, int fetchLimit, CancellationToken ct) =>
        throw new InvalidOperationException("Durable branch order history requires PostgreSQL.");
}
