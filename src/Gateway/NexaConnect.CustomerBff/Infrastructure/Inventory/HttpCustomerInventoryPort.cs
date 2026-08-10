using System.Net.Http.Headers;
using NexaConnect.Contracts.Platform;
using NexaConnect.CustomerBff.Application.Inventory;

namespace NexaConnect.CustomerBff.Infrastructure.Inventory;

public sealed class HttpCustomerInventoryPort(HttpClient client) : ICustomerInventoryPort
{
    public Task<HttpResponseMessage> GetStockAsync(TenantContext tenant, Guid branchId, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/inventory/v1/branches/{branchId:D}/stock");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add(TenantContextHeaders.OrganizationId, tenant.OrganizationId.ToString("D"));
        request.Headers.Add(TenantContextHeaders.ApplicationCode, tenant.ApplicationCode);
        request.Headers.Add(TenantContextHeaders.PortalRequest, "customer");
        return client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}
