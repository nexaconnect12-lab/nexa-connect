using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace NexaConnect.Infrastructure.Authentication;

public interface IOutboundAccessTokenProvider
{
    Task<string?> GetAsync(CancellationToken cancellationToken);
}

public sealed class KeycloakClientCredentialsTokenProvider(IConfiguration configuration, IHttpClientFactory clients) : IOutboundAccessTokenProvider
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private string? token;
    private DateTimeOffset expiresAt;

    public async Task<string?> GetAsync(CancellationToken cancellationToken)
    {
        var staticToken = configuration["Authentication:OutboundToken"];
        if (!string.IsNullOrWhiteSpace(staticToken)) return staticToken;
        if (token is not null && expiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return token;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (token is not null && expiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return token;
            var endpoint = configuration["Authentication:TokenEndpoint"] ?? throw new InvalidOperationException("Authentication:TokenEndpoint is required for workload credentials.");
            var clientId = configuration["Authentication:ClientId"] ?? throw new InvalidOperationException("Authentication:ClientId is required for workload credentials.");
            var clientSecret = configuration["Authentication:ClientSecret"] ?? throw new InvalidOperationException("Authentication:ClientSecret is required for workload credentials.");
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["client_secret"] = clientSecret }) };
            using var response = await clients.CreateClient("keycloak-token").SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            token = document.RootElement.GetProperty("access_token").GetString();
            expiresAt = DateTimeOffset.UtcNow.AddSeconds(document.RootElement.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 60);
            return token;
        }
        finally { gate.Release(); }
    }
}

public sealed class OutboundTokenHandler(IOutboundAccessTokenProvider provider) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await provider.GetAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken);
    }
}
