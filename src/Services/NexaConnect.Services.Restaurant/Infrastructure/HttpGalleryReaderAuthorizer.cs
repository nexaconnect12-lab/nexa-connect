using NexaConnect.Contracts.Media;
using NexaConnect.Services.Restaurant.Application.Configuration;

namespace NexaConnect.Services.Restaurant.Infrastructure;

public sealed class HttpGalleryReaderAuthorizer(IHttpClientFactory factory,ILogger<HttpGalleryReaderAuthorizer> logger):IGalleryReaderAuthorizer
{
    public async Task<bool> GrantedAsync(Guid org,Guid restaurant,Guid branch,string subject,string bearer,CancellationToken ct)
    {
        if(!await Check("GalleryDirectory","api/platform-directory/v1/internal/gallery-access",new GalleryMembershipQuery(org,subject),bearer,ct))return false;
        return await Check("GalleryAuthorization","api/authorization/v1/internal/gallery-access",new GalleryPermissionQuery(org,restaurant,branch,subject),bearer,ct);
    }
    private async Task<bool> Check<T>(string client,string path,T body,string bearer,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,path){Content=JsonContent.Create(body)};
        request.Headers.TryAddWithoutValidation("Authorization",bearer);
        using var response=await factory.CreateClient(client).SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        if(!response.IsSuccessStatusCode)logger.LogWarning("Gallery authority dependency rejected {StatusCode}",(int)response.StatusCode);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(1024,ct);
        return (await response.Content.ReadFromJsonAsync<GalleryAccess>(ct)??throw new InvalidDataException()).Granted;
    }
}
