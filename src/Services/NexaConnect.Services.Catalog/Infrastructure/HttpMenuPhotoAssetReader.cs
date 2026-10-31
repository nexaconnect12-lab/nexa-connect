using NexaConnect.Services.Catalog.Application.MenuPhotos;
namespace NexaConnect.Services.Catalog.Infrastructure;
public sealed class HttpMenuPhotoAssetReader(HttpClient client,CatalogWorkloadTokenProvider tokens,ILogger<HttpMenuPhotoAssetReader> logger):IMenuPhotoAssetReader
{
    public async Task<byte[]?> ReadAsync(Guid org,Guid asset,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,$"api/media/v1/internal/organizations/{org:D}/catalog-menu-images/{asset:D}");
        request.Headers.Authorization=new("Bearer",await tokens.GetAsync(ct));
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        if(response.StatusCode==System.Net.HttpStatusCode.NotFound)return null;
        if(!response.IsSuccessStatusCode){logger.LogWarning("Menu photo Media boundary rejected {StatusCode}",(int)response.StatusCode);throw new HttpRequestException("Menu image unavailable.");}
        if(response.Content.Headers.ContentType?.MediaType!="image/png"||response.Content.Headers.ContentLength>524288)throw new InvalidDataException();
        await response.Content.LoadIntoBufferAsync(524288,ct);return await response.Content.ReadAsByteArrayAsync(ct);
    }
}
