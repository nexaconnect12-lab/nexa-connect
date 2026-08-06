using System.Collections.Concurrent;
using NexaConnect.Services.Catalog.Application.Menu;

namespace NexaConnect.Services.Catalog.Infrastructure;

public sealed class InMemoryMenuCatalog : IMenuCatalog
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, MenuItem>> menus = new();

    public IReadOnlyCollection<MenuItem> GetForBranch(Guid branchId) =>
        menus.TryGetValue(branchId, out ConcurrentDictionary<Guid, MenuItem>? items)
            ? items.Values.OrderBy(item => item.Name).ToArray()
            : [];

    public MenuItem Add(Guid branchId, CreateMenuItem command)
    {
        if (command.ProductId == Guid.Empty || string.IsNullOrWhiteSpace(command.Name) || command.UnitPrice < 0)
            throw new ArgumentException("A valid product, name, and non-negative price are required.");
        var item = new MenuItem(command.ProductId, command.Name.Trim(), command.UnitPrice,
            command.Currency.Trim().ToUpperInvariant(), command.PreparationStation.Trim(), true);
        menus.GetOrAdd(branchId, _ => new()).AddOrUpdate(item.ProductId, item, (_, _) => item);
        return item;
    }
}
