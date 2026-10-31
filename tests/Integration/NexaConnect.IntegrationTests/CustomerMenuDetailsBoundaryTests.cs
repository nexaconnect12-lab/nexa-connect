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
using CUSTOMERBFF::NexaConnect.CustomerBff.Application.Catalog;

namespace NexaConnect.IntegrationTests;

public sealed class CustomerMenuDetailsBoundaryTests
{
    private static readonly Guid Branch=Guid.NewGuid();
    private static string Root=>$"/bff/customer/menu-details/branches/{Branch:D}";
    [Fact] public async Task Oversized_downstream_details_are_rejected_without_exposing_the_body()
    {
        await using var factory=new Factory();using var client=factory.CreateClient(Options());await SignIn(factory,client);
        factory.Port.ResponseSize=524289;using var response=await client.GetAsync(Root);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);Assert.DoesNotContain(new string('x',100),await response.Content.ReadAsStringAsync());
    }
    [Fact] public async Task Session_csrf_tenant_membership_and_safe_errors_are_enforced()
    {
        await using var factory=new Factory();using var client=factory.CreateClient(Options());
        using var anonymous=await client.GetAsync(Root);Assert.Equal(HttpStatusCode.Unauthorized,anonymous.StatusCode);
        await SignIn(factory,client);
        var update=new NexaConnect.Contracts.Catalog.UpdateMenuDetails("Edited",108,"bar",7);
        using var missing=await client.PutAsJsonAsync(Root+"/products/"+Guid.NewGuid(),update);Assert.Equal(HttpStatusCode.BadRequest,missing.StatusCode);Assert.Equal(0,factory.Port.Sends);
        var csrf=await client.GetFromJsonAsync<JsonElement>("/bff/customer/menu-details/csrf");client.DefaultRequestHeaders.Add("X-Nexa-CSRF",csrf.GetProperty("requestToken").GetString());
        factory.Port.Status=HttpStatusCode.Conflict;
        using var conflict=await client.PutAsJsonAsync(Root+"/products/"+Guid.NewGuid()+"?organizationId="+Guid.NewGuid(),update);
        Assert.Equal(HttpStatusCode.Conflict,conflict.StatusCode);Assert.DoesNotContain("downstream-sensitive",await conflict.Content.ReadAsStringAsync());Assert.True(conflict.Headers.CacheControl?.NoStore);
        Assert.Equal(factory.Port.OrganizationId,factory.Port.LastTenant!.OrganizationId);Assert.Equal(7,factory.Port.LastUpdate!.ExpectedVersion);
        factory.Port.Allowed=false;using var denied=await client.GetAsync(Root);Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.Equal(1,factory.Port.Sends);
    }
    [Theory][InlineData("wrong","nexa_connect",401)][InlineData("operator","other",403)]
    public async Task Invalid_subject_or_product_never_reaches_owning_service(string subject,string product,int expected)
    {
        await using var factory=new Factory();using var client=factory.CreateClient(Options());await SignIn(factory,client,subject,product);
        using var response=await client.GetAsync(Root);Assert.Equal(expected,(int)response.StatusCode);Assert.Equal(0,factory.Port.Sends);
    }
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
        public AvailabilityPort Port{get;}=new();
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
            builder.ConfigureTestServices(services=>{services.RemoveAll<ICustomerMenuDetailsPort>();services.AddSingleton<ICustomerMenuDetailsPort>(Port);});
        }
    }
    private sealed class AvailabilityPort:ICustomerMenuDetailsPort
    {
        public Guid OrganizationId{get;}=Guid.NewGuid();public bool Allowed=true;public int Sends,ResponseSize;public HttpStatusCode Status=HttpStatusCode.OK;
        public TenantContext? LastTenant;public NexaConnect.Contracts.Catalog.UpdateMenuDetails? LastUpdate;
        public Task<CurrentPlatformAccessResponse?> AccessAsync(string token,CancellationToken ct)=>Task.FromResult<CurrentPlatformAccessResponse?>(new("operator",Allowed?[new(OrganizationId,"test","Test","nexa_connect")]:[]));
        public Task<HttpResponseMessage> SendAsync(TenantContext tenant,Guid branch,Guid? product,NexaConnect.Contracts.Catalog.UpdateMenuDetails? update,string token,CancellationToken ct)
        {Sends++;LastTenant=tenant;LastUpdate=update;Assert.Equal(Branch,branch);return Task.FromResult(new HttpResponseMessage(Status){Content=ResponseSize>0?new StringContent(new string('x',ResponseSize)):JsonContent.Create(new{message="downstream-sensitive"})});}
    }
}
