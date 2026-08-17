extern alias NOTIFICATION;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaConnect.Contracts.Platform;
using NotificationAuthorizer = NOTIFICATION::NexaConnect.Services.Notification.Application.Tenant.INotificationTenantAuthorizer;
using NotificationProgram = NOTIFICATION::Program;

namespace NexaConnect.IntegrationTests;

public sealed class NotificationHttpBoundaryTests : IClassFixture<NotificationHttpFactory>
{
    private static readonly Guid OrganizationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private readonly NotificationHttpFactory factory;

    public NotificationHttpBoundaryTests(NotificationHttpFactory factory) => this.factory = factory;

    [Fact]
    public async Task Routes_enforce_authentication_tenant_context_and_public_request_shape()
    {
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using HttpResponseMessage unauthenticated = await client.GetAsync(
            $"/api/notification/v1/notifications/{Guid.NewGuid():D}");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        using var missingContext = new HttpRequestMessage(HttpMethod.Post, "/api/notification/v1/notifications")
        {
            Content = RequestContent(Guid.NewGuid())
        };
        missingContext.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "customer");
        using HttpResponseMessage denied = await client.SendAsync(missingContext);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var mismatched = TenantRequest(HttpMethod.Post, "/api/notification/v1/notifications", Guid.NewGuid());
        mismatched.Content = RequestContent(Guid.NewGuid());
        using HttpResponseMessage mismatch = await client.SendAsync(mismatched);
        Assert.Equal(HttpStatusCode.Forbidden, mismatch.StatusCode);

        Guid callerControlledSourceEvent = Guid.NewGuid();
        using var create = TenantRequest(HttpMethod.Post, "/api/notification/v1/notifications");
        create.Content = RequestContent(callerControlledSourceEvent);
        using HttpResponseMessage created = await client.SendAsync(create);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        NotificationResponse first = Assert.IsType<NotificationResponse>(
            await created.Content.ReadFromJsonAsync<NotificationResponse>());
        Assert.Equal("queued", first.Status);

        using var replay = TenantRequest(HttpMethod.Post, "/api/notification/v1/notifications");
        replay.Content = RequestContent(callerControlledSourceEvent);
        using HttpResponseMessage replayed = await client.SendAsync(replay);
        Assert.Equal(HttpStatusCode.Created, replayed.StatusCode);
        NotificationResponse second = Assert.IsType<NotificationResponse>(
            await replayed.Content.ReadFromJsonAsync<NotificationResponse>());
        Assert.NotEqual(first.Id, second.Id);

        using var read = TenantRequest(HttpMethod.Get, $"/api/notification/v1/notifications/{first.Id:D}");
        using HttpResponseMessage found = await client.SendAsync(read);
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
    }

    private static JsonContent RequestContent(Guid sourceEventId) => JsonContent.Create(new
    {
        organizationId = OrganizationId,
        channel = "email",
        recipient = "private@example.test",
        subject = "Subject",
        body = "Private body",
        sourceEventId
    });

    private static HttpRequestMessage TenantRequest(HttpMethod method, string path, Guid? contextOrganization = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "customer");
        request.Headers.TryAddWithoutValidation(TenantContextHeaders.OrganizationId,
            (contextOrganization ?? OrganizationId).ToString("D"));
        request.Headers.TryAddWithoutValidation(TenantContextHeaders.ApplicationCode, "nexa_connect");
        return request;
    }

    private sealed record NotificationResponse(Guid Id, string Status);
}

public sealed class NotificationHttpFactory : WebApplicationFactory<NotificationProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        TestServiceConfiguration.Configure(builder, "notification");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<NotificationAuthorizer>();
            services.AddSingleton<NotificationAuthorizer>(new StubNotificationAuthorizer());
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = NotificationAuthenticationHandler.Scheme;
                options.DefaultChallengeScheme = NotificationAuthenticationHandler.Scheme;
            }).AddScheme<AuthenticationSchemeOptions, NotificationAuthenticationHandler>(
                NotificationAuthenticationHandler.Scheme, _ => { });
        });
    }

    private sealed class StubNotificationAuthorizer : NotificationAuthorizer
    {
        public Task<bool> CanAccessAsync(Guid organizationId, string permission, string authorizationHeader,
            CancellationToken cancellationToken) => Task.FromResult(
                organizationId == Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
    }
}

internal sealed class NotificationAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public new const string Scheme = "NotificationIntegrationTest";

    public NotificationAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
            return Task.FromResult(AuthenticateResult.NoResult());
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "notification-http-user")], Scheme));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme)));
    }
}
