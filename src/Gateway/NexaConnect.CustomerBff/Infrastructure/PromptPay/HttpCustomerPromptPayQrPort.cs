using NexaConnect.Contracts.Media;
using NexaConnect.Contracts.Platform;
using NexaConnect.CustomerBff.Application.PromptPay;
namespace NexaConnect.CustomerBff.Infrastructure.PromptPay;

public sealed class HttpCustomerPromptPayQrPort(IHttpClientFactory clients):ICustomerPromptPayQrPort
{
    public async Task<HttpResponseMessage> UploadAsync(TenantContext tenant,Guid branch,Guid? asset,StartPromptPayQrUploadV1? start,CompletePromptPayQrUploadV1? complete,string token,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,$"api/media/v1/customer/organizations/{tenant.OrganizationId:D}/branches/{branch:D}/promptpay-uploads{(asset is {} id?$"/{id:D}/complete":"")}");
        request.Headers.Authorization=new("Bearer",token);
        request.Headers.Add(TenantContextHeaders.OrganizationId,tenant.OrganizationId.ToString("D"));request.Headers.Add(TenantContextHeaders.ApplicationCode,tenant.ApplicationCode);
        request.Content=start is not null?JsonContent.Create(start):JsonContent.Create(complete);
        var response=await clients.CreateClient("PromptPayMedia").SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        try{
            if(response.IsSuccessStatusCode){
                await response.Content.LoadIntoBufferAsync(16384,ct);
                if(start is not null){var value=await response.Content.ReadFromJsonAsync<PromptPayQrUploadSessionV1>(ct);
                    if(value is null||value.OrganizationId!=tenant.OrganizationId||value.BranchId!=branch||value.AssetId==Guid.Empty||value.Version<1)throw new InvalidDataException();}
                else{var value=await response.Content.ReadFromJsonAsync<PromptPayQrUploadResultV1>(ct);
                    if(value is null||value.OrganizationId!=tenant.OrganizationId||value.BranchId!=branch||value.AssetId!=asset||value.Version<1)throw new InvalidDataException();}
            }
            return response;
        }catch{response.Dispose();throw;}
    }
    public async Task<CurrentPlatformAccessResponse?> AccessAsync(string token,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,"api/platform-directory/v1/me/access");request.Headers.Authorization=new("Bearer",token);
        using var response=await clients.CreateClient("PromptPayDirectory").SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        if(!response.IsSuccessStatusCode)return null;await response.Content.LoadIntoBufferAsync(65536,ct);
        return await response.Content.ReadFromJsonAsync<CurrentPlatformAccessResponse>(ct);
    }
    public async Task<HttpResponseMessage> SendAsync(TenantContext tenant,Guid branch,UpdateBranchPromptPayQrV1? update,bool image,string token,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(update is null?HttpMethod.Get:HttpMethod.Put,
            $"api/{(image?"media":"restaurant")}/v1/customer/organizations/{tenant.OrganizationId:D}/branches/{branch:D}/promptpay-qr{(image?"/management-preview":update is null?"/manage-access":"")}");
        request.Headers.Authorization=new("Bearer",token);
        request.Headers.Add(TenantContextHeaders.OrganizationId,tenant.OrganizationId.ToString("D"));request.Headers.Add(TenantContextHeaders.ApplicationCode,tenant.ApplicationCode);
        if(update is not null)request.Content=JsonContent.Create(update);
        var response=await clients.CreateClient(image?"PromptPayMedia":"PromptPayRestaurant").SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        try{
            if(response.IsSuccessStatusCode){
                await response.Content.LoadIntoBufferAsync(image?3_000_000:16384,ct);
                var selection=image?(await response.Content.ReadFromJsonAsync<PromptPayQrBundleV1>(ct))?.Selection:await response.Content.ReadFromJsonAsync<BranchPromptPayQrV1>(ct);
                if(selection is null||selection.OrganizationId!=tenant.OrganizationId||selection.BranchId!=branch||selection.RestaurantId==Guid.Empty||selection.Version<1||selection.AssetId==Guid.Empty)throw new InvalidDataException("Invalid PromptPay response scope.");
            }
            return response;
        }catch{response.Dispose();throw;}
    }
}
