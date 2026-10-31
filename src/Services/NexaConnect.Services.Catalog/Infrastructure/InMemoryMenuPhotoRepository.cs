using NexaConnect.Contracts.Catalog;
using NexaConnect.Services.Catalog.Application.Menu;
using NexaConnect.Services.Catalog.Application.MenuPhotos;
using NexaConnect.Services.Catalog.Domain.Photos;
namespace NexaConnect.Services.Catalog.Infrastructure;
public sealed class InMemoryMenuPhotoRepository(IMenuCatalog menu) : IMenuPhotoRepository
{
    private readonly Dictionary<(Guid,Guid,Guid),MenuPhotoSelection> rows = [];
    private readonly object gate = new();
    public Task<MenuPhotoSelection?> ReadAsync(Guid org, Guid branch, Guid product, CancellationToken ct)
    { lock(gate) return Task.FromResult(Read(org,branch,product)); }
    private MenuPhotoSelection? Read(Guid org,Guid branch,Guid product) => menu.GetForOrganizationBranch(org,branch).Any(i=>i.ProductId==product) ? rows.GetValueOrDefault((org,branch,product),new(org,branch,product,null,0)) : null;
    public Task<MenuPhotoSelection> ChangeAsync(Guid org,Guid branch,Guid product,Func<MenuPhotoSelection,MenuPhotoChange> decide,MenuMutationContext actor,CancellationToken ct)
    { lock(gate){var before=Read(org,branch,product)??throw new KeyNotFoundException();var change=decide(before);var result=new MenuPhotoSelection(org,branch,product,change.AssetId,change.Version);rows[(org,branch,product)]=result;return Task.FromResult(result);} }
}
