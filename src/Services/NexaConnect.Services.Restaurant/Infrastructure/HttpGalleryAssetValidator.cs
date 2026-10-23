using NexaConnect.Services.Restaurant.Application.Configuration;

namespace NexaConnect.Services.Restaurant.Infrastructure;

public sealed class HttpGalleryAssetValidator(HttpClient client, ILogger<HttpGalleryAssetValidator> logger) : IGalleryAssetValidator
{
    public async Task<bool> IsReadyAsync(Guid org, Guid asset, string bearer, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/media/v1/customer/organizations/{org:D}/gallery-assets/{asset:D}");
        request.Headers.TryAddWithoutValidation("Authorization",bearer);
        using var response = await client.SendAsync(request,ct);
        if (!response.IsSuccessStatusCode) logger.LogWarning("Gallery asset validation rejected {StatusCode}",(int)response.StatusCode);
        if ((int)response.StatusCode >= 500) throw new HttpRequestException("Media unavailable.");
        return response.IsSuccessStatusCode;
    }
}
