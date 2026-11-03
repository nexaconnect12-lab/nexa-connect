extern alias CUSTOMERBFF;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
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
using CUSTOMERBFF::NexaConnect.CustomerBff.Application.Media;
namespace NexaConnect.IntegrationTests;

public sealed class CustomerMediaLibraryBoundaryTests
{
    private const string Root="/bff/customer/media-library";
    [Fact]public async Task Session_subject_membership_and_server_selected_tenant_protect_browsing()
    {
        await using var f=new Factory();using var client=f.CreateClient(Options());
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync(Root)).StatusCode);
        await SignIn(f,client);using var response=await client.GetAsync(Root+"?organizationId="+Guid.NewGuid()+"&search=photo&page=2&readyOnly=true");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal(f.Port.Org,f.Port.LastOrg);Assert.True(response.Headers.CacheControl?.NoStore);
        f.Port.Allowed=false;Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(Root)).StatusCode);Assert.Equal(1,f.Port.Sends);
    }
    [Theory][InlineData("wrong","nexa_connect",401)][InlineData("operator","other",403)]
    public async Task Invalid_cookie_subject_or_workspace_never_reaches_media(string subject,string app,int status)
    {
        await using var f=new Factory();using var client=f.CreateClient(Options());await SignIn(f,client,subject,app);
        Assert.Equal(status,(int)(await client.GetAsync(Root)).StatusCode);Assert.Equal(0,f.Port.Sends);
    }
    [Fact]public async Task Thumbnail_is_bounded_png_no_store_and_downstream_errors_never_disclose_bodies()
    {
        await using var f=new Factory();using var client=f.CreateClient(Options());await SignIn(f,client);
        string path=Root+"/assets/"+Guid.NewGuid()+"/thumbnail";
        using var image=await client.GetAsync(path);Assert.Equal(HttpStatusCode.OK,image.StatusCode);Assert.Equal("image/png",image.Content.Headers.ContentType!.MediaType);Assert.True(image.Headers.CacheControl?.NoStore);Assert.Equal("nosniff",Assert.Single(image.Headers.GetValues("X-Content-Type-Options")));
        f.Port.BadMime=true;Assert.Equal(HttpStatusCode.ServiceUnavailable,(await client.GetAsync(path)).StatusCode);
        f.Port.BadMime=false;f.Port.Size=524289;Assert.Equal(HttpStatusCode.ServiceUnavailable,(await client.GetAsync(path)).StatusCode);
        f.Port.Status=HttpStatusCode.Forbidden;using var denied=await client.GetAsync(path);Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.DoesNotContain("downstream-sensitive",await denied.Content.ReadAsStringAsync());
    }
    [Fact]public async Task Invalid_page_or_search_is_not_forwarded()
    {
        await using var f=new Factory();using var client=f.CreateClient(Options());await SignIn(f,client);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync(Root+"?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync(Root+"?search="+new string('x',101))).StatusCode);Assert.Equal(0,f.Port.Sends);
    }
    private static WebApplicationFactoryClientOptions Options()=>new(){AllowAutoRedirect=false,BaseAddress=new("https://localhost")};
    private static async Task SignIn(Factory f,HttpClient client,string subject="operator",string app="nexa_connect")
    {
        var options=f.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get("CustomerCookie");var properties=new AuthenticationProperties();
        properties.StoreTokens([new AuthenticationToken{Name="access_token",Value="test-server-token"},new AuthenticationToken{Name="expires_at",Value=DateTimeOffset.UtcNow.AddHours(1).ToString("O")}]);
        var principal=new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub","operator"),new Claim(ClaimTypes.NameIdentifier,"operator")],"CustomerCookie"));
        string key=await options.SessionStore!.StoreAsync(new(principal,properties,"CustomerCookie"));
        var sessionPrincipal=new ClaimsPrincipal(new ClaimsIdentity([new Claim("Microsoft.AspNetCore.Authentication.Cookies-SessionId",key)],"CustomerCookie"));
        string session=options.TicketDataFormat.Protect(new(sessionPrincipal,null,"CustomerCookie"));
        string tenant=f.Services.GetRequiredService<TenantSelectionCookie>().Protect(new(subject,f.Port.Org,app));client.DefaultRequestHeaders.Add("Cookie",$"{options.Cookie.Name}={session}; __Host-nexa-customer-tenant={tenant}");
    }
    private sealed class Factory:WebApplicationFactory<CUSTOMERBFF::Program>
    {
        public Port Port=new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");builder.ConfigureAppConfiguration((_,configuration)=>configuration.AddInMemoryCollection(new Dictionary<string,string?>
            {
                ["Bff:Authority"]="https://identity.test/realms/test",["Bff:ClientId"]="test",["Bff:ClientSecret"]="test-secret",
                ["Services:PlatformDirectory"]="https://directory.test/",["Services:Order"]="https://order.test/",["Services:Restaurant"]="https://restaurant.test/",["Services:Reporting"]="https://reporting.test/",["Services:Media"]="https://media.test/",["Services:Notification"]="https://notification.test/",["Services:Catalog"]="https://catalog.test/",["Services:Inventory"]="https://inventory.test/"
            }));builder.ConfigureTestServices(services=>{services.RemoveAll<ICustomerMediaLibraryPort>();services.AddSingleton<ICustomerMediaLibraryPort>(Port);});
        }
    }
    private sealed class Port:ICustomerMediaLibraryPort
    {
        public Guid Org=Guid.NewGuid(),LastOrg;public bool Allowed=true,BadMime;public int Sends,Size;public HttpStatusCode Status=HttpStatusCode.OK;
        public Task<CurrentPlatformAccessResponse?> AccessAsync(string token,CancellationToken ct)=>Task.FromResult<CurrentPlatformAccessResponse?>(new("operator",Allowed?[new(Org,"test","Test","nexa_connect")]:[]));
        public Task<HttpResponseMessage> SendAsync(Guid organization,Guid? asset,string search,int page,bool readyOnly,string token,CancellationToken ct)
        {
            Sends++;LastOrg=organization;HttpContent content=asset.HasValue?new ByteArrayContent(new byte[Size>0?Size:4]):JsonContent.Create(new{items=Array.Empty<object>(),hasMore=false,page});
            if(asset.HasValue)content.Headers.ContentType=new(BadMime?"image/svg+xml":"image/png");
            if(Status!=HttpStatusCode.OK)content=new StringContent("downstream-sensitive");return Task.FromResult(new HttpResponseMessage(Status){Content=content});
        }
    }
}
