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
        client.BaseAddress = new Uri(configuration["Services:Restaurant"]
            ?? throw new InvalidOperationException("Services:Restaurant is required."));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await tokenProvider.GetAsync(cancellationToken));
        RestaurantAuthorizationScope? scope = await client.GetFromJsonAsync<RestaurantAuthorizationScope>(
            $"api/restaurant/v1/branches/{branchId}/authorization-scope", cancellationToken);
        return scope ?? throw new InvalidOperationException("Restaurant hierarchy response was empty.");
    }
}
