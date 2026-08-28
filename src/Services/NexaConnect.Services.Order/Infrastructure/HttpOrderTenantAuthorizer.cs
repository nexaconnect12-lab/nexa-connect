using System.Net.Http.Headers;
using System.Net.Http.Json;
using NexaConnect.Services.Order.Application.Tenant;
using NexaConnect.Infrastructure.Authorization;

namespace NexaConnect.Services.Order.Infrastructure;

public sealed class HttpOrderTenantAuthorizer(
    IHttpClientFactory clients,
    OrderWorkloadTokenProvider tokens,
    ProductAuthorizationClient authorization) : IOrderTenantAuthorizer
{
    public async Task<bool> HasBranchAccessAsync(Guid organizationId, Guid branchId, string permission, string authorizationHeader, CancellationToken cancellationToken)
        =>await GetBranchDecisionAsync(organizationId,branchId,permission,authorizationHeader,cancellationToken) is not null;

    public async Task<Guid?> GetBranchDecisionAsync(Guid organizationId, Guid branchId, string permission, string authorizationHeader, CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty || branchId == Guid.Empty || string.IsNullOrWhiteSpace(authorizationHeader)
            || !AuthenticationHeaderValue.TryParse(authorizationHeader, out AuthenticationHeaderValue? customerAuthorization)) return null;
        HttpClient directory = clients.CreateClient("OrderPlatformDirectory");
        using var membership = new HttpRequestMessage(HttpMethod.Get, $"api/platform-directory/v1/organizations/{organizationId:D}/access");
        membership.Headers.Authorization = customerAuthorization;
        using HttpResponseMessage membershipResponse = await directory.SendAsync(membership, cancellationToken);
        if (!membershipResponse.IsSuccessStatusCode) return null;

        HttpClient restaurant = clients.CreateClient("OrderRestaurant");
        using var scopeRequest = new HttpRequestMessage(HttpMethod.Get, $"api/restaurant/v1/branches/{branchId:D}/authorization-scope");
        scopeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAsync(cancellationToken));
        using HttpResponseMessage scopeResponse = await restaurant.SendAsync(scopeRequest, cancellationToken);
        if (!scopeResponse.IsSuccessStatusCode) return null;
        OrderBranchScope? scope = await scopeResponse.Content.ReadFromJsonAsync<OrderBranchScope>(cancellationToken: cancellationToken);
        return scope is not null && scope.OrganizationId == organizationId && scope.BranchId == branchId
            ?(await authorization.DecideAsync(organizationId,scope.RestaurantId,branchId,permission,authorizationHeader,cancellationToken)) is {Granted:true} decision?decision.DecisionId:null
            :null;
    }

    private sealed record OrderBranchScope(Guid OrganizationId, Guid RestaurantId, Guid BranchId);
}
