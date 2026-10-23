extern alias ORDER;
extern alias CATALOG;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaConnect.Contracts.RestaurantQr;
using ORDER::NexaConnect.Services.Order.Application.Orders;
using ORDER::NexaConnect.Services.Order.Application.Workflow;
using ORDER::NexaConnect.Services.Order.Domain;

namespace NexaConnect.IntegrationTests;

public sealed class QrMenuBoundaryTests
{
    [Fact] public async Task Order_guest_http_validates_capability_and_ignores_query_scope_without_creating_orders()
    {
        await using var factory=new OrderFactory();using var client=factory.CreateClient(Options());
        using(var missing=await client.GetAsync("/api/order/v1/guest/qr/menu?sessionToken="+new string('S',43)))Assert.Equal(HttpStatusCode.Unauthorized,missing.StatusCode);
        client.DefaultRequestHeaders.Add("X-Nexa-Guest-Session",new string('S',43));
        using var menu=await client.GetAsync("/api/order/v1/guest/qr/menu?branchId="+Guid.NewGuid());Assert.Equal(HttpStatusCode.OK,menu.StatusCode);Assert.True(menu.Headers.CacheControl?.NoStore);
        string json=await menu.Content.ReadAsStringAsync();Assert.DoesNotContain("preparationStation",json);Assert.DoesNotContain("organizationId",json);
        Assert.Equal(factory.Port.Context.BranchId,factory.Port.Branch);
        using var quote=await client.PostAsJsonAsync("/api/order/v1/guest/qr/quote",new QrCart([new(factory.Port.Product,2)]));Assert.Equal(HttpStatusCode.OK,quote.StatusCode);
        var result=await quote.Content.ReadFromJsonAsync<QrQuote>();Assert.Equal(235.4m,result!.TotalAmount);
        using var tampered=await client.PostAsync("/api/order/v1/guest/qr/quote",new StringContent("{\"lines\":[],\"currency\":\"SGD\"}",System.Text.Encoding.UTF8,"application/json"));Assert.Equal(HttpStatusCode.BadRequest,tampered.StatusCode);
        factory.Port.Available=false;using var unavailable=await client.PostAsJsonAsync("/api/order/v1/guest/qr/quote",new QrCart([new(factory.Port.Product,2)]));Assert.Equal(HttpStatusCode.Conflict,unavailable.StatusCode);
        factory.Port.Revoked=true;using var revoked=await client.GetAsync("/api/order/v1/guest/qr/menu");Assert.Equal(HttpStatusCode.Unauthorized,revoked.StatusCode);
        using var staff=await client.PostAsJsonAsync("/api/order/v1/workflows/quote",new{});Assert.Equal(HttpStatusCode.Unauthorized,staff.StatusCode);
    }
    [Fact] public async Task Disabled_or_failed_order_dependencies_fail_closed()
    {
        await using(var disabled=new OrderFactory(false)){using var client=disabled.CreateClient(Options());using var response=await client.GetAsync("/api/order/v1/guest/qr/menu");Assert.Equal(HttpStatusCode.NotFound,response.StatusCode);}
        await using var factory=new OrderFactory();using var browser=factory.CreateClient(Options());browser.DefaultRequestHeaders.Add("X-Nexa-Guest-Session",new string('S',43));factory.Port.Failure=true;
        using var failed=await browser.GetAsync("/api/order/v1/guest/qr/menu");Assert.Equal(HttpStatusCode.ServiceUnavailable,failed.StatusCode);Assert.DoesNotContain("private-secret",await failed.Content.ReadAsStringAsync());
    }
    [Fact] public async Task Catalog_internal_menu_accepts_only_exact_order_workload_and_scopes_organization()
    {
        await using var factory=new CatalogFactory();using var client=factory.CreateClient(Options());var catalog=factory.Services.GetRequiredService<CATALOG::NexaConnect.Services.Catalog.Application.Menu.IMenuCatalog>();
        var org=Guid.NewGuid();var branch=Guid.NewGuid();catalog.AddForOrganizationBranch(org,branch,new(Guid.NewGuid(),"Soup",107,"THB","kitchen"));
        string path=$"/api/catalog/v1/internal/organizations/{org}/branches/{branch}/guest-menu";
        using(var anonymous=await client.GetAsync(path))Assert.Equal(HttpStatusCode.Unauthorized,anonymous.StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-Workload","nexaconnect-media-service");using(var wrong=await client.GetAsync(path))Assert.Equal(HttpStatusCode.Forbidden,wrong.StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-Workload");client.DefaultRequestHeaders.Add("X-Test-Workload","nexaconnect-order-service");
        using var allowed=await client.GetAsync(path);Assert.Equal(HttpStatusCode.OK,allowed.StatusCode);Assert.Single((await allowed.Content.ReadFromJsonAsync<QrCatalogMenu>())!.Items);
        var foreign=await client.GetFromJsonAsync<QrCatalogMenu>(path.Replace(org.ToString(),Guid.NewGuid().ToString()));Assert.Empty(foreign!.Items);
    }
    private static WebApplicationFactoryClientOptions Options()=>new(){BaseAddress=new Uri("https://localhost"),AllowAutoRedirect=false};
    private sealed class OrderFactory(bool enabled=true):WebApplicationFactory<ORDER::OrderProgram>
    {
        public Port Port=new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");builder.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{["Authentication:Authority"]="https://identity.test/realms/test",["QrMenu:Enabled"]=enabled.ToString()}));
            builder.ConfigureTestServices(s=>{s.RemoveAll<IQrMenuPort>();s.AddSingleton<IQrMenuPort>(Port);s.RemoveAll<IBranchPricingPort>();s.AddSingleton<IBranchPricingPort,Policies>();Auth(s);});
        }
    }
    private sealed class CatalogFactory:WebApplicationFactory<CATALOG::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");builder.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{["Authentication:Authority"]="https://identity.test/realms/test",["Persistence:Provider"]="InMemory"}));builder.ConfigureTestServices(Auth);
        }
    }
    private static void Auth(IServiceCollection s)=>s.AddAuthentication(o=>{o.DefaultAuthenticateScheme="qr-menu-test";o.DefaultChallengeScheme="qr-menu-test";o.DefaultForbidScheme="qr-menu-test";}).AddScheme<AuthenticationSchemeOptions,Handler>("qr-menu-test",_=>{});
    private sealed class Handler(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()=>Task.FromResult(Request.Headers.TryGetValue("X-Test-Workload",out var azp)?AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity([new Claim("azp",azp.ToString()),new Claim("client_id",azp.ToString())],Scheme.Name)),Scheme.Name)):AuthenticateResult.NoResult());
    }
    private sealed class Port:IQrMenuPort
    {
        public QrGuestContext Context=new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"R","B","A1","THB",DateTimeOffset.UtcNow.AddHours(1));public Guid Product=Guid.NewGuid(),Branch;public bool Available=true,Revoked,Failure;
        public Task<QrGuestContext?> ContextAsync(string capability,CancellationToken ct)=>Task.FromResult<QrGuestContext?>(Revoked?null:Context);
        public Task<IReadOnlyList<CatalogMenuItem>> MenuAsync(Guid org,Guid branch,CancellationToken ct){if(Failure)throw new HttpRequestException("private-secret");Assert.Equal(Context.OrganizationId,org);Branch=branch;return Task.FromResult<IReadOnlyList<CatalogMenuItem>>([new(Product,"Soup",107,"THB",Available,"kitchen")]);}
    }
    private sealed class Policies:IBranchPricingPort{public Task<PricingPolicy> GetAsync(Guid org,Guid restaurant,Guid branch,CancellationToken ct)=>Task.FromResult(new PricingPolicy(1,7,true,10));}
}
