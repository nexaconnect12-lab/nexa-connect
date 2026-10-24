using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.Reporting.Application;

namespace NexaConnect.Services.Reporting.Infrastructure;

public sealed class HttpSettlementSource(HttpClient client) : ISettlementSource
{
    public async Task<SettlementManifest> ReadAsync(SettlementReportRange range, string authorization, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"api/pos/v1/customer/settlement-manifest?organizationId={range.OrganizationId:D}&branchId={range.BranchId:D}"
            + $"&fromDate={Uri.EscapeDataString(range.FromDate.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture))}&toDate={Uri.EscapeDataString(range.ToDate.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture))}");
        request.Headers.Authorization = AuthenticationHeaderValue.Parse(authorization);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized) throw new UnauthorizedAccessException();
        if (response.StatusCode == HttpStatusCode.BadRequest) throw new ArgumentException("Settlement source range exceeds its supported inventory.");
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(2 * 1024 * 1024, ct);
        return await response.Content.ReadFromJsonAsync<SettlementManifest>(cancellationToken: ct) ?? throw new JsonException();
    }
}
