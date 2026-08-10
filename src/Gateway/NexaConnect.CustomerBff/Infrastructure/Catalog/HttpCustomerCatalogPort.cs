using System.Net.Http.Headers;
using NexaConnect.Contracts.Platform;
using NexaConnect.CustomerBff.Application.Catalog;

namespace NexaConnect.CustomerBff.Infrastructure.Catalog;

public sealed class HttpCustomerCatalogPort(HttpClient client) : ICustomerCatalogPort
{
    public Task<HttpResponseMessage> GetMenuAsync(TenantContext tenant, Guid branchId, string accessToken, CancellationToken cancellationToken)
    {
        if (tenant.OrganizationId == Guid.Empty || string.IsNullOrWhiteSpace(tenant.ApplicationCode))
            throw new ArgumentException("A valid tenant context is required.", nameof(tenant));
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new ArgumentException("A customer access token is required.", nameof(accessToken));

        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/catalog/v1/branches/{branchId:D}/menu-items");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add(TenantContextHeaders.OrganizationId, tenant.OrganizationId.ToString("D"));
        request.Headers.Add(TenantContextHeaders.ApplicationCode, tenant.ApplicationCode);
        request.Headers.Add(TenantContextHeaders.PortalRequest, "customer");
        return client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}
