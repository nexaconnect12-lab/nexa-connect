using System.Net.Http.Headers;
using System.Net.Http.Json;
using NexaConnect.Services.POS.Application.Shifts;

namespace NexaConnect.Services.POS.Infrastructure.Authorization;

public sealed class AuthorizationDecisionClient(
    IHttpClientFactory clients,
    IConfiguration configuration) : IAuthorizationDecisionClient
{
    public async Task<AuthorizationDecision> DecideAsync(
        PosUserContext user,
        RestaurantAuthorizationScope scope,
        string permission,
        CancellationToken cancellationToken)
    {
        using HttpClient client = clients.CreateClient("Authorization");
        client.BaseAddress = new Uri(configuration["Services:Authorization"]
            ?? throw new InvalidOperationException("Services:Authorization is required."));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "api/authorization/v1/decisions",
            new
            {
                scope.OrganizationId,
                RestaurantId = (Guid?)scope.RestaurantId,
                BranchId = (Guid?)scope.BranchId,
                Permission = permission,
                Amount = (decimal?)null,
                Currency = (string?)null
            },
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuthorizationDecision>(cancellationToken)
            ?? throw new InvalidOperationException("Authorization returned an empty decision.");
    }
}
