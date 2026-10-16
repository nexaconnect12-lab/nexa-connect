using System.Net.Http.Headers;
using NexaConnect.Contracts.Platform;
using NexaConnect.CustomerBff.Application.Reporting;

namespace NexaConnect.CustomerBff.Infrastructure.Reporting;

public sealed class HttpCustomerCashCorrectionReportsPort(HttpClient client, IHttpClientFactory clients) : ICustomerCashCorrectionReportsPort
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

    public async Task<HttpResponseMessage> ReadAsync(TenantContext tenant, string token, CashCorrectionReportsRequest input, CancellationToken cancellationToken)
    {
        string path = $"api/reporting/v1/customer/organizations/{tenant.OrganizationId:D}/reports/cash-corrections"
            + $"?branchId={input.BranchId:D}&fromUtc={Uri.EscapeDataString(input.FromUtc.ToUniversalTime().ToString("O"))}"
            + $"&toUtc={Uri.EscapeDataString(input.ToUtc.ToUniversalTime().ToString("O"))}";
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
