extern alias MEDIA;
extern alias RESTAURANT;
extern alias AUTH;
extern alias DIRECTORY;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaConnect.Contracts.Media;
using NexaConnect.Contracts.Platform;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Infrastructure.Http;
using MEDIA::NexaConnect.Services.Media.Application;
using MEDIA::NexaConnect.Services.Media.Infrastructure;
using RESTAURANT::NexaConnect.Services.Restaurant.Application.Configuration;
using DIRECTORY::NexaConnect.Services.PlatformDirectory.Application.Access;
using AUTH::NexaConnect.Services.Authorization.Application.Decisions;

namespace NexaConnect.IntegrationTests;

public sealed class GalleryReaderBoundaryTests
{
    [Theory]
    [InlineData("directory",null,401)][InlineData("directory","cashier",403)][InlineData("directory","other-workload",403)][InlineData("directory","nexaconnect-media-service",200)]
    [InlineData("authorization",null,401)][InlineData("authorization","cashier",403)][InlineData("authorization","other-workload",403)][InlineData("authorization","nexaconnect-media-service",200)]
    [InlineData("restaurant",null,401)][InlineData("restaurant","cashier",403)][InlineData("restaurant","other-workload",403)][InlineData("restaurant","nexaconnect-media-service",200)]
    public async Task Delegation_is_reserved_for_Media_workload(string owner,string? identity,int expected)
    {
        var ports=new Ports();await using var app=await Host(ports);using var client=app.GetTestClient();
        if(identity is not null)client.DefaultRequestHeaders.Add("X-Test-Identity",identity);
        var path=owner switch{"directory"=>"/api/platform-directory/v1/internal/gallery-access","authorization"=>"/api/authorization/v1/internal/gallery-access",
            _=>$"/api/restaurant/v1/internal/organizations/{ports.Org}/branches/{ports.Branch}/screensaver-gallery"};
        object body=owner switch{"directory"=>new GalleryMembershipQuery(ports.Org,"original"),"authorization"=>new GalleryPermissionQuery(ports.Org,ports.Restaurant,ports.Branch,"original"),_=>new GalleryViewer("original")};
        using var response=await client.PostAsJsonAsync(path,body);
        Assert.Equal(expected,(int)response.StatusCode);
        if(expected==200){Assert.True(response.Headers.CacheControl?.NoStore);Assert.Equal("original",ports.Subject);}
        else Assert.Null(ports.Subject);
        if(owner=="authorization"&&expected==200)Assert.Equal(ProductPermissions.CatalogMenuRead,ports.Permission);
    }
    [Theory]
    [InlineData(null,401)][InlineData("other-workload",403)][InlineData("cashier",200)]
    public async Task Credential_issuance_requires_customer_authentication_and_live_scope(string? identity,int expected)
    {
        var ports=new Ports();await using var app=await Host(ports);using var client=app.GetTestClient();
        if(identity is not null)client.DefaultRequestHeaders.Add("X-Test-Identity",identity);
        using var response=await client.PostAsync($"/api/media/v1/customer/organizations/{ports.Org}/branches/{ports.Branch}/screensaver-gallery/readers",null);
        Assert.Equal(expected,(int)response.StatusCode);
        if(expected==200){var grant=await response.Content.ReadFromJsonAsync<GalleryReaderCredential>();Assert.NotNull(grant);Assert.True(response.Headers.CacheControl?.NoStore);}
    }
    [Fact]
    public async Task Locked_read_accepts_only_header_capability_and_rechecks_live_revocation_without_cookies_or_Bearer()
    {
        var ports=new Ports();await using var app=await Host(ports);using var client=app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Identity","cashier");
        using var issued=await client.PostAsync($"/api/media/v1/customer/organizations/{ports.Org}/branches/{ports.Branch}/screensaver-gallery/readers",null);
        var reader=(await issued.Content.ReadFromJsonAsync<GalleryReaderCredential>())!;
        client.DefaultRequestHeaders.Clear();var path=$"/api/media/v1/gallery-readers/organizations/{ports.Org}/branches/{ports.Branch}";
        using var missing=await client.GetAsync(path);Assert.Equal(HttpStatusCode.Unauthorized,missing.StatusCode);
        client.DefaultRequestHeaders.Authorization=new("Bearer",reader.Credential);
        using var bearer=await client.GetAsync(path);Assert.Equal(HttpStatusCode.Unauthorized,bearer.StatusCode);client.DefaultRequestHeaders.Authorization=null;
        client.DefaultRequestHeaders.Add("X-Nexa-Gallery-Reader",reader.Credential);
        using var query=await client.GetAsync(path+"?reader=forbidden");Assert.Equal(HttpStatusCode.Unauthorized,query.StatusCode);
        using var ok=await client.GetAsync(path);Assert.Equal(HttpStatusCode.OK,ok.StatusCode);Assert.True(ok.Headers.CacheControl?.NoStore);
        Assert.Equal("operator",ports.Subject);
        using var scope=await client.GetAsync($"/api/media/v1/gallery-readers/organizations/{Guid.NewGuid()}/branches/{ports.Branch}");Assert.Equal(HttpStatusCode.Forbidden,scope.StatusCode);
        ports.Granted=false;using var denied=await client.GetAsync(path);Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        ports.Granted=true;ports.Outage=true;using var unavailable=await client.GetAsync(path);Assert.Equal(HttpStatusCode.ServiceUnavailable,unavailable.StatusCode);
        ports.Outage=false;using var recovered=await client.GetAsync(path);Assert.Equal(HttpStatusCode.OK,recovered.StatusCode);
        client.DefaultRequestHeaders.Remove("X-Nexa-Gallery-Reader");client.DefaultRequestHeaders.Add("X-Nexa-Gallery-Reader","tampered");
        using var tampered=await client.GetAsync(path);Assert.Equal(HttpStatusCode.Forbidden,tampered.StatusCode);
    }
    private static async Task<WebApplication> Host(Ports ports)
    {
        var builder=WebApplication.CreateBuilder();builder.WebHost.UseTestServer();
        builder.Services.AddLogging(logging=>logging.ClearProviders());
        builder.Services.AddAuthentication("GalleryTest").AddScheme<AuthenticationSchemeOptions,Identity>("GalleryTest",_=>{});
        builder.Services.AddAuthorization(options=>options.AddPolicy(NexaAuthorizationPolicies.GalleryMediaWorkload,
            p=>p.RequireAuthenticatedUser().RequireClaim("azp","nexaconnect-media-service")));
        builder.Services.AddControllers().AddApplicationPart(typeof(MEDIA::NexaConnect.Services.Media.Controllers.GalleryReadersController).Assembly)
            .AddApplicationPart(typeof(RESTAURANT::NexaConnect.Services.Restaurant.Controllers.GalleryReaderController).Assembly)
            .AddApplicationPart(typeof(DIRECTORY::NexaConnect.Services.PlatformDirectory.Controllers.GalleryAccessController).Assembly)
            .AddApplicationPart(typeof(AUTH::NexaConnect.Services.Authorization.Controllers.GalleryAccessController).Assembly);
        builder.Services.AddSingleton<IOrganizationAccessReader>(ports);builder.Services.AddScoped<GalleryMembership>();
        builder.Services.AddSingleton<IAuthorizationDecisionService>(ports);builder.Services.AddScoped<GalleryPermission>();
        builder.Services.AddSingleton<IBranchGalleryRepository>(ports);builder.Services.AddSingleton<IGalleryReaderAuthorizer>(ports);builder.Services.AddScoped<GalleryReaderSelection>();
        builder.Services.AddSingleton<IBranchGallerySelectionPort>(ports);builder.Services.AddSingleton<IDelegatedGallerySelection>(ports);
        builder.Services.AddSingleton<IGalleryReaderProtector>(new GalleryReaderProtector(new EphemeralDataProtectionProvider()));
        builder.Services.AddSingleton(new BranchGalleryDelivery(ports,null!,null!,null!,TimeProvider.System));builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(new BoundedRequestGate(TimeProvider.System));builder.Services.AddScoped<GalleryReaders>();
        var app=builder.Build();app.UseAuthentication();app.UseAuthorization();app.MapControllers();await app.StartAsync();return app;
    }
    private sealed class Identity(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string identity=Request.Headers["X-Test-Identity"].ToString();if(string.IsNullOrEmpty(identity))return Task.FromResult(AuthenticateResult.NoResult());
            var principal=new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub","operator"),new Claim("azp",identity),new Claim(ClaimTypes.Role,identity=="cashier"?"customer-user":"workload")],Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal,Scheme.Name)));
        }
    }
    private sealed class Ports:IOrganizationAccessReader,IAuthorizationDecisionService,IBranchGalleryRepository,IGalleryReaderAuthorizer,IBranchGallerySelectionPort,IDelegatedGallerySelection
    {
        public Guid Org=Guid.NewGuid(),Restaurant=Guid.NewGuid(),Branch=Guid.NewGuid();public string? Subject,Permission;public bool Granted=true,Outage;
        public Task<bool> HasNexaConnectAccessAsync(Guid org,string subject,CancellationToken ct){Subject=subject;return Task.FromResult(Granted&&org==Org);}
        public Task<IReadOnlyList<OrganizationApplicationAccess>> GetCurrentAccessAsync(string subject,CancellationToken ct)=>throw new NotSupportedException();
        public Task<AuthorizationDecision> DecideAsync(string subject,Guid org,Guid? restaurant,Guid? branch,string permission,decimal? amount,string? currency,CancellationToken ct)
        {Assert.Null(amount);Assert.Null(currency);Subject=subject;Permission=permission;return Task.FromResult(new AuthorizationDecision(Guid.NewGuid(),Granted,null));}
        public Task<Guid?> RestaurantAsync(Guid org,Guid branch,CancellationToken ct)=>Task.FromResult<Guid?>(org==Org&&branch==Branch?Restaurant:null);
        public Task<BranchGallery?> GetAsync(Guid org,Guid branch,CancellationToken ct)=>Task.FromResult<BranchGallery?>(new(org,branch,1,[],[]));
        public Task<BranchGallery?> SaveAsync(Guid org,Guid branch,UpdateBranchGallery update,string actor,CancellationToken ct)=>throw new NotSupportedException();
        public Task<bool> GrantedAsync(Guid org,Guid restaurant,Guid branch,string subject,string bearer,CancellationToken ct){Subject=subject;return Task.FromResult(Granted);}
        public Task<BranchGallery> ReadAsync(Guid org,Guid branch,string subject,CancellationToken ct)
        {Subject=subject;if(!Granted)throw new UnauthorizedAccessException();if(Outage)throw new HttpRequestException();return Task.FromResult(new BranchGallery(Org,Branch,1,[],[]));}
    }
}
