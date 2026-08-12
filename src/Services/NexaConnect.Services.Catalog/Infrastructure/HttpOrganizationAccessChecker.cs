using System.Net.Http.Headers;
using NexaConnect.Services.Catalog.Application.Tenant;
using NexaConnect.Infrastructure.Authorization;

namespace NexaConnect.Services.Catalog.Infrastructure;

public sealed class HttpOrganizationAccessChecker(
    HttpClient client,
    IRestaurantBranchScopeReader branchScopeReader,
    ProductAuthorizationClient authorization) : ICatalogTenantAuthorizer
{
    public async Task<bool> HasAccessAsync(Guid organizationId, string permission, string authorizationHeader, CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty || string.IsNullOrWhiteSpace(authorizationHeader)) return false;
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/platform-directory/v1/organizations/{organizationId:D}/access");
        if (!AuthenticationHeaderValue.TryParse(authorizationHeader, out AuthenticationHeaderValue? customerAuthorization)) return false;
        request.Headers.Authorization = customerAuthorization;
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode
            && await authorization.IsGrantedAsync(organizationId, null, null, permission, authorizationHeader, cancellationToken);
    }

    public async Task<bool> HasBranchAccessAsync(Guid organizationId, Guid branchId, string permission, string authorizationHeader, CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty || string.IsNullOrWhiteSpace(authorizationHeader)) return false;
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/platform-directory/v1/organizations/{organizationId:D}/access");
        if (!AuthenticationHeaderValue.TryParse(authorizationHeader, out AuthenticationHeaderValue? customerAuthorization)) return false;
        request.Headers.Authorization = customerAuthorization;
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return false;
        RestaurantBranchScope? scope = await branchScopeReader.GetAsync(branchId, cancellationToken);
        return scope is not null && scope.OrganizationId == organizationId && scope.BranchId == branchId
            && await authorization.IsGrantedAsync(organizationId, scope.RestaurantId, branchId, permission,
                authorizationHeader, cancellationToken);
    }
}
