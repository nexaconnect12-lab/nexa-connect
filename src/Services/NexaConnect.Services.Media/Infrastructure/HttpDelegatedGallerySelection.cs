using System.Net;
using System.Net.Http.Headers;
using NexaConnect.Contracts.Media;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Media.Application;

namespace NexaConnect.Services.Media.Infrastructure;

public sealed class HttpDelegatedGallerySelection(HttpClient client,IServiceWorkloadTokenProvider tokens,
    ILogger<HttpDelegatedGallerySelection> logger):IDelegatedGallerySelection
{
    public async Task<BranchGallery> ReadAsync(Guid org,Guid branch,string subject,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,$"api/restaurant/v1/internal/organizations/{org:D}/branches/{branch:D}/screensaver-gallery")
        {Content=JsonContent.Create(new GalleryViewer(subject))};
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",await tokens.GetAsync(ct));
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        if(!response.IsSuccessStatusCode)logger.LogWarning("Gallery reader selection rejected {StatusCode}",(int)response.StatusCode);
        if(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)throw new UnauthorizedAccessException();
        if(response.StatusCode==HttpStatusCode.NotFound)throw new KeyNotFoundException();
        response.EnsureSuccessStatusCode();await response.Content.LoadIntoBufferAsync(16384,ct);
        return await response.Content.ReadFromJsonAsync<BranchGallery>(ct)??throw new InvalidDataException();
    }
}
