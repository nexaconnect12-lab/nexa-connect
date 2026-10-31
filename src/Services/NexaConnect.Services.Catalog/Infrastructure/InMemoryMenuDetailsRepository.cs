using NexaConnect.Contracts.Catalog;
using NexaConnect.Services.Catalog.Application.Details;
using NexaConnect.Services.Catalog.Application.Menu;
namespace NexaConnect.Services.Catalog.Infrastructure;
public sealed class InMemoryMenuDetailsRepository(IMenuCatalog menu):IMenuDetailsRepository
{
    private InMemoryMenuCatalog Memory=>menu as InMemoryMenuCatalog??throw new InvalidOperationException("In-memory menu required.");
    public Task<IReadOnlyList<EditableMenuItem>> ReadAsync(Guid org,Guid branch,CancellationToken ct)=>Task.FromResult(Memory.ReadDetails(org,branch));
    public Task<EditableMenuItem?> ChangeAsync(Guid org,Guid branch,Guid product,UpdateMenuDetails input,MenuMutationContext actor,CancellationToken ct)=>Task.FromResult(Memory.ChangeDetails(org,branch,product,input));
}
