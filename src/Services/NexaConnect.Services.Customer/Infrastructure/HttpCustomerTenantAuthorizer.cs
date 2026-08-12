using System.Net.Http.Headers;
using NexaConnect.Infrastructure.Authorization;
using NexaConnect.Services.Customer.Application.Tenant;

namespace NexaConnect.Services.Customer.Infrastructure;

public sealed class HttpCustomerTenantAuthorizer(
    HttpClient directory,
    ProductAuthorizationClient authorization) : ICustomerTenantAuthorizer
{
    public async Task<bool> HasOrganizationAccessAsync(
        Guid organizationId, string permission, string authorizationHeader, CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty
            || !AuthenticationHeaderValue.TryParse(authorizationHeader, out AuthenticationHeaderValue? customerAuthorization))
            return false;

        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"api/platform-directory/v1/organizations/{organizationId:D}/access");
        request.Headers.Authorization = customerAuthorization;
        using HttpResponseMessage response = await directory.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode
            && await authorization.IsGrantedAsync(organizationId, null, null, permission,
                authorizationHeader, cancellationToken);
    }
}
