using NexaConnect.Contracts.Catalog;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Catalog.Application.Availability;
using NexaConnect.Services.Catalog.Application.Details;
using NexaConnect.Services.Catalog.Application.Menu;
using NexaConnect.Services.Catalog.Application.Tenant;
using NexaConnect.Services.Catalog.Domain.Details;
using NexaConnect.Services.Catalog.Infrastructure;
namespace NexaConnect.UnitTests;

public sealed class MenuDetailsTests
{
    [Fact] public void Details_normalize_text_preserve_currency_and_use_exact_CAS_noops()
    {
        var state=new MenuDetailsState("Meal",107,"THB","kitchen",0);
        Assert.Same(state,state.Change(" Meal ",107," kitchen ",0));
        var next=state.Change(" New meal ",108.25m," bar ",0);
        Assert.Equal("New meal",next.Name);Assert.Equal(108.25m,next.UnitPrice);Assert.Equal("bar",next.PreparationStation);Assert.Equal("THB",next.Currency);Assert.Equal(1,next.Version);
        Assert.Equal(107,state.UnitPrice);Assert.Throws<MenuDetailsConflictException>(()=>next.Change("Meal",107,"kitchen",0));
        Assert.Equal(0,next.Change("Free side",0,"bar",1).UnitPrice);
    }
    [Fact] public void Invalid_text_prices_missing_fields_currency_and_version_fail_closed()
    {
        var state=new MenuDetailsState("Meal",107,"THB","kitchen",0);
        foreach(var text in new[]{""," ","Bad\nname",new string('x',121)})Assert.Throws<ArgumentException>(()=>state.Change(text,1,"kitchen",0));
        foreach(var text in new[]{"","\t",new string('x',81)})Assert.Throws<ArgumentException>(()=>state.Change("Meal",1,text,0));
        foreach(var price in new[]{-1m,1.001m,100000000m})Assert.Throws<ArgumentException>(()=>state.Change("Meal",price,"bar",0));
        Assert.Throws<ArgumentException>(()=>state.Change("Meal",null,"bar",0));Assert.Throws<ArgumentException>(()=>state.Change("Meal",1,"bar",null));
        Assert.Throws<ArgumentException>(()=>new MenuDetailsState("Meal",1,"USD","bar",0).Change("Meal",2,"bar",0));
        Assert.Throws<MenuDetailsConflictException>(()=>new MenuDetailsState("Meal",1,"THB","bar",long.MaxValue).Change("Meal",2,"bar",long.MaxValue));
    }
    [Fact] public async Task Scoped_editor_preserves_soldout_and_legacy_writes_invalidate_old_reviews()
    {
        var f=new Fixture();await f.Availability.ChangeAsync(f.Org,f.Branch,f.Product,new(false,0),f.Actor,default);
        var saved=await f.App.ChangeAsync(f.Org,f.Branch,f.Product,new("Meal edited",108,"bar",0),"Bearer",f.Actor,default);
        Assert.False(saved!.Available);Assert.Equal(1,saved.Version);
        Assert.Equal(1,(await f.Availability.ReadAsync(f.Org,f.Branch,default)).Single().Version);
        f.Menu.AddForOrganizationBranch(f.Org,f.Branch,new(f.Product,"Legacy edit",109,"THB","kitchen"));
        Assert.Equal(2,(await f.App.ReadAsync(f.Org,f.Branch,"Bearer",default)).Items.Single().Version);
        await Assert.ThrowsAsync<MenuDetailsConflictException>(()=>f.App.ChangeAsync(f.Org,f.Branch,f.Product,new("Stale edit",1,"bar",1),"Bearer",f.Actor,default));
        Assert.False(f.Menu.GetForOrganizationBranch(f.Org,f.Branch).Single().Available);
        Assert.Empty(await f.Repo.ReadAsync(Guid.NewGuid(),f.Branch,default));Assert.Null(await f.Repo.ChangeAsync(f.Org,f.Branch,Guid.NewGuid(),new("Unknown",1,"bar",0),f.Actor,default));
    }
    [Fact] public async Task Denied_reader_or_writer_cannot_access_or_change_scoped_details()
    {
        var f=new Fixture();f.Access.Granted=false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.App.ReadAsync(f.Org,f.Branch,"Bearer",default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.App.ChangeAsync(f.Org,f.Branch,f.Product,new("Changed",1,"bar",0),"Bearer",f.Actor,default));
        Assert.Equal(107,f.Menu.GetForOrganizationBranch(f.Org,f.Branch).Single().UnitPrice);
    }
    [Fact] public async Task Read_permission_is_rechecked_after_repository_access()
    {
        var f=new Fixture();var app=new MenuDetailsService(new RevokingRepository(f.Repo,()=>f.Access.Granted=false),f.Access);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>app.ReadAsync(f.Org,f.Branch,"Bearer",default));
    }
    private sealed class Fixture
    {
        public Guid Org=Guid.NewGuid(),Branch=Guid.NewGuid(),Product=Guid.NewGuid();public InMemoryMenuCatalog Menu=new();public Access Access=new();
        public InMemoryMenuDetailsRepository Repo;public InMemoryMenuAvailabilityRepository Availability;public MenuDetailsService App;public MenuMutationContext Actor=new("manager",Guid.NewGuid());
        public Fixture(){Access.Org=Org;Access.Branch=Branch;Menu.AddForOrganizationBranch(Org,Branch,new(Product,"Meal",107,"THB","kitchen"));Repo=new(Menu);Availability=new(Menu);App=new(Repo,Access);}
    }
    private sealed class Access:ICatalogTenantAuthorizer
    {
        public Guid Org,Branch;public bool Granted=true;
        public Task<bool> HasAccessAsync(Guid org,string permission,string bearer,CancellationToken ct)=>Task.FromResult(false);
        public Task<bool> HasBranchAccessAsync(Guid org,Guid branch,string permission,string bearer,CancellationToken ct)
        {Assert.Contains(permission,new[]{ProductPermissions.CatalogMenuRead,ProductPermissions.CatalogMenuWrite});return Task.FromResult(Granted&&org==Org&&branch==Branch);}
    }
    private sealed class RevokingRepository(IMenuDetailsRepository inner,Action revoke):IMenuDetailsRepository
    {
        public async Task<IReadOnlyList<EditableMenuItem>> ReadAsync(Guid org,Guid branch,CancellationToken ct){var result=await inner.ReadAsync(org,branch,ct);revoke();return result;}
        public Task<EditableMenuItem?> ChangeAsync(Guid org,Guid branch,Guid product,UpdateMenuDetails input,MenuMutationContext actor,CancellationToken ct)=>throw new InvalidOperationException();
    }
}
