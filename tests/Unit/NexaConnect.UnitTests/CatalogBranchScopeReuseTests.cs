using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using NexaConnect.Services.Catalog.Application.Menu;
using NexaConnect.Services.Catalog.Application.MenuPhotos;
using NexaConnect.Services.Catalog.Application.Tenant;
using NexaConnect.Services.Catalog.Infrastructure;

namespace NexaConnect.UnitTests;
public sealed class CatalogBranchScopeReuseTests
{
    [Fact]public async Task Photo_read_preview_save_and_delivery_can_revalidate_scope_on_the_same_client()
    {
        using var fixture=new Fixture();var menu=new InMemoryMenuCatalog();var product=Guid.NewGuid();
        menu.AddForOrganizationBranch(fixture.Org,fixture.Branch,new(product,"Meal",107,"THB","kitchen"));
        var app=new MenuPhotos(new InMemoryMenuPhotoRepository(menu),new Images(),new Access(fixture.Reader,fixture.Org));
        Assert.Null((await app.ReadAsync(fixture.Org,fixture.Branch,product,"Bearer staff",default)).AssetId);
        var asset=Guid.NewGuid();Assert.NotNull(await app.PreviewAsync(fixture.Org,fixture.Branch,product,asset,"Bearer staff",default));
        Assert.Equal(asset,(await app.SetAsync(fixture.Org,fixture.Branch,product,new(asset,0,true),"Bearer staff",new("manager",Guid.NewGuid()),default)).AssetId);
        Assert.NotNull(await app.DeliverAsync(fixture.Org,fixture.Branch,product,"Bearer staff",default));
        Assert.Equal(8,fixture.Handler.Reads);
        var organization=new NexaConnect.Services.Catalog.Application.Organization.MenuOrganizationService(new InMemoryMenuOrganizationRepository(menu),new Access(fixture.Reader,fixture.Org),menu);
        Assert.Equal(0,(await organization.ReadAsync(fixture.Org,fixture.Branch,"Bearer staff",default)).Version);
        var category=new NexaConnect.Contracts.Catalog.MenuCategory(Guid.NewGuid(),"Food");
        await organization.SaveAsync(fixture.Org,fixture.Branch,new(0,[category],[new(product,category.Id)]),"Bearer staff",new("manager",Guid.NewGuid()),default);
        Assert.Equal(12,fixture.Handler.Reads);
        Assert.Equal(107,menu.GetForOrganizationBranch(fixture.Org,fixture.Branch).Single().UnitPrice);
    }
    [Fact]public async Task Repeated_and_parallel_scope_reads_still_fetch_live_scope_and_reject_missing_branches()
    {
        using var f=new Fixture();Assert.NotNull(await f.Reader.GetAsync(f.Branch,default));
        var results=await Task.WhenAll(Enumerable.Range(0,4).Select(_=>f.Reader.GetAsync(f.Branch,default)));
        Assert.All(results,s=>Assert.Equal(f.Org,s!.OrganizationId));
        f.Handler.Missing=true;Assert.Null(await f.Reader.GetAsync(f.Branch,default));Assert.Equal(6,f.Handler.Reads);
    }
    private sealed class Access(IRestaurantBranchScopeReader reader,Guid org):ICatalogTenantAuthorizer
    {
        public Task<bool> HasAccessAsync(Guid organization,string permission,string bearer,CancellationToken ct)=>Task.FromResult(organization==org);
        public async Task<bool> HasBranchAccessAsync(Guid organization,Guid branch,string permission,string bearer,CancellationToken ct)
        {var scope=await reader.GetAsync(branch,ct);return scope?.OrganizationId==organization&&scope.BranchId==branch;}
    }
    private sealed class Images:IMenuPhotoAssetReader {public Task<byte[]?> ReadAsync(Guid org,Guid asset,CancellationToken ct)=>Task.FromResult<byte[]?>([1,2,3]);}
    private sealed class Fixture:IDisposable
    {
        public Guid Org=Guid.NewGuid(),Branch=Guid.NewGuid();public ScopeHandler Handler;public RestaurantBranchScopeClient Reader;
        private readonly MemoryCache cache=new(new MemoryCacheOptions());private readonly HttpClient tokenClient,scopeClient;
        public Fixture(){Handler=new(Org,Branch);scopeClient=new(Handler);tokenClient=new(new Tokens());
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Services:Restaurant"]="https://restaurant.test/",["WorkloadIdentity:Authority"]="https://identity.test/",["WorkloadIdentity:ClientId"]="nexaconnect-catalog-service",["WorkloadIdentity:ClientSecret"]="synthetic"}).Build();
            Reader=new(scopeClient,new CatalogWorkloadTokenProvider(tokenClient,config,cache),config);}
        public void Dispose(){scopeClient.Dispose();tokenClient.Dispose();cache.Dispose();}
    }
    private sealed class Tokens:HttpMessageHandler {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new{access_token="synthetic-workload",expires_in=600})});}
    private sealed class ScopeHandler(Guid org,Guid branch):HttpMessageHandler
    {
        public int Reads;public bool Missing;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Interlocked.Increment(ref Reads);
            Assert.Equal($"https://restaurant.test/api/restaurant/v1/branches/{branch:D}/authorization-scope",request.RequestUri!.AbsoluteUri);Assert.Equal("Bearer synthetic-workload",request.Headers.Authorization?.ToString());
            return Task.FromResult(Missing?new HttpResponseMessage(HttpStatusCode.NotFound):new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new{organizationId=org,restaurantId=Guid.NewGuid(),branchId=branch})});}
    }
}
