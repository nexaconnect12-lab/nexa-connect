using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NexaConnect.Infrastructure.Authentication;

namespace NexaConnect.UnitTests;

public sealed class BffAccessTokenServiceTests
{
    [Fact]
    public async Task Expired_access_token_is_refreshed_and_ticket_is_updated()
    {
        var properties = new AuthenticationProperties();
        properties.StoreTokens([
            new AuthenticationToken { Name = "access_token", Value = "expired" },
            new AuthenticationToken { Name = "refresh_token", Value = "refresh" },
            new AuthenticationToken { Name = "expires_at", Value = "2025-01-01T00:00:00.0000000+00:00" }
        ]);
        var authentication = new RecordingAuthenticationService(new AuthenticateResult(
            new AuthenticationTicket(new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity("cookie")), properties, "cookie")));
        var services = new ServiceCollection().AddSingleton<IAuthenticationService>(authentication).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        var client = new HttpClient(new StubHandler()) { BaseAddress = new Uri("https://identity.test/") };
        var service = new BffAccessTokenService(new StubFactory(client), TimeProvider.System, NullLogger<BffAccessTokenService>.Instance);

        string? token = await service.GetValidAccessTokenAsync(context, "cookie", "https://identity.test", "client", "secret", default);

        Assert.Equal("new-access", token);
        Assert.Equal("new-refresh", properties.GetTokenValue("refresh_token"));
        Assert.True(authentication.SignedIn);
        Assert.False(authentication.SignedOut);
    }

    private sealed class StubFactory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string json = request.Method == HttpMethod.Get
                ? "{\"token_endpoint\":\"https://identity.test/token\"}"
                : "{\"access_token\":\"new-access\",\"refresh_token\":\"new-refresh\",\"expires_in\":300}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
    private sealed class RecordingAuthenticationService(AuthenticateResult result) : IAuthenticationService
    {
        public bool SignedIn { get; private set; }
        public bool SignedOut { get; private set; }
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => Task.FromResult(result);
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignInAsync(HttpContext context, string? scheme, System.Security.Claims.ClaimsPrincipal principal, AuthenticationProperties? properties) { SignedIn = true; return Task.CompletedTask; }
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) { SignedOut = true; return Task.CompletedTask; }
    }
}
