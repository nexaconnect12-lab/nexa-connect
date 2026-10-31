using NexaConnect.Services.Catalog.Application.Menu;
using NexaConnect.Services.Catalog.Application.Organization;
using NexaConnect.Services.Catalog.Domain.Organization;
namespace NexaConnect.Services.Catalog.Infrastructure;
public sealed class InMemoryMenuOrganizationRepository(IMenuCatalog menu):IMenuOrganizationRepository
{
    private readonly object gate=new();private readonly Dictionary<(Guid,Guid),BranchMenuOrganizationState> rows=[];
    public Task<BranchMenuOrganizationState> ReadAsync(Guid org,Guid branch,CancellationToken ct){lock(gate)return Task.FromResult(rows.GetValueOrDefault((org,branch),BranchMenuOrganizationState.Empty()));}
    public Task<BranchMenuOrganizationState> ChangeAsync(Guid org,Guid branch,Func<BranchMenuOrganizationState,IReadOnlySet<Guid>,BranchMenuOrganizationState> change,MenuMutationContext actor,CancellationToken ct)
    {lock(gate){var products=menu.GetGuestMenu(org,branch);if(products.Count>200)throw new ArgumentException();var next=change(rows.GetValueOrDefault((org,branch),BranchMenuOrganizationState.Empty()),products.Select(x=>x.ProductId).ToHashSet());rows[(org,branch)]=next;return Task.FromResult(next);}}
}
