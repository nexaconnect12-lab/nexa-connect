using System.Net;
using NexaConnect.Contracts.Platform;
using NexaConnect.Contracts.Reporting;
namespace NexaConnect.CustomerBff.Application.DayClose;
public sealed record LateWorkRequest(Guid BranchId,DateOnly BusinessDate,string Source,Guid WorkId=default,LateReviewCommand? Command=null,string? Cursor=null,int Limit=25);
public interface ICustomerLateWorkPort
{Task<CurrentPlatformAccessResponse?> GetAccessAsync(string token,CancellationToken ct);Task<HttpResponseMessage> ExecuteAsync(TenantContext tenant,string token,LateWorkRequest request,string action,CancellationToken ct);}
public sealed class CustomerLateWork(ICustomerLateWorkPort port)
{
 public async Task<HttpResponseMessage> ExecuteAsync(TenantContext tenant,string token,LateWorkRequest request,string action,CancellationToken ct)
 {
  if(action is not("list" or "detail" or "review")||request.BranchId==Guid.Empty||request.BusinessDate==default||request.BusinessDate==DateOnly.MaxValue
   ||request.Source is not("Order" or "Payment" or "POS")||request.Limit is <1 or >50||request.Cursor?.Length>200
   ||action=="detail"&&request.WorkId==Guid.Empty||action=="review"&&(request.WorkId==Guid.Empty||request.Command is null||request.WorkId!=request.Command.WorkId||request.Command.OperationId==Guid.Empty||request.Command.ExpectedVersion<0
    ||(request.Command.Decision,request.Command.ReasonCode) is not(("investigate","investigate_delivery") or ("require_correction","correction_needed") or ("acknowledge","evidence_checked"))))return new(HttpStatusCode.BadRequest);
  if(tenant.ApplicationCode!="nexa_connect")return new(HttpStatusCode.Forbidden);
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(30));
  var access=await port.GetAccessAsync(token,deadline.Token);
  if(access?.SubjectId!=tenant.SubjectId||access.Organizations?.Any(x=>x.OrganizationId==tenant.OrganizationId&&x.ApplicationCode==tenant.ApplicationCode)!=true)return new(HttpStatusCode.Forbidden);
  return await port.ExecuteAsync(tenant,token,request,action,deadline.Token);
 }
}
