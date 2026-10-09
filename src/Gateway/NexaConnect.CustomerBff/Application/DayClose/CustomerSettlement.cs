using System.Net;
using NexaConnect.Contracts.Platform;
namespace NexaConnect.CustomerBff.Application.DayClose;
public sealed record SettlementRequest(Guid BranchId,DateOnly BusinessDate,Guid OperationId=default,long ExpectedPreparationVersion=0,Guid PreparationOperationId=default,Guid ApprovalId=default,long ReviewedApprovalVersion=0);
public interface ICustomerSettlementPort
{Task<CurrentPlatformAccessResponse?> GetAccessAsync(string token,CancellationToken ct);Task<HttpResponseMessage> ExecuteAsync(TenantContext tenant,string token,SettlementRequest request,string action,CancellationToken ct);}
public sealed class CustomerSettlement(ICustomerSettlementPort port)
{
 public async Task<HttpResponseMessage> ExecuteAsync(TenantContext tenant,string token,SettlementRequest request,string action,CancellationToken ct)
 {
  if(action is not ("read" or "finalize") || request.BranchId==Guid.Empty||request.BusinessDate==default||request.BusinessDate==DateOnly.MaxValue
   ||action!="read"&&request.OperationId==Guid.Empty||action=="finalize"&&(request.ExpectedPreparationVersion<=0||request.PreparationOperationId==Guid.Empty||request.ApprovalId==Guid.Empty||request.ReviewedApprovalVersion<=0))return new(HttpStatusCode.BadRequest);
  if(tenant.ApplicationCode!="nexa_connect")return new(HttpStatusCode.Forbidden);
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(30));
  var access=await port.GetAccessAsync(token,deadline.Token);
  if(access?.SubjectId!=tenant.SubjectId||access.Organizations?.Any(x=>x.OrganizationId==tenant.OrganizationId&&x.ApplicationCode==tenant.ApplicationCode)!=true)return new(HttpStatusCode.Forbidden);
  return await port.ExecuteAsync(tenant,token,request,action,deadline.Token);
 }
}
