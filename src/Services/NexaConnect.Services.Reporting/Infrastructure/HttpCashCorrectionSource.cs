using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.Reporting.Application;

namespace NexaConnect.Services.Reporting.Infrastructure;

public sealed class HttpCashCorrectionSource(HttpClient client) : ICashCorrectionSource
{
    public async Task<CashCorrectionManifest> ReadAsync(ReportingRange range, string authorization, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"api/pos/v1/customer/cash-correction-manifest?organizationId={range.OrganizationId:D}&branchId={range.BranchId:D}"
            + $"&fromUtc={Uri.EscapeDataString(range.FromUtc.ToString("O"))}&toUtc={Uri.EscapeDataString(range.ToUtc.ToString("O"))}");
        request.Headers.Authorization = AuthenticationHeaderValue.Parse(authorization);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized) throw new UnauthorizedAccessException();
        if (response.StatusCode == HttpStatusCode.BadRequest) throw new ArgumentException("Correction source range exceeds its supported inventory.");
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(2 * 1024 * 1024, ct);
        return await response.Content.ReadFromJsonAsync<CashCorrectionManifest>(cancellationToken: ct) ?? throw new JsonException();
    }
}
