using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace NexaConnect.Infrastructure.Authentication;

public interface IServiceWorkloadTokenProvider
{
    Task<string> GetAsync(CancellationToken cancellationToken);
}

public sealed class ServiceWorkloadTokenProvider(
    HttpClient client,
    IConfiguration configuration,
    IMemoryCache cache) : IServiceWorkloadTokenProvider
{
    public Task<string> GetAsync(CancellationToken cancellationToken)
    {
        string clientId = configuration["WorkloadIdentity:ClientId"]
            ?? throw new InvalidOperationException("WorkloadIdentity:ClientId is required.");
        return cache.GetOrCreateAsync($"workload-token:{clientId}", async entry =>
        {
            string authority = configuration["WorkloadIdentity:Authority"]
                ?? throw new InvalidOperationException("WorkloadIdentity:Authority is required.");
            string clientSecret = configuration["WorkloadIdentity:ClientSecret"]
                ?? throw new InvalidOperationException("WorkloadIdentity:ClientSecret is required.");
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"{authority.TrimEnd('/')}/protocol/openid-connect/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret
                })
            };
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            string token = document.RootElement.GetProperty("access_token").GetString()
                ?? throw new InvalidOperationException("Identity provider returned no access token.");
            int lifetime = document.RootElement.GetProperty("expires_in").GetInt32();
            entry.AbsoluteExpiration = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, lifetime - 30));
            return token;
        })!;
    }
}
