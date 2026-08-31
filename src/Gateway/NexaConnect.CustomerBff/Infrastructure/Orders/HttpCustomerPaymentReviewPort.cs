using System.Net.Http.Headers;
using NexaConnect.Contracts.Platform;
using NexaConnect.CustomerBff.Application.PaymentReviews;

namespace NexaConnect.CustomerBff.Infrastructure.Orders;

public sealed class HttpCustomerPaymentReviewPort(HttpClient client,IHttpClientFactory clients):ICustomerPaymentReviewPort
{
    public async Task<CurrentPlatformAccessResponse?> GetAccessAsync(string token,CancellationToken cancellationToken)
    {
        using var message=new HttpRequestMessage(HttpMethod.Get,"api/platform-directory/v1/me/access");message.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        using var response=await clients.CreateClient("PlatformDirectory").SendAsync(message,cancellationToken);
        return response.IsSuccessStatusCode?await response.Content.ReadFromJsonAsync<CurrentPlatformAccessResponse>(cancellationToken:cancellationToken):null;
    }
    public async Task<HttpResponseMessage> SendAsync(TenantContext tenant,string token,ReviewOperation operation,Guid id,PaymentReviewResolutionRequest? request,CancellationToken cancellationToken)
    {
        string path=operation switch
        {
            ReviewOperation.List=>$"?organizationId={tenant.OrganizationId:D}&branchId={id:D}&limit=100",
            ReviewOperation.Access=>$"/branches/{id:D}/access?organizationId={tenant.OrganizationId:D}",
            ReviewOperation.Detail=>$"/{id:D}?organizationId={tenant.OrganizationId:D}",
            ReviewOperation.History=>$"/{id:D}/history?organizationId={tenant.OrganizationId:D}",
            ReviewOperation.Resolve=>$"/{id:D}/resolve",
            _=>throw new ArgumentOutOfRangeException(nameof(operation))
        };
        using var message=new HttpRequestMessage(operation==ReviewOperation.Resolve?HttpMethod.Post:HttpMethod.Get,"api/order/v1/payment-reviews"+path);
        message.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        message.Headers.Add(TenantContextHeaders.OrganizationId,tenant.OrganizationId.ToString("D"));
        message.Headers.Add(TenantContextHeaders.ApplicationCode,tenant.ApplicationCode);
        message.Headers.Add(TenantContextHeaders.PortalRequest,"customer");
        if(operation==ReviewOperation.Resolve)
            message.Content=JsonContent.Create(new{tenant.OrganizationId,request!.Resolution,request.Reason,request.ExpectedConcurrencyVersion});
        return await client.SendAsync(message,cancellationToken);
    }
}
