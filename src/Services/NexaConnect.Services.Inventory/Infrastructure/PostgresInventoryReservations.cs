using Npgsql;
using NexaConnect.Services.Inventory.Application.Reservations;

namespace NexaConnect.Services.Inventory.Infrastructure;

public sealed class PostgresInventoryReservations(NpgsqlDataSource dataSource) : IInventoryReservations
{
    public IReadOnlyCollection<StockItem> GetStock(Guid branchId)
    {
        using var command = dataSource.CreateCommand("SELECT product_id,available_quantity FROM inventory_stock WHERE branch_id=@branch ORDER BY product_id"); command.Parameters.AddWithValue("branch", branchId);
        using var reader = command.ExecuteReader(); var result = new List<StockItem>(); while (reader.Read()) result.Add(new StockItem(reader.GetGuid(0), reader.GetDecimal(1))); return result;
    }
    public IReadOnlyCollection<StockItem> GetStock(Guid organizationId, Guid branchId)
    {
        using var command = dataSource.CreateCommand("SELECT product_id,available_quantity FROM inventory_stock WHERE organization_id=@organization AND branch_id=@branch ORDER BY product_id"); command.Parameters.AddWithValue("organization", organizationId); command.Parameters.AddWithValue("branch", branchId);
        using var reader = command.ExecuteReader(); var result = new List<StockItem>(); while (reader.Read()) result.Add(new StockItem(reader.GetGuid(0), reader.GetDecimal(1))); return result;
    }
    public StockItem SetStock(Guid branchId, Guid productId, decimal quantity)
    {
        if (branchId == Guid.Empty || productId == Guid.Empty || quantity < 0) throw new ArgumentException("Branch, product, and a non-negative quantity are required.");
        using var command = dataSource.CreateCommand("INSERT INTO inventory_stock (branch_id,product_id,available_quantity) VALUES (@branch,@product,@quantity) ON CONFLICT (branch_id,product_id) DO UPDATE SET available_quantity=EXCLUDED.available_quantity"); command.Parameters.AddWithValue("branch", branchId); command.Parameters.AddWithValue("product", productId); command.Parameters.AddWithValue("quantity", quantity); command.ExecuteNonQuery(); return new StockItem(productId, quantity);
    }
    public StockItem SetStock(Guid organizationId, Guid branchId, Guid productId, decimal quantity)
    {
        if (organizationId == Guid.Empty || branchId == Guid.Empty || productId == Guid.Empty || quantity < 0) throw new ArgumentException("Organization, branch, product, and a non-negative quantity are required.");
        using var command = dataSource.CreateCommand("INSERT INTO inventory_stock (organization_id,branch_id,product_id,available_quantity) VALUES (@organization,@branch,@product,@quantity) ON CONFLICT (organization_id,branch_id,product_id) DO UPDATE SET available_quantity=EXCLUDED.available_quantity"); command.Parameters.AddWithValue("organization", organizationId); command.Parameters.AddWithValue("branch", branchId); command.Parameters.AddWithValue("product", productId); command.Parameters.AddWithValue("quantity", quantity); command.ExecuteNonQuery(); return new StockItem(productId, quantity);
    }
    public StockReservation Reserve(ReserveStock command)
    {
        using var connection = dataSource.OpenConnection(); using var transaction = connection.BeginTransaction();
        foreach (var line in command.Lines)
        {
            using var update = new NpgsqlCommand("UPDATE inventory_stock SET available_quantity=available_quantity-@quantity WHERE branch_id=@branch AND product_id=@product AND available_quantity>=@quantity", connection, transaction); update.Parameters.AddWithValue("quantity", line.Quantity); update.Parameters.AddWithValue("branch", command.BranchId); update.Parameters.AddWithValue("product", line.ProductId); if (update.ExecuteNonQuery() != 1) throw new InvalidOperationException($"Insufficient stock for product {line.ProductId}.");
        }
        foreach (var line in command.Lines)
        {
            using var insert = new NpgsqlCommand("INSERT INTO inventory_reservation_lines (order_id,branch_id,product_id,quantity) VALUES (@order,@branch,@product,@quantity) ON CONFLICT (order_id,product_id) DO UPDATE SET quantity=EXCLUDED.quantity", connection, transaction); insert.Parameters.AddWithValue("order", command.OrderId); insert.Parameters.AddWithValue("branch", command.BranchId); insert.Parameters.AddWithValue("product", line.ProductId); insert.Parameters.AddWithValue("quantity", line.Quantity); insert.ExecuteNonQuery();
        }
        transaction.Commit(); return new StockReservation(Guid.NewGuid(), command.OrderId, command.BranchId, command.Lines);
    }
    public StockReservation Reserve(Guid organizationId, ReserveStock command)
    {
        using var connection = dataSource.OpenConnection(); using var transaction = connection.BeginTransaction();
        foreach (var line in command.Lines)
        {
            using var update = new NpgsqlCommand("UPDATE inventory_stock SET available_quantity=available_quantity-@quantity WHERE organization_id=@organization AND branch_id=@branch AND product_id=@product AND available_quantity>=@quantity", connection, transaction); update.Parameters.AddWithValue("quantity", line.Quantity); update.Parameters.AddWithValue("organization", organizationId); update.Parameters.AddWithValue("branch", command.BranchId); update.Parameters.AddWithValue("product", line.ProductId); if (update.ExecuteNonQuery() != 1) throw new InvalidOperationException($"Insufficient stock for product {line.ProductId}.");
        }
        foreach (var line in command.Lines)
        {
            using var insert = new NpgsqlCommand("INSERT INTO inventory_reservation_lines (organization_id,order_id,branch_id,product_id,quantity) VALUES (@organization,@order,@branch,@product,@quantity) ON CONFLICT (organization_id,order_id,product_id) DO UPDATE SET quantity=EXCLUDED.quantity", connection, transaction); insert.Parameters.AddWithValue("organization", organizationId); insert.Parameters.AddWithValue("order", command.OrderId); insert.Parameters.AddWithValue("branch", command.BranchId); insert.Parameters.AddWithValue("product", line.ProductId); insert.Parameters.AddWithValue("quantity", line.Quantity); insert.ExecuteNonQuery();
        }
        transaction.Commit(); return new StockReservation(Guid.NewGuid(), command.OrderId, command.BranchId, command.Lines);
    }
    public void Release(Guid orderId)
    {
        using var command = dataSource.CreateCommand("UPDATE inventory_stock s SET available_quantity = s.available_quantity + r.quantity FROM inventory_reservation_lines r WHERE r.order_id=@order AND r.branch_id=s.branch_id AND r.product_id=s.product_id AND r.released_at_utc IS NULL; UPDATE inventory_reservation_lines SET released_at_utc=now() WHERE order_id=@order AND released_at_utc IS NULL");
        command.Parameters.AddWithValue("order", orderId);
        command.ExecuteNonQuery();
    }
    public void Release(Guid organizationId, Guid orderId)
    {
        using var command = dataSource.CreateCommand("UPDATE inventory_stock s SET available_quantity=s.available_quantity+r.quantity FROM inventory_reservation_lines r WHERE r.organization_id=@organization AND r.order_id=@order AND s.organization_id=r.organization_id AND r.branch_id=s.branch_id AND r.product_id=s.product_id AND r.released_at_utc IS NULL; UPDATE inventory_reservation_lines SET released_at_utc=now() WHERE organization_id=@organization AND order_id=@order AND released_at_utc IS NULL");
        command.Parameters.AddWithValue("organization", organizationId); command.Parameters.AddWithValue("order", orderId); command.ExecuteNonQuery();
    }
}
