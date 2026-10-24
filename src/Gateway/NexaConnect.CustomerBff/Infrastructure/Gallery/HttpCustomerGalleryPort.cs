using System.Net.Http.Headers;
using NexaConnect.Contracts.Media;
using NexaConnect.Contracts.Platform;
using NexaConnect.CustomerBff.Application.Gallery;

namespace NexaConnect.CustomerBff.Infrastructure.Gallery;

public sealed class HttpCustomerGalleryPort(IHttpClientFactory clients):ICustomerGalleryPort
{
    public async Task<CurrentPlatformAccessResponse?> AccessAsync(string token,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,"api/platform-directory/v1/me/access");request.Headers.Authorization=new("Bearer",token);
        using var response=await clients.CreateClient("PlatformDirectory").SendAsync(request,ct);
        return response.IsSuccessStatusCode?await response.Content.ReadFromJsonAsync<CurrentPlatformAccessResponse>(ct):null;
    }
    public async Task<HttpResponseMessage> SendAsync(TenantContext tenant,Guid branch,UpdateBranchGallery? update,string token,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(update is null?HttpMethod.Get:HttpMethod.Put,$"api/restaurant/v1/customer/organizations/{tenant.OrganizationId:D}/branches/{branch:D}/screensaver-gallery");
        request.Headers.Authorization=new("Bearer",token);
        request.Headers.Add(TenantContextHeaders.OrganizationId,tenant.OrganizationId.ToString("D"));request.Headers.Add(TenantContextHeaders.ApplicationCode,tenant.ApplicationCode);
        if(update is not null)request.Content=JsonContent.Create(update);
        return await clients.CreateClient("Restaurant").SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
    }
}
