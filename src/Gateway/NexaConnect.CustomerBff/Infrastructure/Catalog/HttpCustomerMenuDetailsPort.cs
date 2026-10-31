using NexaConnect.Contracts.Catalog;
using NexaConnect.Contracts.Platform;
using NexaConnect.CustomerBff.Application.Catalog;
namespace NexaConnect.CustomerBff.Infrastructure.Catalog;
public sealed class HttpCustomerMenuDetailsPort(IHttpClientFactory clients):ICustomerMenuDetailsPort
{
    public async Task<CurrentPlatformAccessResponse?> AccessAsync(string token,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,"api/platform-directory/v1/me/access");request.Headers.Authorization=new("Bearer",token);
        using var response=await clients.CreateClient("MenuDetailsDirectory").SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        if(!response.IsSuccessStatusCode)return null;
        await response.Content.LoadIntoBufferAsync(262144,ct);return await response.Content.ReadFromJsonAsync<CurrentPlatformAccessResponse>(ct);
    }
    public Task<HttpResponseMessage> SendAsync(TenantContext tenant,Guid branch,Guid? product,UpdateMenuDetails? input,string token,CancellationToken ct)
    {
        var request=new HttpRequestMessage(input is null?HttpMethod.Get:HttpMethod.Put,$"api/catalog/v1/customer/branches/{branch:D}/menu-details"+(product is null?"":$"/products/{product:D}"));
        request.Headers.Authorization=new("Bearer",token);request.Headers.Add(TenantContextHeaders.OrganizationId,tenant.OrganizationId.ToString("D"));request.Headers.Add(TenantContextHeaders.ApplicationCode,tenant.ApplicationCode);
        if(input is not null)request.Content=JsonContent.Create(input);
        return Send(request,ct);
    }
    private async Task<HttpResponseMessage> Send(HttpRequestMessage request,CancellationToken ct)
    {using(request)return await clients.CreateClient("MenuDetailsCatalog").SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);}
}
