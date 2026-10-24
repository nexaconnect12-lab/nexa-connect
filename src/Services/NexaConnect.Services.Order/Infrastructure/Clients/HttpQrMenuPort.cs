using System.Net;
using System.Text.Json;
using NexaConnect.Contracts.RestaurantQr;
using NexaConnect.Services.Order.Application.Orders;
using NexaConnect.Services.Order.Application.Workflow;

namespace NexaConnect.Services.Order.Infrastructure.Clients;

public sealed class HttpQrMenuPort(IHttpClientFactory clients) : IQrMenuPort,IQrAcceptancePort
{
    public async Task<QrAcceptanceContext?> ContextAsync(Domain.Qr.GuestRequestScope scope,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,$"api/restaurant/v1/internal/organizations/{scope.OrganizationId:D}/branches/{scope.BranchId:D}/qr-visitors/{scope.VisitorId:D}/entries/{scope.EntryId:D}");
        return await Read<QrAcceptanceContext>(clients.CreateClient("QrAcceptanceContext"),request,4096,ct,true);
    }
    public async Task<QrGuestContext?> ContextAsync(string capability, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/restaurant/v1/guest/qr/context");
        request.Headers.Add("X-Nexa-Guest-Session", capability);
        // This client has no workload or staff Authorization handler.
        return await Read<QrGuestContext>(clients.CreateClient("QrGuestContext"), request, 4096, ct, true);
    }
    public async Task<IReadOnlyList<CatalogMenuItem>> MenuAsync(Guid organization, Guid branch, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"api/catalog/v1/internal/organizations/{organization:D}/branches/{branch:D}/guest-menu");
        var menu = await Read<QrCatalogMenu>(clients.CreateClient("QrGuestCatalog"), request, 524288, ct, false)
            ?? throw new HttpRequestException("Menu unavailable.");
        if (menu.Items is null || menu.Items.Any(i => i is null)) throw new JsonException("Invalid menu shape.");
        return menu.Items.Select(i => new CatalogMenuItem(i.ProductId, i.Name, i.UnitPrice, i.Currency, i.Available, i.PreparationStation)).ToArray();
    }
    private static async Task<T?> Read<T>(HttpClient client, HttpRequestMessage request, int maximum, CancellationToken ct, bool allowUnauthorized)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (allowUnauthorized && response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound) return default;
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("QR dependency unavailable.");
        if (response.Content.Headers.ContentLength > maximum) throw new HttpRequestException("QR dependency exceeded bounds.");
        using var stream = await response.Content.ReadAsStreamAsync(deadline.Token); using var data = new MemoryStream();
        byte[] buffer = new byte[4096];
        while (true)
        {
            int read = await stream.ReadAsync(buffer, deadline.Token); if (read == 0) break;
            if (data.Length + read > maximum) throw new HttpRequestException("QR dependency exceeded bounds.");
            await data.WriteAsync(buffer.AsMemory(0, read), deadline.Token);
        }
        return JsonSerializer.Deserialize<T>(data.ToArray(), JsonSerializerOptions.Web);
    }
}
