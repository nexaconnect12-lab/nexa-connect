using System.Net.Http.Headers;
using NexaConnect.Contracts.Platform;
using NexaConnect.CustomerBff.Application.Reporting;

namespace NexaConnect.CustomerBff.Infrastructure.Reporting;

public sealed class HttpCustomerCashClosePort(HttpClient client, IHttpClientFactory clients) : ICustomerCashClosePort
{
    public async Task<CurrentPlatformAccessResponse?> GetAccessAsync(string token, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, "api/platform-directory/v1/me/access");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await clients.CreateClient("PlatformDirectory").SendAsync(message, ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<CurrentPlatformAccessResponse>(cancellationToken: ct) : null;
    }
    public async Task<HttpResponseMessage> ReadAsync(TenantContext tenant, string token, CashCloseRequest request, CancellationToken ct)
    {
        string path = $"api/reporting/v1/customer/organizations/{tenant.OrganizationId:D}/reports/cash-close?branchId={request.BranchId:D}&storeId={request.StoreId:D}&fromUtc={Uri.EscapeDataString(request.FromUtc.ToUniversalTime().ToString("O"))}&toUtc={Uri.EscapeDataString(request.ToUtc.ToUniversalTime().ToString("O"))}&limit={request.Limit}&cursor={Uri.EscapeDataString(request.Cursor ?? "")}";
        using var message = new HttpRequestMessage(HttpMethod.Get, path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Headers.Add(TenantContextHeaders.OrganizationId, tenant.OrganizationId.ToString("D"));
        message.Headers.Add(TenantContextHeaders.ApplicationCode, tenant.ApplicationCode);
        message.Headers.Add(TenantContextHeaders.PortalRequest, "customer");
        return await client.SendAsync(message, ct);
    }
}
