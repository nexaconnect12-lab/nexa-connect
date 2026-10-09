using System.Net;
using NexaConnect.Contracts.Platform;
namespace NexaConnect.CustomerBff.Application.DayClose;
public sealed record DayCloseRequest(Guid BranchId,DateOnly BusinessDate,Guid OperationId=default,long ExpectedVersion=0,string? ReasonCode=null,long? ReviewedCutoffVersion=null);
public interface ICustomerDayClosePort
{
    Task<CurrentPlatformAccessResponse?> GetAccessAsync(string token,CancellationToken ct);
    Task<HttpResponseMessage> ExecuteAsync(TenantContext tenant,string token,DayCloseRequest request,bool prepare,CancellationToken ct);
}
public sealed class CustomerDayClose(ICustomerDayClosePort port)
{
    public async Task<HttpResponseMessage> ExecuteAsync(TenantContext tenant,string token,DayCloseRequest request,bool prepare,CancellationToken ct)
    {
        if(request.BranchId==Guid.Empty || request.BusinessDate==default || request.BusinessDate==DateOnly.MaxValue
            || prepare && (request.OperationId==Guid.Empty || request.ExpectedVersion<0 || request.ReasonCode is not("routine_close" or "recheck")))return new(HttpStatusCode.BadRequest);
        if(tenant.ApplicationCode!="nexa_connect")return new(HttpStatusCode.Forbidden);
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var access=await port.GetAccessAsync(token,deadline.Token);
        if(access?.SubjectId!=tenant.SubjectId || access.Organizations?.Any(x=>x.OrganizationId==tenant.OrganizationId && x.ApplicationCode==tenant.ApplicationCode)!=true)return new(HttpStatusCode.Forbidden);
        return await port.ExecuteAsync(tenant,token,request,prepare,deadline.Token);
    }
}
