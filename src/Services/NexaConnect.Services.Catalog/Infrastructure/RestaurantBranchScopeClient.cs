using System.Net.Http.Headers;
using System.Net.Http.Json;
using NexaConnect.Services.Catalog.Application.Tenant;

namespace NexaConnect.Services.Catalog.Infrastructure;

public sealed class RestaurantBranchScopeClient(
    HttpClient client,
    CatalogWorkloadTokenProvider tokenProvider,
    IConfiguration configuration) : IRestaurantBranchScopeReader
{
    public async Task<RestaurantBranchScope?> GetAsync(Guid branchId, CancellationToken cancellationToken)
    {
        client.BaseAddress = new Uri(configuration["Services:Restaurant"]
            ?? throw new InvalidOperationException("Services:Restaurant is required."));
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"api/restaurant/v1/branches/{branchId:D}/authorization-scope");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await tokenProvider.GetAsync(cancellationToken));
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        RestaurantScopeResponse? scope = await response.Content.ReadFromJsonAsync<RestaurantScopeResponse>(cancellationToken);
        return scope is null ? null : new RestaurantBranchScope(scope.OrganizationId, scope.RestaurantId, scope.BranchId);
    }

    private sealed record RestaurantScopeResponse(Guid OrganizationId, Guid RestaurantId, Guid BranchId);
}
