using System.Net.Http.Headers;
using System.Net.Http.Json;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Inventory.Application.Tenant;
using NexaConnect.Infrastructure.Authorization;

namespace NexaConnect.Services.Inventory.Infrastructure;

public sealed class HttpInventoryTenantAuthorizer(
    IHttpClientFactory clients,
    IServiceWorkloadTokenProvider tokens,
    ProductAuthorizationClient authorization) : IInventoryTenantAuthorizer
{
    public async Task<bool> HasBranchAccessAsync(Guid organizationId, Guid branchId, string permission, string authorizationHeader,
        CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty || branchId == Guid.Empty
            || !AuthenticationHeaderValue.TryParse(authorizationHeader, out AuthenticationHeaderValue? customerAuthorization))
            return false;

        using var accessRequest = new HttpRequestMessage(HttpMethod.Get,
            $"api/platform-directory/v1/organizations/{organizationId:D}/access");
        accessRequest.Headers.Authorization = customerAuthorization;
        using HttpResponseMessage accessResponse = await clients.CreateClient("InventoryPlatformDirectory")
            .SendAsync(accessRequest, cancellationToken);
        if (!accessResponse.IsSuccessStatusCode) return false;

        using var scopeRequest = new HttpRequestMessage(HttpMethod.Get,
            $"api/restaurant/v1/branches/{branchId:D}/authorization-scope");
        scopeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAsync(cancellationToken));
        using HttpResponseMessage scopeResponse = await clients.CreateClient("InventoryRestaurant")
            .SendAsync(scopeRequest, cancellationToken);
        if (!scopeResponse.IsSuccessStatusCode) return false;
        BranchScope? scope = await scopeResponse.Content.ReadFromJsonAsync<BranchScope>(cancellationToken: cancellationToken);
        return scope is not null && scope.OrganizationId == organizationId && scope.BranchId == branchId
            && await authorization.IsGrantedAsync(organizationId, scope.RestaurantId, branchId, permission,
                authorizationHeader, cancellationToken);
    }

    private sealed record BranchScope(Guid OrganizationId, Guid RestaurantId, Guid BranchId);
}
