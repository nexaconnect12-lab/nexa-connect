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
using CATALOG::NexaConnect.Services.Catalog.Application.Organization;
using CATALOG::NexaConnect.Services.Catalog.Application.Menu;
using CATALOG::NexaConnect.Services.Catalog.Application.Tenant;
using CATALOG::NexaConnect.Services.Catalog.Infrastructure;
namespace NexaConnect.IntegrationTests;
public sealed class MenuOrganizationBoundaryTests
{
    [Fact]public async Task Customer_routes_require_live_scope_validate_versions_and_preserve_prices()
    {
        var f=new Access();await using var app=await Host(f);using var c=app.GetTestClient();string url=$"/api/catalog/v1/customer/branches/{f.Branch}/menu-organization";
        Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync(url)).StatusCode);c.DefaultRequestHeaders.Add("X-Test-Client","customer");
        Assert.Equal(HttpStatusCode.BadRequest,(await c.GetAsync(url)).StatusCode);c.DefaultRequestHeaders.Add(TenantContextHeaders.OrganizationId,f.Org.ToString());c.DefaultRequestHeaders.Add(TenantContextHeaders.ApplicationCode,"nexa_connect");
        var category=new MenuCategory(Guid.NewGuid(),"Food");var update=new UpdateMenuOrganization(0,[category],[new(f.Product,category.Id)]);
        f.Granted=false;Assert.Equal(HttpStatusCode.Forbidden,(await c.PutAsJsonAsync(url,update)).StatusCode);f.Granted=true;
        using var saved=await c.PutAsJsonAsync(url,update);Assert.Equal(HttpStatusCode.OK,saved.StatusCode);Assert.True(saved.Headers.CacheControl?.NoStore);Assert.Equal(1,(await saved.Content.ReadFromJsonAsync<BranchMenuOrganization>())!.Version);
        Assert.Equal(HttpStatusCode.Conflict,(await c.PutAsJsonAsync(url,update with{Categories=[] ,Items=[]})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PutAsJsonAsync(url,update with{ExpectedVersion=1,Items=[new(Guid.NewGuid(),category.Id)]})).StatusCode);
        c.DefaultRequestHeaders.Remove(TenantContextHeaders.OrganizationId);c.DefaultRequestHeaders.Add(TenantContextHeaders.OrganizationId,Guid.NewGuid().ToString());Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync(url)).StatusCode);
    }
    [Theory][InlineData("nexaconnect-order-service",200)][InlineData("nexaconnect-catalog-service",403)][InlineData("customer",403)]
    public async Task Workload_read_has_no_customer_permission_bypass_and_requires_exact_Order(string id,int expected)
    {
        var f=new Access();await using var app=await Host(f);using var c=app.GetTestClient();c.DefaultRequestHeaders.Add("X-Test-Client",id);
        using var r=await c.GetAsync($"/api/catalog/v1/internal/organizations/{f.Org}/branches/{f.Branch}/menu-organization");Assert.Equal(expected,(int)r.StatusCode);
    }
    private static async Task<WebApplication> Host(Access f)
    {
        var b=WebApplication.CreateBuilder();b.WebHost.UseTestServer();b.Services.AddLogging(l=>l.ClearProviders());b.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions,Identity>("Test",_=>{});
        b.Services.AddAuthorization(o=>o.AddPolicy(NexaAuthorizationPolicies.ServiceWorkload,p=>p.RequireAuthenticatedUser().RequireClaim("azp","nexaconnect-order-service","nexaconnect-catalog-service")));
        b.Services.AddControllers().AddApplicationPart(typeof(CATALOG::NexaConnect.Services.Catalog.Controllers.MenuOrganizationController).Assembly);
        var menu=new InMemoryMenuCatalog();menu.AddForOrganizationBranch(f.Org,f.Branch,new(f.Product,"Meal",107,"THB","kitchen"));b.Services.AddSingleton<IMenuCatalog>(menu);b.Services.AddSingleton<IMenuOrganizationRepository>(new InMemoryMenuOrganizationRepository(menu));b.Services.AddSingleton<ICatalogTenantAuthorizer>(f);b.Services.AddScoped<MenuOrganizationService>();
        var app=b.Build();app.UseAuthentication();app.UseAuthorization();app.MapControllers();await app.StartAsync();return app;
    }
    private sealed class Access:ICatalogTenantAuthorizer
    {public Guid Org=Guid.NewGuid(),Branch=Guid.NewGuid(),Product=Guid.NewGuid();public bool Granted=true;public Task<bool> HasAccessAsync(Guid o,string p,string bearer,CancellationToken ct)=>Task.FromResult(false);public Task<bool> HasBranchAccessAsync(Guid o,Guid b,string p,string bearer,CancellationToken ct)=>Task.FromResult(Granted&&o==Org&&b==Branch);}
    private sealed class Identity(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {protected override Task<AuthenticateResult> HandleAuthenticateAsync(){string id=Request.Headers["X-Test-Client"].ToString();return Task.FromResult(id==""?AuthenticateResult.NoResult():AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub","manager"),new Claim("azp",id)],Scheme.Name)),Scheme.Name)));}}
}
