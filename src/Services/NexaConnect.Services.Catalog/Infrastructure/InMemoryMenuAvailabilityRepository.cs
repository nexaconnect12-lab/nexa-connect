using NexaConnect.Contracts.Catalog;
using NexaConnect.Services.Catalog.Application.Availability;
using NexaConnect.Services.Catalog.Application.Menu;
using NexaConnect.Services.Catalog.Domain.Availability;

namespace NexaConnect.Services.Catalog.Infrastructure;

public sealed class InMemoryMenuAvailabilityRepository(IMenuCatalog menu) : IMenuAvailabilityRepository
{
    private readonly object gate = new();
    private readonly Dictionary<(Guid, Guid, Guid), long> versions = new();
    public Task<IReadOnlyList<MenuAvailabilityItem>> ReadAsync(Guid org, Guid branch, CancellationToken ct)
    {
        lock (gate) return Task.FromResult<IReadOnlyList<MenuAvailabilityItem>>(menu.GetGuestMenu(org, branch)
            .Select(p => new MenuAvailabilityItem(p.ProductId, p.Name, p.Available, versions.GetValueOrDefault((org, branch, p.ProductId)))).ToArray());
    }
    public Task<MenuAvailabilityItem?> ChangeAsync(Guid org, Guid branch, Guid product, UpdateMenuAvailability input, MenuMutationContext actor, CancellationToken ct)
    {
        lock (gate)
        {
            if (menu is not InMemoryMenuCatalog memory) throw new InvalidOperationException("In-memory menu provider required.");
            MenuAvailabilityItem? result = null;
            memory.ChangeAvailability(org, branch, product, item =>
            {
                var next = new MenuAvailabilityState(item.Available, versions.GetValueOrDefault((org, branch, product))).Change(input.Available ?? throw new ArgumentException(), input.ExpectedVersion ?? throw new ArgumentException());
                result = new(product, item.Name, next.Available, next.Version);
                return item with { Available = next.Available };
            });
            if (result is not null) versions[(org, branch, product)] = result.Version;
            return Task.FromResult(result);
        }
    }
}
