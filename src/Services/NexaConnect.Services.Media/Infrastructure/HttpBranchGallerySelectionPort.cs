using NexaConnect.Contracts.Media;
using NexaConnect.Services.Media.Application;

namespace NexaConnect.Services.Media.Infrastructure;

public sealed class HttpBranchGallerySelectionPort(HttpClient client,ILogger<HttpBranchGallerySelectionPort> logger):IBranchGallerySelectionPort
{
    public async Task<BranchGallery> ReadAsync(Guid org,Guid branch,string bearer,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,$"api/restaurant/v1/customer/organizations/{org:D}/branches/{branch:D}/screensaver-gallery");
        request.Headers.TryAddWithoutValidation("Authorization",bearer);
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        if(!response.IsSuccessStatusCode)logger.LogWarning("Gallery selection boundary rejected {StatusCode}",(int)response.StatusCode);
        if(response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)throw new UnauthorizedAccessException();
        if(response.StatusCode==System.Net.HttpStatusCode.NotFound)throw new KeyNotFoundException();
        response.EnsureSuccessStatusCode();await response.Content.LoadIntoBufferAsync(16384,ct);
        return await response.Content.ReadFromJsonAsync<BranchGallery>(ct)??throw new InvalidDataException();
    }
}
