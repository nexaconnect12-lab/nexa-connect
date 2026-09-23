using System.Net.Http.Headers;
using System.Net.Http.Json;
using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Infrastructure.Identity;

namespace NexaConnect.Services.POS.Infrastructure.Restaurant;

public sealed class RestaurantHierarchyClient(
    HttpClient client,
    PosWorkloadTokenProvider tokenProvider,
    IConfiguration configuration) : IRestaurantScopeReader
{
    public async Task<RestaurantAuthorizationScope> GetAsync(
        Guid branchId,
        CancellationToken cancellationToken)
    {
        var endpoint = new Uri(configuration["Services:Restaurant"]
            ?? throw new InvalidOperationException("Services:Restaurant is required."));
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint,
            $"api/restaurant/v1/branches/{branchId}/authorization-scope"));
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await tokenProvider.GetAsync(cancellationToken));
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        RestaurantAuthorizationScope? scope = await response.Content.ReadFromJsonAsync<RestaurantAuthorizationScope>(cancellationToken: cancellationToken);
        return scope ?? throw new InvalidOperationException("Restaurant hierarchy response was empty.");
    }
}
