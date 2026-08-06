using Npgsql;
using NexaConnect.Services.Catalog.Application.Menu;

namespace NexaConnect.Services.Catalog.Infrastructure;

public sealed class PostgresMenuCatalog(NpgsqlDataSource dataSource) : IMenuCatalog
{
    public IReadOnlyCollection<MenuItem> GetForBranch(Guid branchId)
    {
        using var command = dataSource.CreateCommand("SELECT product_id,name,unit_price,currency,preparation_station,available FROM catalog_menu_items WHERE branch_id=@branch ORDER BY name");
        command.Parameters.AddWithValue("branch", branchId);
        using var reader = command.ExecuteReader(); var result = new List<MenuItem>();
        while (reader.Read()) result.Add(new MenuItem(reader.GetGuid(0), reader.GetString(1), reader.GetDecimal(2), reader.GetString(3), reader.GetString(4), reader.GetBoolean(5)));
        return result;
    }

    public MenuItem Add(Guid branchId, CreateMenuItem command)
    {
        if (branchId == Guid.Empty || command.ProductId == Guid.Empty || string.IsNullOrWhiteSpace(command.Name) || command.UnitPrice < 0) throw new ArgumentException("A valid branch, product, name, and non-negative price are required.");
        var item = new MenuItem(command.ProductId, command.Name.Trim(), command.UnitPrice, command.Currency.Trim().ToUpperInvariant(), command.PreparationStation.Trim(), true);
        using var sql = dataSource.CreateCommand("INSERT INTO catalog_menu_items (branch_id,product_id,name,unit_price,currency,preparation_station,available) VALUES (@branch,@product,@name,@price,@currency,@station,true) ON CONFLICT (branch_id,product_id) DO UPDATE SET name=EXCLUDED.name,unit_price=EXCLUDED.unit_price,currency=EXCLUDED.currency,preparation_station=EXCLUDED.preparation_station,available=true");
        sql.Parameters.AddWithValue("branch", branchId); sql.Parameters.AddWithValue("product", item.ProductId); sql.Parameters.AddWithValue("name", item.Name); sql.Parameters.AddWithValue("price", item.UnitPrice); sql.Parameters.AddWithValue("currency", item.Currency); sql.Parameters.AddWithValue("station", item.PreparationStation); sql.ExecuteNonQuery();
        return item;
    }
}
