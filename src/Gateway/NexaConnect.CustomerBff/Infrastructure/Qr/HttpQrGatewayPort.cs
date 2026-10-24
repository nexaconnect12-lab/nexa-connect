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
    public Task<QrGatewayReply> ShoppingAsync(string operation, string session, QrCart? cart, CancellationToken ct)
    {
        var request = operation switch
        {
            "menu" => new HttpRequestMessage(HttpMethod.Get, "api/order/v1/guest/qr/menu"),
            "quote" => new HttpRequestMessage(HttpMethod.Post, "api/order/v1/guest/qr/quote") { Content = JsonContent.Create(cart) },
            _ => throw new ArgumentException("Invalid guest operation.")
        };
        request.Headers.Add("X-Nexa-Guest-Session", session);
        return Send(request, ct, clients.CreateClient("QrGuestOrder"), 524288);
    }
    public Task<QrGatewayReply> RequestsAsync(string session,QrSubmission? input,CancellationToken ct)
    {
        var request=new HttpRequestMessage(input is null?HttpMethod.Get:HttpMethod.Post,"api/order/v1/guest/qr/requests");
        if(input is not null)request.Content=JsonContent.Create(input);
        request.Headers.Add("X-Nexa-Guest-Session",session);
        return Send(request,ct,clients.CreateClient("QrGuestOrder"),2097152);
    }
    public Task<QrGatewayReply> ReviewAsync(TenantContext tenant,string bearer,Guid branch,string state,Guid? id,QrRejection? input,CancellationToken ct)
    {
        if(state is not ("pending" or "price_review" or "accepted" or "rejected" or "expired"))throw new ArgumentException("Invalid review state.");
        string path=$"api/order/v1/customer/organizations/{tenant.OrganizationId:D}/branches/{branch:D}/qr-requests";
        path+=input is null?$"?state={state}":$"/{id:D}/reject";
        var request=new HttpRequestMessage(input is null?HttpMethod.Get:HttpMethod.Post,path);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",bearer);
        request.Headers.Add(TenantContextHeaders.OrganizationId,tenant.OrganizationId.ToString("D"));
        request.Headers.Add(TenantContextHeaders.ApplicationCode,tenant.ApplicationCode);
        if(input is not null)request.Content=JsonContent.Create(input);
        return Send(request,ct,clients.CreateClient("QrGuestOrder"),2097152);
    }
    private async Task<QrGatewayReply> Send(HttpRequestMessage request, CancellationToken ct, HttpClient? target = null, int maximum = 65536)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using (request)
        using (var response = await (target ?? restaurant).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token))
        {
            if (!response.IsSuccessStatusCode) return new((int)response.StatusCode, null);
            return new((int)response.StatusCode, response.StatusCode == System.Net.HttpStatusCode.NoContent ? null : await Json(response.Content, maximum, deadline.Token));
        }
    }
    public Task<QrGatewayReply> AcceptAsync(TenantContext tenant,string bearer,Guid branch,Guid id,QrAcceptance input,CancellationToken ct)
    {
        var request=new HttpRequestMessage(HttpMethod.Post,$"api/order/v1/customer/organizations/{tenant.OrganizationId:D}/branches/{branch:D}/qr-requests/{id:D}/accept"){Content=JsonContent.Create(input)};
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",bearer);request.Headers.Add(TenantContextHeaders.OrganizationId,tenant.OrganizationId.ToString("D"));request.Headers.Add(TenantContextHeaders.ApplicationCode,tenant.ApplicationCode);
        return Send(request,ct,clients.CreateClient("QrGuestOrder"),2097152);
    }
    public Task<QrGatewayReply> ConfirmAsync(string session,Guid id,QrPriceConfirmation input,CancellationToken ct)
    {
        var request=new HttpRequestMessage(HttpMethod.Post,$"api/order/v1/guest/qr/requests/{id:D}/confirm-price"){Content=JsonContent.Create(input)};request.Headers.Add("X-Nexa-Guest-Session",session);
        return Send(request,ct,clients.CreateClient("QrGuestOrder"),2097152);
    }
    private static async Task<JsonElement> Json(HttpContent content, int maximum, CancellationToken ct)
    {
        if (content.Headers.ContentLength > maximum) throw new HttpRequestException("QR response exceeded its bound.");
        using var data = new MemoryStream(); using var stream = await content.ReadAsStreamAsync(ct); byte[] buffer = new byte[4096];
        while (true) { int read = await stream.ReadAsync(buffer, ct); if (read == 0) break; if (data.Length + read > maximum) throw new HttpRequestException("QR response exceeded its bound."); await data.WriteAsync(buffer.AsMemory(0, read), ct); }
        using var document = JsonDocument.Parse(data.ToArray()); return document.RootElement.Clone();
    }
}
