using System.ComponentModel.DataAnnotations;
using NexaConnect.Contracts.Platform;

namespace NexaConnect.CustomerBff.Application.PaymentReviews;

public enum ReviewOperation { List, Detail, History, Access, Resolve }
public sealed record PaymentReviewResolutionRequest(
    [Required,RegularExpression("^(confirm_void|resume_payment|escalate)$")] string Resolution,
    [Required,StringLength(200,MinimumLength=1)] string Reason,
    [Range(1,long.MaxValue)] long ExpectedConcurrencyVersion);

public interface ICustomerPaymentReviewPort
{
    Task<CurrentPlatformAccessResponse?> GetAccessAsync(string token,CancellationToken cancellationToken);
    Task<HttpResponseMessage> SendAsync(TenantContext tenant,string token,ReviewOperation operation,Guid id,PaymentReviewResolutionRequest? request,CancellationToken cancellationToken);
}

public sealed class CustomerPaymentReviewService(ICustomerPaymentReviewPort port)
{
    public async Task<HttpResponseMessage> ExecuteAsync(TenantContext tenant,string token,ReviewOperation operation,Guid id,PaymentReviewResolutionRequest? request,CancellationToken cancellationToken)
    {
        var access=await port.GetAccessAsync(token,cancellationToken);
        if(tenant.ApplicationCode!="nexa_connect"||access?.SubjectId!=tenant.SubjectId||!access.Organizations.Any(item=>item.OrganizationId==tenant.OrganizationId&&item.ApplicationCode==tenant.ApplicationCode))
            return new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden);
        return await port.SendAsync(tenant,token,operation,id,request,cancellationToken);
    }
}
