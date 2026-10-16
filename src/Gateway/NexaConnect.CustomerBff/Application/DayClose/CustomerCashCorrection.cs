using System.Net;
using NexaConnect.Contracts.Platform;
using NexaConnect.Contracts.Reporting;
namespace NexaConnect.CustomerBff.Application.DayClose;
public sealed record CashCorrectionRequest(Guid BranchId,DateOnly BusinessDate,Guid WorkId,LateCashCorrectionCommand? Command=null);
public interface ICustomerCashCorrectionPort
{Task<CurrentPlatformAccessResponse?> GetAccessAsync(string token,CancellationToken ct);Task<HttpResponseMessage> ExecuteAsync(TenantContext tenant,string token,CashCorrectionRequest request,string action,CancellationToken ct);}
public sealed class CustomerCashCorrection(ICustomerCashCorrectionPort port)
{
 public async Task<HttpResponseMessage> ExecuteAsync(TenantContext tenant,string token,CashCorrectionRequest request,string action,CancellationToken ct)
 {
  if(action is not("preview" or "post")||request.BranchId==Guid.Empty||request.WorkId==Guid.Empty||request.BusinessDate==default||request.BusinessDate==DateOnly.MaxValue
   ||action=="post"&&(request.Command is null||request.WorkId!=request.Command.WorkId||request.Command.OperationId==Guid.Empty||request.Command.ExpectedReviewVersion<=0||request.Command.PreviewFingerprint is null||request.Command.PreviewFingerprint.Length!=64||request.Command.PreviewFingerprint.Any(c=>!char.IsAsciiHexDigit(c))))return new(HttpStatusCode.BadRequest);
  if(tenant.ApplicationCode!="nexa_connect")return new(HttpStatusCode.Forbidden);
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(30));var access=await port.GetAccessAsync(token,deadline.Token);
  if(access?.SubjectId!=tenant.SubjectId||access.Organizations?.Any(x=>x.OrganizationId==tenant.OrganizationId&&x.ApplicationCode==tenant.ApplicationCode)!=true)return new(HttpStatusCode.Forbidden);
  return await port.ExecuteAsync(tenant,token,request,action,deadline.Token);
 }
}
