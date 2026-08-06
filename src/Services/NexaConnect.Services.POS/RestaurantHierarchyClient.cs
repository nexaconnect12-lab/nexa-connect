using System.Net.Http.Headers;
using System.Net.Http.Json;

public sealed class RestaurantHierarchyClient(
    HttpClient client,
    PosWorkloadTokenProvider tokenProvider,
    IConfiguration configuration)
{
    public async Task<RestaurantAuthorizationScope> GetScopeAsync(
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

public sealed record RestaurantAuthorizationScope(Guid OrganizationId, Guid RestaurantId, Guid BranchId);
