extern alias MEDIA;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MEDIA::NexaConnect.Services.Media.Application;
namespace NexaConnect.IntegrationTests;

public sealed class MediaLibraryBoundaryTests
{
    [Theory][InlineData("customer-manager",true,200)][InlineData("customer-viewer",true,200)][InlineData("customer-user",false,403)][InlineData("unrelated",true,403)]
    public async Task Customer_role_and_live_read_permission_gate_the_library(string role,bool allowed,int expected)
    {
        var f=new Fixture{Allowed=allowed};await using var app=await Host(f);using var client=app.GetTestClient();
        client.DefaultRequestHeaders.Authorization=new("Bearer","fixture");client.DefaultRequestHeaders.Add("X-Test-Role",role);
        using var response=await client.GetAsync($"/api/media/v1/customer/organizations/{f.Org}/library?search=rice&page=2&readyOnly=true");
        Assert.Equal(expected,(int)response.StatusCode);Assert.Equal(expected==200?1:0,f.Lists);
        if(expected==200){Assert.True(response.Headers.CacheControl?.NoStore);Assert.Equal(new("rice",2,true),f.Query);}
    }
    [Fact]public async Task Anonymous_wrong_scope_and_revoked_membership_return_no_page()
    {
        var f=new Fixture();await using var app=await Host(f);using var client=app.GetTestClient();
        string root=$"/api/media/v1/customer/organizations/{f.Org}/library";
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync(root)).StatusCode);
        client.DefaultRequestHeaders.Authorization=new("Bearer","fixture");client.DefaultRequestHeaders.Add("X-Test-Role","customer-manager");
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync($"/api/media/v1/customer/organizations/{Guid.NewGuid()}/library")).StatusCode);Assert.Equal(0,f.Lists);
        f.RevokeAfterList=true;Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(root)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(root+"/assets/"+Guid.NewGuid()+"/thumbnail")).StatusCode);
    }
    [Fact]public async Task Invalid_query_is_rejected_before_persistence()
    {
        var f=new Fixture();await using var app=await Host(f);using var client=app.GetTestClient();client.DefaultRequestHeaders.Authorization=new("Bearer","fixture");client.DefaultRequestHeaders.Add("X-Test-Role","customer-manager");
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync($"/api/media/v1/customer/organizations/{f.Org}/library?page=10001")).StatusCode);Assert.Equal(0,f.Lists);
    }
    private static async Task<WebApplication> Host(Fixture f)
    {
        var b=WebApplication.CreateBuilder();b.WebHost.UseTestServer();b.Services.AddLogging(l=>l.ClearProviders());
        b.Services.AddAuthentication("Library").AddScheme<AuthenticationSchemeOptions,Identity>("Library",_=>{});b.Services.AddAuthorization();
        b.Services.AddControllers().AddApplicationPart(typeof(MEDIA::NexaConnect.Services.Media.Controllers.CustomerMediaLibraryController).Assembly);
        b.Services.AddSingleton(new MediaLibrary(f,new MenuPhotoDelivery(null!,null!,null!),f));
        var app=b.Build();app.UseAuthentication();app.UseAuthorization();app.MapControllers();await app.StartAsync();return app;
    }
    private sealed class Fixture:IMediaLibraryRepository,IMediaCustomerAuthorizer
    {
        public Guid Org=Guid.NewGuid();public bool Allowed=true,RevokeAfterList;public int Lists;public MediaLibraryQuery? Query;
        public Task<bool> IsGrantedAsync(Guid org,string permission,string bearer,CancellationToken ct)=>Task.FromResult(Allowed&&org==Org);
        public Task<MediaLibraryPage> BrowseAsync(Guid org,MediaLibraryQuery query,CancellationToken ct){Lists++;Query=query;if(RevokeAfterList)Allowed=false;return Task.FromResult(new MediaLibraryPage([],false,query.Page));}
    }
    private sealed class Identity(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()=>Task.FromResult(!Request.Headers.ContainsKey("Authorization")?AuthenticateResult.NoResult():AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub","operator"),new Claim(ClaimTypes.Role,Request.Headers["X-Test-Role"].ToString())],"Library")),"Library")));
    }
}
