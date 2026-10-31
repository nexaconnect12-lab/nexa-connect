using NexaConnect.Contracts.Catalog;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Catalog.Application.Menu;
using NexaConnect.Services.Catalog.Application.Tenant;
using NexaConnect.Services.Catalog.Domain.Organization;
namespace NexaConnect.Services.Catalog.Application.Organization;
public interface IMenuOrganizationRepository
{
    Task<BranchMenuOrganizationState> ReadAsync(Guid org,Guid branch,CancellationToken ct);
    Task<BranchMenuOrganizationState> ChangeAsync(Guid org,Guid branch,Func<BranchMenuOrganizationState,IReadOnlySet<Guid>,BranchMenuOrganizationState> change,MenuMutationContext actor,CancellationToken ct);
}
public sealed class MenuOrganizationService(IMenuOrganizationRepository repository,ICatalogTenantAuthorizer access,IMenuCatalog menu)
{
    private async Task Require(Guid org,Guid branch,string permission,string bearer,CancellationToken ct)
    {if(org==Guid.Empty||branch==Guid.Empty||!await access.HasBranchAccessAsync(org,branch,permission,bearer,ct))throw new UnauthorizedAccessException();}
    public async Task<BranchMenuOrganization> ReadAsync(Guid org,Guid branch,string? bearer,CancellationToken ct)
    {
        if(org==Guid.Empty||branch==Guid.Empty)throw new ArgumentException();
        if(bearer is not null)await Require(org,branch,ProductPermissions.CatalogMenuRead,bearer,ct);
        var state=await repository.ReadAsync(org,branch,ct);
        var products=menu.GetGuestMenu(org,branch);if(products.Count>200)throw new ArgumentException("Menu exceeds organization bounds.");
        var known=products.Select(x=>x.ProductId).ToHashSet();
        if(bearer is not null)await Require(org,branch,ProductPermissions.CatalogMenuRead,bearer,ct);
        return new(org,branch,state.Version,state.Categories.Select(x=>new MenuCategory(x.Id,x.Name)).ToArray(),state.Items.Where(x=>known.Contains(x.ProductId)).Select(x=>new MenuPlacement(x.ProductId,x.CategoryId)).ToArray());
    }
    public async Task<BranchMenuOrganization> SaveAsync(Guid org,Guid branch,UpdateMenuOrganization input,string bearer,MenuMutationContext actor,CancellationToken ct)
    {
        await Require(org,branch,ProductPermissions.CatalogMenuWrite,bearer,ct);
        if(input is null||input.Categories is null||input.Items is null||input.Categories.Count>50||input.Items.Count>200||input.Categories.Any(x=>x is null)||input.Items.Any(x=>x is null)
            ||string.IsNullOrWhiteSpace(actor.ActorSubjectId)||actor.ActorSubjectId.Length>256||actor.ActorSubjectId.Any(char.IsControl)||actor.CorrelationId==Guid.Empty)throw new ArgumentException();
        // Validate shape before database work; the repository supplies locked live product membership.
        var categories=input.Categories.Select(x=>new Category(x.Id,x.Name)).ToArray();var items=input.Items.Select(x=>new Placement(x.ProductId,x.CategoryId)).ToArray();
        _=BranchMenuOrganizationState.Restore(1,categories,items);
        await Require(org,branch,ProductPermissions.CatalogMenuWrite,bearer,ct);
        var result=await repository.ChangeAsync(org,branch,(current,products)=>current.Change(input.ExpectedVersion,categories,items,products),actor,ct);
        return new(org,branch,result.Version,result.Categories.Select(x=>new MenuCategory(x.Id,x.Name)).ToArray(),result.Items.Select(x=>new MenuPlacement(x.ProductId,x.CategoryId)).ToArray());
    }
}
