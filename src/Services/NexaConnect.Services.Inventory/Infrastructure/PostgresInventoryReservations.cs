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
    public StockItem SetStock(Guid branchId, Guid productId, decimal quantity)
    {
        if (branchId == Guid.Empty || productId == Guid.Empty || quantity < 0) throw new ArgumentException("Branch, product, and a non-negative quantity are required.");
        using var command = dataSource.CreateCommand("INSERT INTO inventory_stock (branch_id,product_id,available_quantity) VALUES (@branch,@product,@quantity) ON CONFLICT (branch_id,product_id) DO UPDATE SET available_quantity=EXCLUDED.available_quantity"); command.Parameters.AddWithValue("branch", branchId); command.Parameters.AddWithValue("product", productId); command.Parameters.AddWithValue("quantity", quantity); command.ExecuteNonQuery(); return new StockItem(productId, quantity);
    }
    public StockReservation Reserve(ReserveStock command)
    {
        using var connection = dataSource.OpenConnection(); using var transaction = connection.BeginTransaction();
        foreach (var line in command.Lines)
        {
            using var update = new NpgsqlCommand("UPDATE inventory_stock SET available_quantity=available_quantity-@quantity WHERE branch_id=@branch AND product_id=@product AND available_quantity>=@quantity", connection, transaction); update.Parameters.AddWithValue("quantity", line.Quantity); update.Parameters.AddWithValue("branch", command.BranchId); update.Parameters.AddWithValue("product", line.ProductId); if (update.ExecuteNonQuery() != 1) throw new InvalidOperationException($"Insufficient stock for product {line.ProductId}.");
        }
        transaction.Commit(); return new StockReservation(Guid.NewGuid(), command.OrderId, command.BranchId, command.Lines);
    }
}
