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
using CustomerMediaController = MEDIA::NexaConnect.Services.Media.Controllers.CustomerMediaController;
using IMediaAssetRepository = MEDIA::NexaConnect.Services.Media.Application.IMediaAssetRepository;
using IMediaCustomerAuthorizer = MEDIA::NexaConnect.Services.Media.Application.IMediaCustomerAuthorizer;
using MediaAssetQueries = MEDIA::NexaConnect.Services.Media.Application.MediaAssetQueries;
using MediaAssetSummary = MEDIA::NexaConnect.Services.Media.Application.MediaAssetSummary;
using MediaVariantSummary = MEDIA::NexaConnect.Services.Media.Application.MediaVariantSummary;
using MediaManagement = MEDIA::NexaConnect.Services.Media.Application.MediaManagement;
using IMediaManagementRepository = MEDIA::NexaConnect.Services.Media.Application.IMediaManagementRepository;
using IMediaObjectStorage = MEDIA::NexaConnect.Services.Media.Application.IMediaObjectStorage;
using IMediaOwnerValidator = MEDIA::NexaConnect.Services.Media.Application.IMediaOwnerValidator;
using IMediaContentSafety = MEDIA::NexaConnect.Services.Media.Application.IMediaContentSafety;
using MediaQuota = MEDIA::NexaConnect.Services.Media.Application.MediaQuota;

namespace NexaConnect.IntegrationTests;

public sealed class MediaAuthenticatedHttpAcceptanceTests
{
    [Theory]
    [InlineData("customer-admin",true,200)]
    [InlineData("customer-user",true,200)]
    [InlineData("customer-user",false,403)]
    [InlineData("unrelated",true,403)]
    public async Task Authenticated_media_read_uses_route_organization_and_authorization_boundary(string role,bool granted,int expected)
    {
        Guid organizationId = Guid.NewGuid(); var repository = new CapturingAssets(); var authorizer = new CapturingAuthorizer(granted);
        WebApplicationBuilder builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication(options => { options.DefaultAuthenticateScheme = MediaAuthenticationHandler.Scheme; options.DefaultChallengeScheme = MediaAuthenticationHandler.Scheme; }).AddScheme<AuthenticationSchemeOptions,MediaAuthenticationHandler>(MediaAuthenticationHandler.Scheme,_=>{});
        builder.Services.AddAuthorization(); builder.Services.AddControllers().AddApplicationPart(typeof(CustomerMediaController).Assembly);
        builder.Services.AddSingleton<IMediaAssetRepository>(repository); builder.Services.AddScoped<MediaAssetQueries>(); builder.Services.AddSingleton<IMediaCustomerAuthorizer>(authorizer);
        builder.Services.AddSingleton<MediaManagement>(provider => new MediaManagement(null!,null!,null!,null!,new MediaQuota(1,1)));
        await using WebApplication app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
        using HttpClient client = app.GetTestClient(); client.DefaultRequestHeaders.Authorization = new("Bearer","customer-token");client.DefaultRequestHeaders.Add("X-Test-Role",role);
        using HttpResponseMessage response = await client.GetAsync($"/api/media/v1/customer/organizations/{organizationId:D}/assets");
        Assert.Equal(expected,(int)response.StatusCode);if(expected==200){Assert.Equal(organizationId,repository.OrganizationId);Assert.Equal(organizationId,authorizer.OrganizationId);}else Assert.Equal(Guid.Empty,repository.OrganizationId);
    }

    private sealed class CapturingAssets : IMediaAssetRepository
    {
        public Guid OrganizationId { get; private set; }
        public Task<IReadOnlyCollection<MediaAssetSummary>> ListAsync(Guid organizationId,CancellationToken cancellationToken){OrganizationId=organizationId;return Task.FromResult<IReadOnlyCollection<MediaAssetSummary>>([]);}
        public Task<IReadOnlyCollection<MediaVariantSummary>> ListVariantsAsync(Guid organizationId,Guid assetId,CancellationToken cancellationToken){OrganizationId=organizationId;return Task.FromResult<IReadOnlyCollection<MediaVariantSummary>>([]);}
    }
    private sealed class CapturingAuthorizer(bool granted) : IMediaCustomerAuthorizer
    {
        public Guid OrganizationId { get; private set; }
        public Task<bool> IsGrantedAsync(Guid organizationId,string permission,string authorizationHeader,CancellationToken cancellationToken){OrganizationId=organizationId;return Task.FromResult(granted);}
    }
}

internal sealed class MediaAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
{
    public new const string Scheme="MediaAcceptance";
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        Claim[] claims=[new("sub","media-acceptance-user"),new(ClaimTypes.Role,Request.Headers["X-Test-Role"].ToString())];
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims,Scheme)),Scheme)));
    }
}
