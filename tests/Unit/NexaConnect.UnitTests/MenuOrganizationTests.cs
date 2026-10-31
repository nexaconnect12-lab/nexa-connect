using NexaConnect.Contracts.Catalog;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Catalog.Application.Menu;
using NexaConnect.Services.Catalog.Application.Organization;
using NexaConnect.Services.Catalog.Application.Tenant;
using NexaConnect.Services.Catalog.Domain.Organization;
using NexaConnect.Services.Catalog.Infrastructure;
using NexaConnect.POS;
namespace NexaConnect.UnitTests;
public sealed class MenuOrganizationTests
{
    [Theory][InlineData("")][InlineData(" ")][InlineData("Food\n")][InlineData("Food\t")]
    public void Category_names_are_safe_and_required(string name)=>Assert.Throws<ArgumentException>(()=>BranchMenuOrganizationState.Empty().Change(0,[new(Guid.NewGuid(),name)],[],new HashSet<Guid>()));
    [Theory][InlineData("duplicate-id")][InlineData("duplicate-name")][InlineData("category-limit")][InlineData("item-limit")][InlineData("duplicate-product")][InlineData("unknown-category")][InlineData("foreign-product")][InlineData("empty-id")]
    public void Invalid_or_foreign_layouts_are_rejected(string condition)
    {
        var product=Guid.NewGuid();var category=new Category(Guid.NewGuid(),"Food");Category[] categories=[category];Placement[] items=[new(product,category.Id)];
        switch(condition){case "duplicate-id":categories=[category,category with{Name="Drinks"}];break;case "duplicate-name":categories=[category,new(Guid.NewGuid()," food ")];break;
            case "category-limit":categories=Enumerable.Range(0,51).Select(i=>new Category(Guid.NewGuid(),"Category "+i)).ToArray();break;
            case "item-limit":items=Enumerable.Range(0,201).Select(_=>new Placement(Guid.NewGuid(),null)).ToArray();break;
            case "duplicate-product":items=[items[0],items[0]];break;case "unknown-category":items=[new(product,Guid.NewGuid())];break;
            case "foreign-product":items=[new(Guid.NewGuid(),category.Id)];break;default:categories=[new(Guid.Empty,"Food")];break;}
        Assert.Throws<ArgumentException>(()=>BranchMenuOrganizationState.Empty().Change(0,categories,items,new HashSet<Guid>{product}));
    }
    [Fact]public void Version_noop_order_and_explicit_uncategorized_preserve_immutable_state()
    {
        Guid one=Guid.NewGuid(),two=Guid.NewGuid();var cat=new Category(Guid.NewGuid()," Food ");var known=new HashSet<Guid>{one,two};
        var first=BranchMenuOrganizationState.Empty().Change(0,[cat],[new(two,cat.Id),new(one,null)],known);
        Assert.Equal(1,first.Version);Assert.Equal("Food",first.Categories[0].Name);
        Assert.Same(first,first.Change(1,first.Categories,first.Items,known));
        Assert.Throws<MenuOrganizationConflictException>(()=>first.Change(0,first.Categories,first.Items,known));
        Assert.Throws<ArgumentException>(()=>first.Change(1,[],first.Items,known));
        var clear=first.Change(1,[],[new(two,null),new(one,null)],known);Assert.Equal(2,clear.Version);Assert.NotNull(first.Items[0].CategoryId);
        Assert.Throws<NotSupportedException>(()=>((IList<Category>)first.Categories).Add(cat));
    }
    [Fact]public async Task Branch_permission_and_membership_are_authoritative_and_prices_are_untouched()
    {
        var f=new Fixture();var original=f.Menu.GetForOrganizationBranch(f.Org,f.Branch).Single();var cat=new MenuCategory(Guid.NewGuid(),"Food");
        Assert.Equal(0,(await f.App.ReadAsync(f.Org,f.Branch,"Bearer",default)).Version);
        var saved=await f.App.SaveAsync(f.Org,f.Branch,new(0,[cat],[new(f.Product,cat.Id)]),"Bearer",f.Actor,default);Assert.Equal(1,saved.Version);
        Assert.Equal(original,f.Menu.GetForOrganizationBranch(f.Org,f.Branch).Single());
        Assert.Empty((await f.Repo.ReadAsync(Guid.NewGuid(),f.Branch,default)).Categories);
        Assert.Empty((await f.Repo.ReadAsync(f.Org,Guid.NewGuid(),default)).Categories);
        await Assert.ThrowsAsync<ArgumentException>(()=>f.App.SaveAsync(f.Org,f.Branch,new(1,[cat],[new(Guid.NewGuid(),cat.Id)]),"Bearer",f.Actor,default));
        f.Access.Granted=false;await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.App.ReadAsync(f.Org,f.Branch,"Bearer",default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.App.SaveAsync(f.Org,f.Branch,new(1,[],[]),"Bearer",f.Actor,default));
    }
    [Fact]public async Task Read_rechecks_live_permission_after_repository_access()
    {
        var f=new Fixture();var app=new MenuOrganizationService(new Revoking(f.Repo,()=>f.Access.Granted=false),f.Access,f.Menu);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>app.ReadAsync(f.Org,f.Branch,"Bearer",default));
    }
    [Fact]public void Native_ordering_preserves_original_financial_items_and_rejects_foreign_scope()
    {
        var org=Guid.NewGuid();var branch=Guid.NewGuid();var a=new PosMenuItem(Guid.NewGuid(),"Tea",5,"THB","bar",true);var b=new PosMenuItem(Guid.NewGuid(),"Meal",107,"THB","kitchen",true);var c=new PosMenuItem(Guid.NewGuid(),"Other",1,"THB","kitchen",false);
        var food=new MenuCategory(Guid.NewGuid(),"Food");var drink=new MenuCategory(Guid.NewGuid(),"Drink");var layout=new BranchMenuOrganization(org,branch,1,[food,drink],[new(a.ProductId,drink.Id),new(b.ProductId,food.Id)]);
        PosMenuOrganizationView.Validate(layout,org,branch);var sorted=PosMenuOrganizationView.Order([a,b,c],layout);
        Assert.Same(b,sorted[0]);Assert.Same(a,sorted[1]);Assert.Same(c,sorted[2]);Assert.Equal(107,sorted[0].UnitPrice);Assert.Null(PosMenuOrganizationView.CategoryFor(c.ProductId,layout));
        Assert.Throws<InvalidDataException>(()=>PosMenuOrganizationView.Validate(layout,Guid.NewGuid(),branch));
        Assert.Throws<InvalidDataException>(()=>PosMenuOrganizationView.Validate(layout with{Items=[new(a.ProductId,Guid.NewGuid())]},org,branch));
    }
    private sealed class Fixture
    {
        public Guid Org=Guid.NewGuid(),Branch=Guid.NewGuid(),Product=Guid.NewGuid();public InMemoryMenuCatalog Menu=new();public Access Access=new();public InMemoryMenuOrganizationRepository Repo;public MenuOrganizationService App;public MenuMutationContext Actor=new("manager",Guid.NewGuid());
        public Fixture(){Menu.AddForOrganizationBranch(Org,Branch,new(Product,"Meal",107,"THB","kitchen"));Repo=new(Menu);App=new(Repo,Access,Menu);}
    }
    private sealed class Access:ICatalogTenantAuthorizer
    {public bool Granted=true;public Task<bool> HasAccessAsync(Guid o,string p,string token,CancellationToken ct)=>Task.FromResult(Granted);public Task<bool> HasBranchAccessAsync(Guid o,Guid b,string p,string token,CancellationToken ct)=>Task.FromResult(Granted);}
    private sealed class Revoking(IMenuOrganizationRepository inner,Action revoke):IMenuOrganizationRepository
    {public async Task<BranchMenuOrganizationState> ReadAsync(Guid o,Guid b,CancellationToken ct){var s=await inner.ReadAsync(o,b,ct);revoke();return s;}public Task<BranchMenuOrganizationState> ChangeAsync(Guid o,Guid b,Func<BranchMenuOrganizationState,IReadOnlySet<Guid>,BranchMenuOrganizationState> decide,MenuMutationContext a,CancellationToken ct)=>inner.ChangeAsync(o,b,decide,a,ct);}
}
