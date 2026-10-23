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
using ORDER::NexaConnect.Services.Order.Domain.Qr;
using ORDER::NexaConnect.Services.Order.Application.Tenant;

namespace NexaConnect.IntegrationTests;

public sealed class QrMenuBoundaryTests
{
    [Fact]public async Task Request_http_retains_reviewed_identity_private_status_and_scoped_staff_rejection()
    {
        await using var factory=new OrderFactory(true,true);using var client=factory.CreateClient(Options());client.DefaultRequestHeaders.Add("X-Nexa-Guest-Session",new string('S',43));
        var quote=await (await client.PostAsJsonAsync("/api/order/v1/guest/qr/quote",new QrCart([new(factory.Port.Product,1)]))).Content.ReadFromJsonAsync<QrQuote>();Assert.NotNull(quote!.QuoteId);
        using var submitted=await client.PostAsJsonAsync("/api/order/v1/guest/qr/requests",new QrSubmission(quote.QuoteId.Value,quote.Fingerprint));Assert.Equal(HttpStatusCode.OK,submitted.StatusCode);Assert.True(submitted.Headers.CacheControl?.NoStore);
        var mine=await client.GetFromJsonAsync<QrRequestList>("/api/order/v1/guest/qr/requests?visitorId="+Guid.NewGuid());Assert.Equal(quote.QuoteId,Assert.Single(mine!.Items).Id);
        factory.Port.Context=factory.Port.Context with{VisitorId=Guid.NewGuid()};Assert.Empty((await client.GetFromJsonAsync<QrRequestList>("/api/order/v1/guest/qr/requests"))!.Items);
        using(var guessed=await client.PostAsJsonAsync("/api/order/v1/guest/qr/requests",new QrSubmission(quote.QuoteId.Value,quote.Fingerprint)))Assert.Equal(HttpStatusCode.Conflict,guessed.StatusCode);
        var scope=factory.Port.Context;string queue=$"/api/order/v1/customer/organizations/{scope.OrganizationId}/branches/{scope.BranchId}/qr-requests";
        using(var guest=await client.GetAsync(queue))Assert.Equal(HttpStatusCode.Unauthorized,guest.StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-Staff","yes");client.DefaultRequestHeaders.Authorization=new("Bearer","staff");client.DefaultRequestHeaders.Add("X-Nexa-Organization-Id",scope.OrganizationId.ToString("D"));client.DefaultRequestHeaders.Add("X-Nexa-Application-Code","nexa_connect");
        using(var denied=await client.GetAsync(queue))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        factory.Authorization.Allowed=true;using(var allowed=await client.GetAsync(queue))Assert.Equal(HttpStatusCode.OK,allowed.StatusCode);
        using var rejected=await client.PostAsJsonAsync(queue+$"/{quote.QuoteId}/reject",new QrRejection(1,"closing"));Assert.Equal(HttpStatusCode.OK,rejected.StatusCode);
        using var stale=await client.PostAsJsonAsync(queue+$"/{quote.QuoteId}/reject",new QrRejection(1,"closing"));Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        factory.Authorization.Allowed=false;using var revoked=await client.GetAsync(queue);Assert.Equal(HttpStatusCode.Forbidden,revoked.StatusCode);
        factory.Port.Revoked=true;using var noGuest=await client.GetAsync("/api/order/v1/guest/qr/requests");Assert.Equal(HttpStatusCode.Unauthorized,noGuest.StatusCode);
    }
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
    private sealed class OrderFactory(bool enabled=true,bool requests=false):WebApplicationFactory<ORDER::OrderProgram>
    {
        public Port Port=new();
        public Auth Authorization=new();public Repo Repository=new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");builder.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{["Authentication:Authority"]="https://identity.test/realms/test",["QrMenu:Enabled"]=enabled.ToString(),["QrRequests:Enabled"]=requests.ToString(),["Persistence:Provider"]=requests?"PostgreSQL":"InMemory",["ConnectionStrings:Order"]="Host=127.0.0.1;Database=unused"}));
            builder.ConfigureTestServices(s=>{s.RemoveAll<IQrMenuPort>();s.AddSingleton<IQrMenuPort>(Port);s.RemoveAll<IBranchPricingPort>();s.AddSingleton<IBranchPricingPort,Policies>();s.RemoveAll<IQrRequestRepository>();s.AddSingleton<IQrRequestRepository>(Repository);s.RemoveAll<IOrderTenantAuthorizer>();s.AddSingleton<IOrderTenantAuthorizer>(Authorization);ConfigureAuth(s);});
        }
    }
    private sealed class CatalogFactory:WebApplicationFactory<CATALOG::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");builder.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{["Authentication:Authority"]="https://identity.test/realms/test",["Persistence:Provider"]="InMemory"}));builder.ConfigureTestServices(ConfigureAuth);
        }
    }
    private static void ConfigureAuth(IServiceCollection s)=>s.AddAuthentication(o=>{o.DefaultAuthenticateScheme="qr-menu-test";o.DefaultChallengeScheme="qr-menu-test";o.DefaultForbidScheme="qr-menu-test";}).AddScheme<AuthenticationSchemeOptions,Handler>("qr-menu-test",_=>{});
    private sealed class Handler(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()=>Task.FromResult(Request.Headers["X-Test-Staff"]=="yes"?AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub","operator"),new Claim(ClaimTypes.Role,"customer-user")],Scheme.Name)),Scheme.Name)):Request.Headers.TryGetValue("X-Test-Workload",out var azp)?AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity([new Claim("azp",azp.ToString()),new Claim("client_id",azp.ToString())],Scheme.Name)),Scheme.Name)):AuthenticateResult.NoResult());
    }
    private sealed class Port:IQrMenuPort
    {
        public QrGuestContext Context=new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"R","B","A1","THB",DateTimeOffset.UtcNow.AddHours(1));public Guid Product=Guid.NewGuid(),Branch;public bool Available=true,Revoked,Failure;
        public Task<QrGuestContext?> ContextAsync(string capability,CancellationToken ct)=>Task.FromResult<QrGuestContext?>(Revoked?null:Context);
        public Task<IReadOnlyList<CatalogMenuItem>> MenuAsync(Guid org,Guid branch,CancellationToken ct){if(Failure)throw new HttpRequestException("private-secret");Assert.Equal(Context.OrganizationId,org);Branch=branch;return Task.FromResult<IReadOnlyList<CatalogMenuItem>>([new(Product,"Soup",107,"THB",Available,"kitchen")]);}
    }
    private sealed class Policies:IBranchPricingPort{public Task<PricingPolicy> GetAsync(Guid org,Guid restaurant,Guid branch,CancellationToken ct)=>Task.FromResult(new PricingPolicy(1,7,true,10));}
    private sealed class Auth:IOrderTenantAuthorizer
    {
        public bool Allowed;
        public Task<bool> HasBranchAccessAsync(Guid org,Guid branch,string permission,string bearer,CancellationToken ct)=>Task.FromResult(Allowed);
        public Task<Guid?> GetBranchDecisionAsync(Guid org,Guid branch,string permission,string bearer,CancellationToken ct)=>Task.FromResult<Guid?>(Allowed?Guid.NewGuid():null);
    }
    private sealed class Repo:IQrRequestRepository
    {
        private readonly Dictionary<Guid,RetainedGuestQuote> quotes=[];private readonly Dictionary<Guid,RetainedGuestRequest> requests=[];
        public Task SaveQuoteAsync(RetainedGuestQuote quote,CancellationToken ct){quotes.Add(quote.Quote.QuoteId!.Value,quote);return Task.CompletedTask;}
        public Task<RetainedGuestRequest> SubmitAsync(GuestRequestScope scope,QrSubmission input,DateTimeOffset expiry,Guid correlation,CancellationToken ct)
        {
            if(!quotes.TryGetValue(input.QuoteId,out var quote)||quote.Scope!=scope||quote.Quote.Fingerprint!=input.Fingerprint)throw new GuestRequestConflictException();
            if(!requests.TryGetValue(input.QuoteId,out var request)){request=new(new(input.QuoteId,scope,input.Fingerprint,DateTimeOffset.UtcNow,expiry),quote.Quote);requests.Add(input.QuoteId,request);}return Task.FromResult(request);
        }
        public Task<QrRequestList> ListAsync(Guid org,Guid branch,Guid? visitor,string state,CancellationToken ct)=>Task.FromResult(new QrRequestList(requests.Values.Where(r=>r.Request.Scope.OrganizationId==org&&r.Request.Scope.BranchId==branch&&(visitor is null?r.Request.Status==state:r.Request.Scope.VisitorId==visitor)).Select(r=>r.View()).ToArray(),false));
        public Task<RetainedGuestRequest?> RejectAsync(Guid org,Guid branch,Guid id,QrRejection input,string actor,Guid decision,Guid correlation,CancellationToken ct){if(!requests.TryGetValue(id,out var r)||r.Request.Scope.OrganizationId!=org||r.Request.Scope.BranchId!=branch)return Task.FromResult<RetainedGuestRequest?>(null);r.Request.Reject(input.ExpectedVersion,input.Reason,DateTimeOffset.UtcNow);return Task.FromResult<RetainedGuestRequest?>(r);}
    }
}
