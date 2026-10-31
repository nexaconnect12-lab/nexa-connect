using System.Collections.Concurrent;
using NexaConnect.Services.Catalog.Application.Menu;
using NexaConnect.Contracts.Catalog;
using NexaConnect.Services.Catalog.Domain.Details;

namespace NexaConnect.Services.Catalog.Infrastructure;

public sealed class InMemoryMenuCatalog : IMenuCatalog
{
    private readonly ConcurrentDictionary<(Guid OrganizationId, Guid BranchId), ConcurrentDictionary<Guid, MenuItem>> menus = new();
    private readonly object mutation = new();
    private readonly Dictionary<(Guid,Guid,Guid),long> detailsVersions = new();

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
        lock(mutation)
        {
            var items=menus.GetOrAdd((organizationId,branchId),_=>new());
            if(items.TryGetValue(item.ProductId,out var previous))
            {
                item=item with{Available=previous.Available};
                if(item.Name!=previous.Name||item.UnitPrice!=previous.UnitPrice||item.Currency!=previous.Currency||item.PreparationStation!=previous.PreparationStation)
                    detailsVersions[(organizationId,branchId,item.ProductId)]=checked(detailsVersions.GetValueOrDefault((organizationId,branchId,item.ProductId))+1);
            }
            items[item.ProductId]=item;return item;
        }
    }

    internal void ChangeAvailability(Guid org, Guid branch, Guid product, Func<MenuItem, MenuItem> change)
    {
        lock(mutation)
        {
            if(menus.TryGetValue((org,branch),out var items)&&items.TryGetValue(product,out var previous))items[product]=change(previous);
        }
    }

    internal IReadOnlyList<EditableMenuItem> ReadDetails(Guid org,Guid branch)
    {lock(mutation)return GetGuestMenuValues(org,branch).Select(item=>Details(org,branch,item)).ToArray();}
    private IReadOnlyCollection<MenuItem> GetGuestMenuValues(Guid org,Guid branch)=>GetForOrganizationBranch(org,branch).Take(201).ToArray();
    private EditableMenuItem Details(Guid org,Guid branch,MenuItem item)=>new(item.ProductId,item.Name,item.UnitPrice,item.Currency,item.PreparationStation,item.Available,detailsVersions.GetValueOrDefault((org,branch,item.ProductId)));
    internal EditableMenuItem? ChangeDetails(Guid org,Guid branch,Guid product,UpdateMenuDetails input)
    {
        lock(mutation)
        {
            if(!menus.TryGetValue((org,branch),out var items)||!items.TryGetValue(product,out var previous))return null;
            var state=new MenuDetailsState(previous.Name,previous.UnitPrice,previous.Currency,previous.PreparationStation,detailsVersions.GetValueOrDefault((org,branch,product)));
            var next=state.Change(input.Name,input.UnitPrice,input.PreparationStation,input.ExpectedVersion);
            var item=previous with{Name=next.Name,UnitPrice=next.UnitPrice,PreparationStation=next.PreparationStation};
            items[product]=item;detailsVersions[(org,branch,product)]=next.Version;return Details(org,branch,item);
        }
    }
}
