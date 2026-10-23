using System.Net.Http.Headers;
using System.Text.Json;
using NexaConnect.Contracts.Platform;
using NexaConnect.Contracts.RestaurantQr;
using NexaConnect.CustomerBff.Application.Qr;

namespace NexaConnect.CustomerBff.Infrastructure.Qr;

public sealed class HttpQrGatewayPort(HttpClient restaurant, IHttpClientFactory clients) : IQrGatewayPort
{
    public async Task<CurrentPlatformAccessResponse?> AccessAsync(string bearer, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/platform-directory/v1/me/access");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var response = await clients.CreateClient("PlatformDirectory").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        return response.IsSuccessStatusCode ? (await Json(response.Content, 262144, deadline.Token)).Deserialize<CurrentPlatformAccessResponse>(JsonSerializerOptions.Web) : null;
    }
    public Task<QrGatewayReply> StaffAsync(TenantContext tenant, string bearer, Guid branch, QrAdminOperation operation, Guid? entry, object? body, CancellationToken ct)
    {
        string path = $"api/restaurant/v1/customer/organizations/{tenant.OrganizationId:D}/branches/{branch:D}/qr-entries";
        path += operation switch { QrAdminOperation.List or QrAdminOperation.Create => "", QrAdminOperation.Rotate => $"/{entry:D}/rotate", QrAdminOperation.Revoke => $"/{entry:D}/revoke", _ => throw new ArgumentException("Invalid QR operation.") };
        var request = new HttpRequestMessage(operation == QrAdminOperation.List ? HttpMethod.Get : HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (body is not null) request.Content = JsonContent.Create(body);
        return Send(request, ct);
    }
    public Task<QrGatewayReply> GuestAsync(string operation, string? session, QrEntryExchange? input, CancellationToken ct)
    {
        var request = operation switch
        {
            "start" => new HttpRequestMessage(HttpMethod.Post, "api/restaurant/v1/guest/qr/sessions") { Content = JsonContent.Create(input) },
            "context" => new HttpRequestMessage(HttpMethod.Get, "api/restaurant/v1/guest/qr/context"),
            "end" => new HttpRequestMessage(HttpMethod.Delete, "api/restaurant/v1/guest/qr/sessions"),
            _ => throw new ArgumentException("Invalid guest operation.")
        };
        if (session is not null) request.Headers.Add("X-Nexa-Guest-Session", session);
        // Never copy staff Authorization, tenant headers, cookies or arbitrary inbound headers.
        return Send(request, ct);
    }
    private async Task<QrGatewayReply> Send(HttpRequestMessage request, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using (request)
        using (var response = await restaurant.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token))
        {
            if (!response.IsSuccessStatusCode) return new((int)response.StatusCode, null);
            return new((int)response.StatusCode, response.StatusCode == System.Net.HttpStatusCode.NoContent ? null : await Json(response.Content, 65536, deadline.Token));
        }
    }
    private static async Task<JsonElement> Json(HttpContent content, int maximum, CancellationToken ct)
    {
        if (content.Headers.ContentLength > maximum) throw new HttpRequestException("QR response exceeded its bound.");
        using var data = new MemoryStream(); using var stream = await content.ReadAsStreamAsync(ct); byte[] buffer = new byte[4096];
        while (true) { int read = await stream.ReadAsync(buffer, ct); if (read == 0) break; if (data.Length + read > maximum) throw new HttpRequestException("QR response exceeded its bound."); await data.WriteAsync(buffer.AsMemory(0, read), ct); }
        using var document = JsonDocument.Parse(data.ToArray()); return document.RootElement.Clone();
    }
}
