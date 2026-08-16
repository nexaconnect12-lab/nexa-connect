using System.Collections.Concurrent;
using NexaConnect.Services.Catalog.Application.Menu;

namespace NexaConnect.Services.Catalog.Infrastructure;

public sealed class InMemoryMenuCatalog : IMenuCatalog
{
    private readonly ConcurrentDictionary<(Guid OrganizationId, Guid BranchId), ConcurrentDictionary<Guid, MenuItem>> menus = new();

    public IReadOnlyCollection<MenuItem> GetForBranch(Guid branchId) =>
        GetForOrganizationBranch(Guid.Empty, branchId);

    public IReadOnlyCollection<MenuItem> GetForOrganizationBranch(Guid organizationId, Guid branchId) =>
        menus.TryGetValue((organizationId, branchId), out ConcurrentDictionary<Guid, MenuItem>? items)
            ? items.Values.OrderBy(item => item.Name).ToArray()
            : [];

    public MenuItem Add(Guid branchId, CreateMenuItem command, MenuMutationContext? context = null) => AddForOrganizationBranch(Guid.Empty, branchId, command, context);

    public Task<bool> ProductExistsAsync(Guid organizationId, Guid productId, CancellationToken cancellationToken) =>
        Task.FromResult(menus.Where(pair => pair.Key.OrganizationId == organizationId).Any(pair => pair.Value.ContainsKey(productId)));

    public MenuItem AddForOrganizationBranch(Guid organizationId, Guid branchId, CreateMenuItem command, MenuMutationContext? context = null)
    {
        if (command.ProductId == Guid.Empty || string.IsNullOrWhiteSpace(command.Name) || command.UnitPrice < 0)
            throw new ArgumentException("A valid product, name, and non-negative price are required.");
        var item = new MenuItem(command.ProductId, command.Name.Trim(), command.UnitPrice,
            command.Currency.Trim().ToUpperInvariant(), command.PreparationStation.Trim(), true);
        menus.GetOrAdd((organizationId, branchId), _ => new()).AddOrUpdate(item.ProductId, item, (_, _) => item);
        return item;
    }
}
