using System.Net;
using System.Net.Http.Headers;
using NexaConnect.Services.POS.Application.ManualRefunds;
using NexaConnect.Services.POS.Application.Shifts;

namespace NexaConnect.Services.POS.Infrastructure.ManualRefunds;

public sealed class HttpManualRefundMembership(HttpClient client, IConfiguration configuration, IHostEnvironment environment,
    ILogger<HttpManualRefundMembership> logger) : IManualRefundMembership
{
    public async Task<bool> HasAccessAsync(Guid organizationId, PosUserContext user, CancellationToken ct)
    {
        var address = ManualRefundDependencyAddress.Resolve(configuration, environment, "PlatformDirectory",
            $"api/platform-directory/v1/organizations/{organizationId:D}/access");
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound) return false;
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Manual refund membership dependency {StatusCode}", (int)response.StatusCode);
            throw new HttpRequestException("Organization membership is unavailable.");
        }
        return true;
    }
}
