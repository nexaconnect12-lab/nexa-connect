using NexaConnect.Contracts.Media;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Restaurant.Application.Configuration;
using NexaConnect.Services.Restaurant.Application.Branches;

namespace NexaConnect.UnitTests;

public sealed class BranchGalleryTests
{
    [Theory]
    [InlineData("version")][InlineData("count")][InlineData("text")][InlineData("duplicate")][InlineData("interval")]
    public void Invalid_selections_are_rejected(string change)
    {
        var slide=new GallerySlide(Guid.NewGuid(),"Item","");var update=new UpdateBranchGallery(1,[slide],[]);
        update=change switch{"version"=>update with{ExpectedVersion=0},"count"=>update with{Staff=Enumerable.Range(0,7).Select(_=>new GallerySlide(Guid.NewGuid())).ToArray()},"text"=>update with{Staff=[slide with{Caption="unsafe\ntext"}]},"duplicate"=>update with{Staff=[slide,slide]},_=>update with{SecondsPerSlide=1}};
        Assert.Throws<ArgumentException>(()=>BranchGalleryService.Validate(update));
    }
    [Fact]
    public async Task Live_branch_scope_and_ready_media_are_checked_before_any_save()
    {
        var fake=new Ports();var service=new BranchGalleryService(fake,fake,fake);var org=Guid.NewGuid();var branch=Guid.NewGuid();
        var update=new UpdateBranchGallery(4,[new(Guid.NewGuid(),"Approved","")],[]);
        fake.Granted=false;await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.SaveAsync(org,branch,update,"operator","Bearer synthetic",default));Assert.Equal(0,fake.Saves);
        fake.Granted=true;fake.Ready=false;await Assert.ThrowsAsync<ArgumentException>(()=>service.SaveAsync(org,branch,update,"operator","Bearer synthetic",default));Assert.Equal(0,fake.Saves);
        fake.Ready=true;await service.SaveAsync(org,branch,update,"operator","Bearer synthetic",default);
        Assert.Equal(1,fake.Saves);Assert.Equal(org,fake.Org);Assert.Equal(branch,fake.Branch);Assert.Equal(fake.Restaurant,fake.AuthorizedRestaurant);Assert.Equal(ProductPermissions.RestaurantConfigurationManage,fake.Permission);
        await service.GetAsync(org,branch,"Bearer synthetic",default);Assert.Equal(ProductPermissions.CatalogMenuRead,fake.Permission);
    }
    [Fact]
    public async Task Missing_or_inactive_hierarchy_is_denied_before_authorization_or_gallery_access()
    {
        var fake=new Ports { Restaurant=null };var service=new BranchGalleryService(fake,fake,fake);
        var org=Guid.NewGuid();var branch=Guid.NewGuid();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.GetAsync(org,branch,"Bearer synthetic",default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.SaveAsync(org,branch,new(1,[],[]),"operator","Bearer synthetic",default));
        Assert.Null(fake.Permission);Assert.Equal(0,fake.Reads);Assert.Equal(0,fake.Saves);
    }
    [Fact]
    public async Task Restaurant_scope_is_required_for_read_and_live_denials_remain_authoritative()
    {
        var fake=new Ports();var service=new BranchGalleryService(fake,fake,fake);
        var org=Guid.NewGuid();var branch=Guid.NewGuid();
        await service.GetAsync(org,branch,"Bearer synthetic",default);
        Assert.Equal(fake.Restaurant,fake.AuthorizedRestaurant);Assert.Equal(org,fake.LookupOrg);Assert.Equal(branch,fake.LookupBranch);
        fake.Granted=false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.GetAsync(org,branch,"Bearer synthetic",default));
        Assert.Equal(1,fake.Reads);
    }
    private sealed class Ports:IBranchGalleryRepository,IBranchCustomerAuthorizer,IGalleryAssetValidator
    {
        public bool Granted=true,Ready=true;public int Saves,Reads;public Guid Org,Branch,LookupOrg,LookupBranch;public Guid? Restaurant=Guid.NewGuid(),AuthorizedRestaurant;public string? Permission;
        public Task<Guid?> RestaurantAsync(Guid org,Guid branch,CancellationToken ct){LookupOrg=org;LookupBranch=branch;return Task.FromResult(Restaurant);}
        public Task<bool> IsGrantedAsync(Guid organizationId,Guid? restaurantId,Guid? branchId,string permission,string authorizationHeader,CancellationToken cancellationToken){Org=organizationId;Branch=branchId!.Value;AuthorizedRestaurant=restaurantId;Permission=permission;return Task.FromResult(Granted&&restaurantId==Restaurant&&restaurantId is not null);}
        public Task<bool> IsReadyAsync(Guid org,Guid asset,string bearer,CancellationToken ct)=>Task.FromResult(Ready);
        public Task<BranchGallery?> GetAsync(Guid org,Guid branch,CancellationToken ct){Reads++;return Task.FromResult<BranchGallery?>(new(org,branch,4,[],[]));}
        public Task<BranchGallery?> SaveAsync(Guid org,Guid branch,UpdateBranchGallery u,string actor,CancellationToken ct){Saves++;return Task.FromResult<BranchGallery?>(new(org,branch,5,u.Staff,u.Customer,u.SecondsPerSlide));}
    }
}
