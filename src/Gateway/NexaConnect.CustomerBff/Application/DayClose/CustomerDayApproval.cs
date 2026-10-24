using System.Net;
using NexaConnect.Contracts.Platform;
namespace NexaConnect.CustomerBff.Application.DayClose;

public sealed record DayApprovalRequest(Guid BranchId,DateOnly BusinessDate,Guid OperationId=default,long ExpectedApprovalVersion=0,long ReviewedSealVersion=0,string? ReasonCode=null);
public interface ICustomerDayApprovalPort
{
    Task<CurrentPlatformAccessResponse?> GetAccessAsync(string token,CancellationToken ct);
    Task<HttpResponseMessage> ExecuteAsync(TenantContext tenant,string token,DayApprovalRequest request,bool approve,CancellationToken ct);
}
public sealed class CustomerDayApproval(ICustomerDayApprovalPort port)
{
    public async Task<HttpResponseMessage> ExecuteAsync(TenantContext tenant,string token,DayApprovalRequest request,bool approve,CancellationToken ct)
    {
        if(request.BranchId==Guid.Empty || request.BusinessDate==default || request.BusinessDate==DateOnly.MaxValue
            || approve&&(request.OperationId==Guid.Empty || request.ExpectedApprovalVersion<0 || request.ReviewedSealVersion<=0
            || request.ReasonCode is not("review_complete" or "review_after_changes")))return new(HttpStatusCode.BadRequest);
        if(tenant.ApplicationCode!="nexa_connect")return new(HttpStatusCode.Forbidden);
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var access=await port.GetAccessAsync(token,deadline.Token);
        if(access?.SubjectId!=tenant.SubjectId || access.Organizations?.Any(x=>x.OrganizationId==tenant.OrganizationId&&x.ApplicationCode==tenant.ApplicationCode)!=true)return new(HttpStatusCode.Forbidden);
        return await port.ExecuteAsync(tenant,token,request,approve,deadline.Token);
    }
}
