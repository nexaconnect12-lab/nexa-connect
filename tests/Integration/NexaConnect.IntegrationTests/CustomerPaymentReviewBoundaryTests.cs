extern alias CUSTOMERBFF;

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NexaConnect.Contracts.Platform;
using CUSTOMERBFF::NexaConnect.CustomerBff;
using CUSTOMERBFF::NexaConnect.CustomerBff.Application.PaymentReviews;

namespace NexaConnect.IntegrationTests;

public sealed class CustomerPaymentReviewBoundaryTests
{
    private const string Root="/bff/customer/payment-reviews";
    private static readonly Guid OrderId=Guid.NewGuid();

    [Theory]
    [InlineData("/csrf")]
    [InlineData("/11111111-1111-1111-1111-111111111111")]
    [InlineData("/11111111-1111-1111-1111-111111111111/history")]
    [InlineData("/branches/11111111-1111-1111-1111-111111111111")]
    [InlineData("/branches/11111111-1111-1111-1111-111111111111/access")]
    public async Task Reads_require_cookie_session(string suffix)
    {
        await using var factory=new Factory();using var client=factory.CreateClient(Options());
        using var response=await client.GetAsync(Root+suffix);
        Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);Assert.Null(response.Headers.Location);
        Assert.Equal(0,factory.Port.Sends);
    }

    [Fact]
    public async Task Resolve_requires_session_and_antiforgery_before_forwarding()
    {
        await using var factory=new Factory();using var client=factory.CreateClient(Options());
        using var anonymous=await client.PostAsJsonAsync($"{Root}/{OrderId}/resolve",Body());
        Assert.Equal(HttpStatusCode.Unauthorized,anonymous.StatusCode);
        await SignIn(factory,client);
        using var missing=await client.PostAsJsonAsync($"{Root}/{OrderId}/resolve",Body());
        Assert.Equal(HttpStatusCode.BadRequest,missing.StatusCode);Assert.Equal(0,factory.Port.Sends);
    }

    [Fact]
    public async Task Resolve_uses_protected_tenant_revalidates_membership_and_preserves_conflict()
    {
        await using var factory=new Factory();using var client=factory.CreateClient(Options());await SignIn(factory,client);
        var csrf=await client.GetFromJsonAsync<JsonElement>(Root+"/csrf");
        client.DefaultRequestHeaders.Add("X-Nexa-CSRF",csrf.GetProperty("requestToken").GetString());
        factory.Port.Status=HttpStatusCode.Conflict;
        using var response=await client.PostAsJsonAsync($"{Root}/{OrderId}/resolve?organizationId={Guid.NewGuid()}",Body());
        Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);
        Assert.DoesNotContain("downstream-sensitive",await response.Content.ReadAsStringAsync());
        Assert.Equal(factory.Port.OrganizationId,factory.Port.LastTenant!.OrganizationId);
        Assert.Equal("operator",factory.Port.LastTenant.SubjectId);
        Assert.Equal("resume_payment",factory.Port.LastRequest!.Resolution);
        Assert.Equal(7,factory.Port.LastRequest.ExpectedConcurrencyVersion);
        Assert.Equal(1,factory.Port.Sends);
        factory.Port.Allowed=false;
        using var denied=await client.GetAsync($"{Root}/{OrderId}/history");
        Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.Equal(1,factory.Port.Sends);
    }

    [Theory]
    [InlineData("", "resume_payment",1)]
    [InlineData("   ", "resume_payment",1)]
    [InlineData("verified", "refund",1)]
    [InlineData("verified", "resume_payment",0)]
    public async Task Invalid_decisions_never_reach_Order(string reason,string resolution,long version)
    {
        await using var factory=new Factory();using var client=factory.CreateClient(Options());await SignIn(factory,client);
        var csrf=await client.GetFromJsonAsync<JsonElement>(Root+"/csrf");client.DefaultRequestHeaders.Add("X-Nexa-CSRF",csrf.GetProperty("requestToken").GetString());
        using var response=await client.PostAsJsonAsync($"{Root}/{OrderId}/resolve",new{reason,resolution,expectedConcurrencyVersion=version});
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);Assert.Equal(0,factory.Port.Sends);
    }

    [Theory]
    [InlineData("other-subject","nexa_connect",401)]
    [InlineData("operator","other_product",403)]
    public async Task Wrong_subject_or_product_cannot_read(string subject,string product,int expected)
    {
        await using var factory=new Factory();using var client=factory.CreateClient(Options());await SignIn(factory,client,subject,product);
        using var response=await client.GetAsync($"{Root}/{OrderId}");
        Assert.Equal(expected,(int)response.StatusCode);Assert.Equal(0,factory.Port.Sends);
    }

    private static object Body()=>new{resolution="resume_payment",reason="verified externally",expectedConcurrencyVersion=7,organizationId=Guid.NewGuid(),actorSubjectId="spoofed",authorizationDecisionId=Guid.NewGuid()};
    private static WebApplicationFactoryClientOptions Options()=>new(){AllowAutoRedirect=false,BaseAddress=new Uri("https://localhost")};
    private static async Task SignIn(Factory factory,HttpClient client,string tenantSubject="operator",string product="nexa_connect")
    {
        var options=factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get("CustomerCookie");
        var properties=new AuthenticationProperties();properties.StoreTokens([
            new AuthenticationToken{Name="access_token",Value="test-server-token"},
            new AuthenticationToken{Name="expires_at",Value=DateTimeOffset.UtcNow.AddHours(1).ToString("O")},
        ]);
        var principal=new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub","operator"),new Claim(ClaimTypes.NameIdentifier,"operator")],"CustomerCookie"));
        var ticket=new AuthenticationTicket(principal,properties,"CustomerCookie");
        string key=await options.SessionStore!.StoreAsync(ticket);
        var cookiePrincipal=new ClaimsPrincipal(new ClaimsIdentity([new Claim("Microsoft.AspNetCore.Authentication.Cookies-SessionId",key)],"CustomerCookie"));
        string session=options.TicketDataFormat.Protect(new AuthenticationTicket(cookiePrincipal,null,"CustomerCookie"));
        string tenant=factory.Services.GetRequiredService<TenantSelectionCookie>().Protect(new(tenantSubject,factory.Port.OrganizationId,product));
        client.DefaultRequestHeaders.Add("Cookie",$"{options.Cookie.Name}={session}; __Host-nexa-customer-tenant={tenant}");
    }

    private sealed class Factory:WebApplicationFactory<CUSTOMERBFF::Program>
    {
        public ReviewPort Port{get;}=new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_,configuration)=>configuration.AddInMemoryCollection(new Dictionary<string,string?>
            {
                ["Bff:Authority"]="https://identity.test/realms/test",["Bff:ClientId"]="test",["Bff:ClientSecret"]="test-secret",
                ["Services:PlatformDirectory"]="https://directory.test/",["Services:Order"]="https://order.test/",
                ["Services:Restaurant"]="https://restaurant.test/",["Services:Reporting"]="https://reporting.test/",
                ["Services:Media"]="https://media.test/",["Services:Notification"]="https://notification.test/",
                ["Services:Catalog"]="https://catalog.test/",["Services:Inventory"]="https://inventory.test/",
            }));
            builder.ConfigureTestServices(services=>{services.RemoveAll<ICustomerPaymentReviewPort>();services.AddSingleton<ICustomerPaymentReviewPort>(Port);});
        }
    }
    private sealed class ReviewPort:ICustomerPaymentReviewPort
    {
        public Guid OrganizationId{get;}=Guid.NewGuid();
        public bool Allowed{get;set;}=true;
        public int Sends{get;private set;}
        public HttpStatusCode Status{get;set;}=HttpStatusCode.OK;
        public TenantContext? LastTenant{get;private set;}
        public PaymentReviewResolutionRequest? LastRequest{get;private set;}
        public Task<CurrentPlatformAccessResponse?> GetAccessAsync(string token,CancellationToken ct)=>Task.FromResult<CurrentPlatformAccessResponse?>(new("operator",Allowed?[new(OrganizationId,"test","Test","nexa_connect")]:[]));
        public Task<HttpResponseMessage> SendAsync(TenantContext tenant,string token,ReviewOperation operation,Guid id,PaymentReviewResolutionRequest? request,CancellationToken ct)
        {Assert.Equal("test-server-token",token);Sends++;LastTenant=tenant;LastRequest=request;return Task.FromResult(new HttpResponseMessage(Status){Content=JsonContent.Create(new{message="downstream-sensitive"})});}
    }
}
