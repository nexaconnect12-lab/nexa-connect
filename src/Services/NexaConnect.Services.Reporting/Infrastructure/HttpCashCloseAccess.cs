using NexaConnect.Services.Reporting.Application;

namespace NexaConnect.Services.Reporting.Infrastructure;

public sealed class HttpCashCloseAccess(HttpClient client) : ICashCloseAccess
{
    public async Task<bool> CanReadAsync(Guid organization, Guid branch, Guid store, string authorization, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/pos/v1/cash-reviews/access?organizationId={organization:D}&branchId={branch:D}&storeId={store:D}");
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        using var response = await client.SendAsync(request, ct);
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound) return false;
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Access>(cancellationToken: ct))?.CanRead == true;
    }
    private sealed record Access(bool CanRead);
}
