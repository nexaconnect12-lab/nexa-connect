using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace NexaConnect.Services.Order.Infrastructure;

public sealed class OrderWorkloadTokenProvider(HttpClient client, IConfiguration configuration, IMemoryCache cache)
{
    public Task<string> GetAsync(CancellationToken cancellationToken) => cache.GetOrCreateAsync("order-workload-token", async entry =>
    {
        string authority = configuration["WorkloadIdentity:Authority"] ?? throw new InvalidOperationException("WorkloadIdentity:Authority is required.");
        string clientId = configuration["WorkloadIdentity:ClientId"] ?? throw new InvalidOperationException("WorkloadIdentity:ClientId is required.");
        string secret = configuration["WorkloadIdentity:ClientSecret"] ?? throw new InvalidOperationException("WorkloadIdentity:ClientSecret is required.");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{authority.TrimEnd('/')}/protocol/openid-connect/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["client_secret"] = secret
            })
        };
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        string token = document.RootElement.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("Keycloak returned no access token.");
        entry.AbsoluteExpiration = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, document.RootElement.GetProperty("expires_in").GetInt32() - 30));
        return token;
    })!;
}
