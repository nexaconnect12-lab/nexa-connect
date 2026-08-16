using System.Net.Http.Headers;
using NexaConnect.Infrastructure.Authorization;
using NexaConnect.Services.Customer.Application.Tenant;

namespace NexaConnect.Services.Customer.Infrastructure;

public sealed class HttpCustomerTenantAuthorizer(
    HttpClient directory,
    ProductAuthorizationClient authorization,
    ILogger<HttpCustomerTenantAuthorizer> logger) : ICustomerTenantAuthorizer
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
        HttpResponseMessage response;
        try
        {
            response = await directory.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception,
                "Customer organization access dependency failed for organization {OrganizationId} and permission {Permission}.",
                organizationId, permission);
            throw;
        }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Customer organization access lookup failed for organization {OrganizationId}, permission {Permission}, and status {StatusCode}.",
                    organizationId, permission, (int)response.StatusCode);
                return false;
            }
            return await authorization.IsGrantedAsync(organizationId, null, null, permission,
                authorizationHeader, cancellationToken);
        }
    }
}
