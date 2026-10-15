using System.Net;
using System.Net.Http.Json;
using NexaConnect.Services.Order.Application.ManualTenders;

namespace NexaConnect.Services.Order.Infrastructure.Clients;

public sealed class HttpManualTenderAdmission(HttpClient client, ILogger<HttpManualTenderAdmission> logger) : IManualTenderAdmission
{
    public async Task<DateTimeOffset> AdmitAsync(ManualTenderAdmissionRequest request, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync("api/pos/v1/internal/manual-tender-admission", request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        { logger.LogWarning("Manual tender terminal admission rejected"); throw new ManualTenderAdmissionConflictException(); }
        if (!response.IsSuccessStatusCode)
        { logger.LogWarning("Manual tender terminal admission dependency failed; status {Status}", (int)response.StatusCode); }
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AdmissionResponse>(cancellationToken: cancellationToken);
        return result?.AdmittedAtUtc ?? throw new HttpRequestException("POS admission returned no proof.");
    }
    private sealed record AdmissionResponse(DateTimeOffset AdmittedAtUtc);
}
