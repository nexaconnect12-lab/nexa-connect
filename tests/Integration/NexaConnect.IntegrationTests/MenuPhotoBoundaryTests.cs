extern alias CATALOG;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaConnect.Contracts.Catalog;
using NexaConnect.Contracts.Platform;
using NexaConnect.Infrastructure.Authentication;
using CATALOG::NexaConnect.Services.Catalog.Application.MenuPhotos;
using CATALOG::NexaConnect.Services.Catalog.Application.Tenant;
using CATALOG::NexaConnect.Services.Catalog.Infrastructure;

namespace NexaConnect.IntegrationTests;
public sealed class MenuPhotoBoundaryTests
{
    [Fact]public async Task Customer_assignment_requires_tenant_live_permissions_approval_and_version()
    {
        var f=new Fixture();await using var app=await Host(f);using var client=app.GetTestClient();
        string path=$"/api/catalog/v1/customer/branches/{f.Branch}/products/{f.Product}";
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync(path+"/photo-selection")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-Client","cashier");
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync(path+"/photo-selection")).StatusCode);
        client.DefaultRequestHeaders.Add(TenantContextHeaders.OrganizationId,f.Org.ToString());client.DefaultRequestHeaders.Add(TenantContextHeaders.ApplicationCode,"nexa_connect");
        f.Write=false;Assert.Equal(HttpStatusCode.Forbidden,(await client.PutAsJsonAsync(path+"/photo-selection",new SetMenuPhoto(f.Asset,0,true))).StatusCode);
        f.Write=true;f.Media=false;Assert.Equal(HttpStatusCode.Forbidden,(await client.PutAsJsonAsync(path+"/photo-selection",new SetMenuPhoto(f.Asset,0,true))).StatusCode);
        f.Media=true;Assert.Equal(HttpStatusCode.BadRequest,(await client.PutAsJsonAsync(path+"/photo-selection",new SetMenuPhoto(f.Asset,0,false))).StatusCode);
        using var saved=await client.PutAsJsonAsync(path+"/photo-selection",new SetMenuPhoto(f.Asset,0,true));Assert.Equal(HttpStatusCode.OK,saved.StatusCode);Assert.True(saved.Headers.CacheControl?.NoStore);
        Assert.Equal(HttpStatusCode.Conflict,(await client.PutAsJsonAsync(path+"/photo-selection",new SetMenuPhoto(null,0,true))).StatusCode);
        f.Media=false;using var photo=await client.GetAsync(path+"/photo");Assert.Equal(HttpStatusCode.OK,photo.StatusCode);Assert.Equal("image/png",photo.Content.Headers.ContentType?.MediaType);Assert.True(photo.Headers.CacheControl?.NoStore);
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(path+$"/photo-preview/{f.Asset}")).StatusCode);
        f.Read=false;Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(path+"/photo")).StatusCode);
        f.Read=true;client.DefaultRequestHeaders.Remove(TenantContextHeaders.OrganizationId);client.DefaultRequestHeaders.Add(TenantContextHeaders.OrganizationId,Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(path+"/photo")).StatusCode);
    }
    [Theory][InlineData("nexaconnect-order-service",200)][InlineData("nexaconnect-catalog-service",403)][InlineData("cashier",403)]
    public async Task Internal_photo_read_is_exact_Order_workload_only(string clientId,int code)
    {
        var f=new Fixture();await using var app=await Host(f);using var client=app.GetTestClient();
        await f.Repo.ChangeAsync(f.Org,f.Branch,f.Product,_=>new(f.Asset,1,true),new("manager",Guid.NewGuid()),default);
        client.DefaultRequestHeaders.Add("X-Test-Client",clientId);
        using var result=await client.GetAsync($"/api/catalog/v1/internal/organizations/{f.Org}/branches/{f.Branch}/products/{f.Product}/photo");Assert.Equal(code,(int)result.StatusCode);
        if(code==200)Assert.True(result.Headers.CacheControl?.NoStore);
    }
    private static async Task<WebApplication> Host(Fixture f)
    {
        var b=WebApplication.CreateBuilder();b.WebHost.UseTestServer();b.Services.AddLogging(l=>l.ClearProviders());
        b.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions,Identity>("Test",_=>{});
        b.Services.AddAuthorization(o=>o.AddPolicy(NexaAuthorizationPolicies.ServiceWorkload,p=>p.RequireAuthenticatedUser().RequireClaim("azp","nexaconnect-order-service","nexaconnect-catalog-service")));
        b.Services.AddControllers().AddApplicationPart(typeof(CATALOG::NexaConnect.Services.Catalog.Controllers.MenuPhotosController).Assembly);
        b.Services.AddSingleton<IMenuPhotoRepository>(f.Repo);b.Services.AddSingleton<IMenuPhotoAssetReader>(f);b.Services.AddSingleton<ICatalogTenantAuthorizer>(f);b.Services.AddScoped<MenuPhotos>();
        var app=b.Build();app.UseAuthentication();app.UseAuthorization();app.MapControllers();await app.StartAsync();return app;
    }
    private sealed class Fixture:ICatalogTenantAuthorizer,IMenuPhotoAssetReader
    {
        public Guid Org=Guid.NewGuid(),Branch=Guid.NewGuid(),Product=Guid.NewGuid(),Asset=Guid.NewGuid();public bool Read=true,Write=true,Media=true;public InMemoryMenuPhotoRepository Repo;
        public Fixture(){var menu=new InMemoryMenuCatalog();menu.AddForOrganizationBranch(Org,Branch,new(Product,"Meal",10,"THB","kitchen"));Repo=new(menu);}
        public Task<bool> HasAccessAsync(Guid org,string p,string token,CancellationToken ct)=>Task.FromResult(org==Org&&p==ProductPermissions.MediaAssetRead&&Media);
        public Task<bool> HasBranchAccessAsync(Guid org,Guid branch,string p,string token,CancellationToken ct)=>Task.FromResult(org==Org&&branch==Branch&&(p==ProductPermissions.CatalogMenuWrite?Write:Read));
        public Task<byte[]?> ReadAsync(Guid org,Guid asset,CancellationToken ct)=>Task.FromResult<byte[]?>(org==Org&&asset==Asset?[137,80,78,71]:null);
    }
    private sealed class Identity(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string id=Request.Headers["X-Test-Client"].ToString();return Task.FromResult(id==""?AuthenticateResult.NoResult():AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub","fixture"),new Claim("azp",id)],Scheme.Name)),Scheme.Name)));
        }
    }
}
