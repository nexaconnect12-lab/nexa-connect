using NexaConnect.Contracts.Catalog;
using NexaConnect.Contracts.Platform;
using NexaConnect.CustomerBff.Application.Catalog;

namespace NexaConnect.CustomerBff.Infrastructure.Catalog;

public sealed class HttpCustomerBarcodePort(IHttpClientFactory clients):ICustomerBarcodePort
{
    public async Task<CurrentPlatformAccessResponse?> AccessAsync(string token,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,"api/platform-directory/v1/me/access");request.Headers.Authorization=new("Bearer",token);
        using var response=await clients.CreateClient("PlatformDirectory").SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        if(!response.IsSuccessStatusCode)return null;
        await response.Content.LoadIntoBufferAsync(262144,ct);return await response.Content.ReadFromJsonAsync<CurrentPlatformAccessResponse>(ct);
    }
    public async Task<HttpResponseMessage> SendAsync(TenantContext tenant,Guid branch,Guid product,SetProductBarcode? update,string token,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(update is null?HttpMethod.Get:HttpMethod.Put,$"api/catalog/v1/customer/branches/{branch:D}/products/{product:D}/barcode");
        request.Headers.Authorization=new("Bearer",token);request.Headers.Add(TenantContextHeaders.OrganizationId,tenant.OrganizationId.ToString("D"));request.Headers.Add(TenantContextHeaders.ApplicationCode,tenant.ApplicationCode);
        if(update is not null)request.Content=JsonContent.Create(update);
        return await clients.CreateClient("BarcodeCatalog").SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
    }
}
