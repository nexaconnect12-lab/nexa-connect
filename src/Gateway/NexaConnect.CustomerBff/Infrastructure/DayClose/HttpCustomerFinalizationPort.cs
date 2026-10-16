using System.Net.Http.Headers;
using NexaConnect.Contracts.Platform;
using NexaConnect.CustomerBff.Application.DayClose;
namespace NexaConnect.CustomerBff.Infrastructure.DayClose;

public sealed class HttpCustomerFinalizationPort(HttpClient client,IHttpClientFactory clients):ICustomerFinalizationPort
{
    public async Task<CurrentPlatformAccessResponse?> GetAccessAsync(string token,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,"api/platform-directory/v1/me/access");request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        using var response=await clients.CreateClient("PlatformDirectory").SendAsync(request,ct);
        return response.IsSuccessStatusCode?await response.Content.ReadFromJsonAsync<CurrentPlatformAccessResponse>(cancellationToken:ct):null;
    }
    public async Task<HttpResponseMessage> ExecuteAsync(TenantContext tenant,string token,FinalizationRequest input,string action,CancellationToken ct)
    {
        string path=$"api/pos/v1/customer/organizations/{tenant.OrganizationId:D}/day-close-finalization-preparations";
        if(action=="read")path+=$"?branchId={input.BranchId:D}&businessDate={input.BusinessDate:yyyy-MM-dd}";
        if(action=="cancel")path+="/cancel";
        using var request=new HttpRequestMessage(action!="read"?HttpMethod.Post:HttpMethod.Get,path);request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        request.Headers.Add(TenantContextHeaders.OrganizationId,tenant.OrganizationId.ToString("D"));request.Headers.Add(TenantContextHeaders.ApplicationCode,tenant.ApplicationCode);
        request.Headers.Add(TenantContextHeaders.PortalRequest,"customer");if(action!="read")request.Content=JsonContent.Create(input);
        return await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
    }
}
