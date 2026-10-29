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
using CATALOG::NexaConnect.Services.Catalog.Application.Barcodes;
using CATALOG::NexaConnect.Services.Catalog.Application.Tenant;
using CATALOG::NexaConnect.Services.Catalog.Infrastructure;

namespace NexaConnect.IntegrationTests;

public sealed class BarcodeBoundaryTests
{
    [Theory][InlineData(null,true,401)][InlineData("workload",true,403)][InlineData("customer-user",false,403)][InlineData("customer-user",true,200)]
    public async Task Barcode_reads_require_customer_live_branch_access(string? role,bool granted,int status)
    {
        var access=new Access{Granted=granted};await using var app=await Host(access);using var client=app.GetTestClient();
        if(role is not null)client.DefaultRequestHeaders.Add("X-Test-Role",role);
        client.DefaultRequestHeaders.Add(TenantContextHeaders.OrganizationId,access.Org.ToString());client.DefaultRequestHeaders.Add(TenantContextHeaders.ApplicationCode,"nexa_connect");
        using var result=await client.GetAsync($"/api/catalog/v1/customer/branches/{access.Branch}/products/{access.Product}/barcode");
        Assert.Equal(status,(int)result.StatusCode);if(status==200)Assert.True(result.Headers.CacheControl?.NoStore);
    }
    [Fact]public async Task Scope_version_validation_no_store_and_lookup_body_are_enforced()
    {
        var access=new Access();await using var app=await Host(access);using var client=app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Role","customer-user");
        string root=$"/api/catalog/v1/customer/branches/{access.Branch}";
        using var missing=await client.GetAsync(root+$"/products/{access.Product}/barcode");Assert.Equal(HttpStatusCode.BadRequest,missing.StatusCode);
        client.DefaultRequestHeaders.Add(TenantContextHeaders.OrganizationId,access.Org.ToString());client.DefaultRequestHeaders.Add(TenantContextHeaders.ApplicationCode,"nexa_connect");
        using var saved=await client.PutAsJsonAsync(root+$"/products/{access.Product}/barcode",new SetProductBarcode("001",0));Assert.Equal(HttpStatusCode.OK,saved.StatusCode);Assert.Equal(ProductPermissions.CatalogMenuWrite,access.Permission);
        using var conflict=await client.PutAsJsonAsync(root+$"/products/{access.Product}/barcode",new SetProductBarcode("002",0));Assert.Equal(HttpStatusCode.Conflict,conflict.StatusCode);
        using var invalid=await client.PostAsJsonAsync(root+"/barcode-lookup",new BarcodeLookupRequest("bad code"));Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        using var found=await client.PostAsJsonAsync(root+"/barcode-lookup",new BarcodeLookupRequest("001"));Assert.Equal(HttpStatusCode.OK,found.StatusCode);Assert.True(found.Headers.CacheControl?.NoStore);Assert.Equal(ProductPermissions.CatalogMenuRead,access.Permission);
        using var unknown=await client.PostAsJsonAsync(root+"/barcode-lookup",new BarcodeLookupRequest("002"));Assert.Equal(HttpStatusCode.NotFound,unknown.StatusCode);
        client.DefaultRequestHeaders.Remove(TenantContextHeaders.OrganizationId);client.DefaultRequestHeaders.Add(TenantContextHeaders.OrganizationId,Guid.NewGuid().ToString());
        using var foreign=await client.PostAsJsonAsync(root+"/barcode-lookup",new BarcodeLookupRequest("001"));Assert.Equal(HttpStatusCode.Forbidden,foreign.StatusCode);
    }
    private static async Task<WebApplication> Host(Access access)
    {
        var builder=WebApplication.CreateBuilder();builder.WebHost.UseTestServer();builder.Services.AddLogging(l=>l.ClearProviders());
        builder.Services.AddAuthentication("BarcodeTest").AddScheme<AuthenticationSchemeOptions,Identity>("BarcodeTest",_=>{});builder.Services.AddAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(CATALOG::NexaConnect.Services.Catalog.Controllers.ProductBarcodesController).Assembly);
        var menu=new InMemoryMenuCatalog();menu.AddForOrganizationBranch(access.Org,access.Branch,new(access.Product,"Meal",10,"THB","kitchen"));
        builder.Services.AddSingleton<IBarcodeRepository>(new InMemoryBarcodeRepository(menu));builder.Services.AddSingleton<ICatalogTenantAuthorizer>(access);builder.Services.AddScoped<BarcodeApplication>();
        var app=builder.Build();app.UseAuthentication();app.UseAuthorization();app.MapControllers();await app.StartAsync();return app;
    }
    private sealed class Access:ICatalogTenantAuthorizer
    {
        public Guid Org=Guid.NewGuid(),Branch=Guid.NewGuid(),Product=Guid.NewGuid();public bool Granted=true;public string? Permission;
        public Task<bool> HasAccessAsync(Guid org,string p,string token,CancellationToken ct)=>Task.FromResult(false);
        public Task<bool> HasBranchAccessAsync(Guid org,Guid branch,string p,string token,CancellationToken ct){Permission=p;return Task.FromResult(Granted&&org==Org&&branch==Branch);}
    }
    private sealed class Identity(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string role=Request.Headers["X-Test-Role"].ToString();if(role=="")return Task.FromResult(AuthenticateResult.NoResult());
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub","fixture"),new Claim(ClaimTypes.Role,role)],Scheme.Name)),Scheme.Name)));
        }
    }
}
