using System.Net;
using System.Text.Json;
using NexaConnect.Contracts.RestaurantQr;
using NexaConnect.Services.Order.Application.Orders;
using NexaConnect.Services.Order.Application.Workflow;

namespace NexaConnect.Services.Order.Infrastructure.Clients;

public sealed class HttpQrMenuPort(IHttpClientFactory clients,ILogger<HttpQrMenuPort>? logger=null) : IQrMenuPort,IQrAcceptancePort,IQrMenuPhotoPort,IQrMenuOrganizationPort
{
    public async Task<QrMenuOrganization> OrganizationAsync(Guid org,Guid branch,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,$"api/catalog/v1/internal/organizations/{org:D}/branches/{branch:D}/menu-organization");
        var value=await Read<NexaConnect.Contracts.Catalog.BranchMenuOrganization>(clients.CreateClient("QrGuestCatalog"),request,65536,ct,false);
        if(value is null||value.OrganizationId!=org||value.BranchId!=branch||value.Version<0||value.Categories is null||value.Items is null
            ||value.Categories.Count>50||value.Items.Count>200||value.Categories.Any(x=>x is null||x.Id==Guid.Empty||string.IsNullOrWhiteSpace(x.Name)||x.Name.Length>80||x.Name.Any(char.IsControl))
            ||value.Items.Any(x=>x is null||x.ProductId==Guid.Empty||x.CategoryId==Guid.Empty)||value.Categories.Select(x=>x.Id).Distinct().Count()!=value.Categories.Count
            ||value.Categories.Select(c=>c.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=value.Categories.Count||value.Version==0&&(value.Categories.Count>0||value.Items.Count>0)
            ||value.Items.Select(x=>x.ProductId).Distinct().Count()!=value.Items.Count||value.Items.Any(x=>x.CategoryId is Guid id&&!value.Categories.Any(c=>c.Id==id)))throw new HttpRequestException("Invalid menu organization.");
        return new(value.Categories.Select(x=>new QrMenuCategory(x.Id,x.Name)).ToArray(),value.Items.Select(x=>new QrMenuPlacement(x.ProductId,x.CategoryId)).ToArray());
    }
    public async Task<byte[]?> PhotoAsync(Guid organization,Guid branch,Guid product,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,$"api/catalog/v1/internal/organizations/{organization:D}/branches/{branch:D}/products/{product:D}/photo");
        using var response=await clients.CreateClient("QrGuestCatalog").SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        if(response.StatusCode==HttpStatusCode.NotFound)return null;
        if(!response.IsSuccessStatusCode){logger?.LogWarning("QR menu photo Catalog boundary rejected {StatusCode}",(int)response.StatusCode);throw new HttpRequestException("Menu photo unavailable.");}
        if(response.Content.Headers.ContentType?.MediaType!="image/png"||response.Content.Headers.ContentLength>524288)throw new InvalidDataException();
        await response.Content.LoadIntoBufferAsync(524288,ct);return await response.Content.ReadAsByteArrayAsync(ct);
    }
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
