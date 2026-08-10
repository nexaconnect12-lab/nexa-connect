using System.Net.Http.Headers;
using NexaConnect.Services.Catalog.Application.Tenant;

namespace NexaConnect.Services.Catalog.Infrastructure;

public sealed class HttpOrganizationAccessChecker(HttpClient client, IRestaurantBranchScopeReader branchScopeReader) : ICatalogTenantAuthorizer
{
    public async Task<bool> HasAccessAsync(Guid organizationId, string authorizationHeader, CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty || string.IsNullOrWhiteSpace(authorizationHeader)) return false;
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/platform-directory/v1/organizations/{organizationId:D}/access");
        if (!AuthenticationHeaderValue.TryParse(authorizationHeader, out AuthenticationHeaderValue? authorization)) return false;
        request.Headers.Authorization = authorization;
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> HasBranchAccessAsync(Guid organizationId, Guid branchId, string authorizationHeader, CancellationToken cancellationToken)
    {
        if (!await HasAccessAsync(organizationId, authorizationHeader, cancellationToken)) return false;
        RestaurantBranchScope? scope = await branchScopeReader.GetAsync(branchId, cancellationToken);
        return scope is not null && scope.OrganizationId == organizationId && scope.BranchId == branchId;
    }
}
