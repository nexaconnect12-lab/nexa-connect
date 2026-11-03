using System.Net.Http.Headers;
using System.Net.Http.Json;
using NexaConnect.Contracts.Platform;
using NexaConnect.CustomerBff.Application.Media;

namespace NexaConnect.CustomerBff.Infrastructure.Media;

public sealed class HttpCustomerMediaLibraryPort(IHttpClientFactory clients):ICustomerMediaLibraryPort
{
    public async Task<CurrentPlatformAccessResponse?> AccessAsync(string token,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,"api/platform-directory/v1/me/access");request.Headers.Authorization=new("Bearer",token);
        using var response=await clients.CreateClient("MediaLibraryDirectory").SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        if(!response.IsSuccessStatusCode)return null;await response.Content.LoadIntoBufferAsync(262144,ct);
        return await response.Content.ReadFromJsonAsync<CurrentPlatformAccessResponse>(ct);
    }
    public Task<HttpResponseMessage> SendAsync(Guid organization,Guid? asset,string search,int page,bool readyOnly,string token,CancellationToken ct)
    {
        string path=$"api/media/v1/customer/organizations/{organization:D}/library"+(asset is {} id?$"/assets/{id:D}/thumbnail":$"?search={Uri.EscapeDataString(search)}&page={page}&readyOnly={readyOnly.ToString().ToLowerInvariant()}");
        return Send(path,token,ct);
    }
    private async Task<HttpResponseMessage> Send(string path,string token,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,path);request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        return await clients.CreateClient("MediaLibraryMedia").SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
    }
}
