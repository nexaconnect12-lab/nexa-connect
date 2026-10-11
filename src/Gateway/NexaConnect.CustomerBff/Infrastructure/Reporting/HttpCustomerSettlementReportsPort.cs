using System.Net.Http.Headers;
using NexaConnect.Contracts.Platform;
using NexaConnect.CustomerBff.Application.Reporting;

namespace NexaConnect.CustomerBff.Infrastructure.Reporting;

public sealed class HttpCustomerSettlementReportsPort(HttpClient client, IHttpClientFactory clients) : ICustomerSettlementReportsPort
{
    public async Task<CurrentPlatformAccessResponse?> GetAccessAsync(string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/platform-directory/v1/me/access");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await clients.CreateClient("PlatformDirectory").SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<CurrentPlatformAccessResponse>(cancellationToken: cancellationToken)
            : null;
    }

    public async Task<HttpResponseMessage> ReadAsync(TenantContext tenant, string token, SettlementReportsRequest input, CancellationToken cancellationToken)
    {
        string path = $"api/reporting/v1/customer/organizations/{tenant.OrganizationId:D}/reports/day-settlements"
            + $"?branchId={input.BranchId:D}&fromDate={Uri.EscapeDataString(input.FromDate.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture))}"
            + $"&toDate={Uri.EscapeDataString(input.ToDate.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture))}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(TenantContextHeaders.OrganizationId, tenant.OrganizationId.ToString("D"));
        request.Headers.Add(TenantContextHeaders.ApplicationCode, tenant.ApplicationCode);
        request.Headers.Add(TenantContextHeaders.PortalRequest, "customer");
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        try { await response.Content.LoadIntoBufferAsync(2 * 1024 * 1024, cancellationToken); return response; }
        catch { response.Dispose(); throw; }
    }
}
