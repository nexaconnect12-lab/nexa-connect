using System.Net.Http.Headers;
using NexaConnect.Contracts.Platform;
using NexaConnect.CustomerBff.Application.DayClose;
namespace NexaConnect.CustomerBff.Infrastructure.DayClose;

public sealed class HttpCustomerLateWorkPort(HttpClient client,IHttpClientFactory clients):ICustomerLateWorkPort
{
    public async Task<CurrentPlatformAccessResponse?> GetAccessAsync(string token,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,"api/platform-directory/v1/me/access");request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        using var response=await clients.CreateClient("PlatformDirectory").SendAsync(request,ct);
        return response.IsSuccessStatusCode?await response.Content.ReadFromJsonAsync<CurrentPlatformAccessResponse>(cancellationToken:ct):null;
    }
    public async Task<HttpResponseMessage> ExecuteAsync(TenantContext tenant,string token,LateWorkRequest input,string action,CancellationToken ct)
    {
        string path=$"api/pos/v1/customer/organizations/{tenant.OrganizationId:D}/day-close-late-work";
        if(action=="detail")path+=$"/{input.WorkId:D}";
        if(action!="review")path+=$"?branchId={input.BranchId:D}&businessDate={input.BusinessDate:yyyy-MM-dd}&source={Uri.EscapeDataString(input.Source)}&limit={input.Limit}"+(input.Cursor is null?"":"&cursor="+Uri.EscapeDataString(input.Cursor));

        using var request=new HttpRequestMessage(action=="review"?HttpMethod.Post:HttpMethod.Get,path);request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        request.Headers.Add(TenantContextHeaders.OrganizationId,tenant.OrganizationId.ToString("D"));request.Headers.Add(TenantContextHeaders.ApplicationCode,tenant.ApplicationCode);
        request.Headers.Add(TenantContextHeaders.PortalRequest,"customer");if(action=="review")request.Content=JsonContent.Create(input);
        return await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
    }
}
