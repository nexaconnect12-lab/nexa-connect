using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.Order.Domain;
namespace NexaConnect.Services.Order.Application.Orders;
public interface IOrderLateWorkStore
{
 Task<LateWorkPage> ListAsync(LateWorkScope scope,string? cursor,int limit,bool canReview,CancellationToken ct);
 Task<LateWorkDetail?> ReadAsync(LateWorkScope scope,Guid id,bool canReview,CancellationToken ct);
 Task<LateReviewResult> ReviewAsync(LateWorkScope scope,LateReviewCommand command,string actor,Guid authorization,CancellationToken ct);
}
public sealed class OrderLateWork(OrderDayRead read,NexaConnect.Services.Order.Application.Tenant.IOrderTenantAuthorizer permissions,IOrderLateWorkStore store)
{
 private async Task<Guid?> Permission(EndOfDayWindow w,string user,string permission,CancellationToken ct){return await permissions.GetBranchDecisionAsync(w.OrganizationId,w.BranchId,permission,user,ct);}
 private async Task<bool> Authorize(LateWorkScope scope,string user,CancellationToken ct)
 {
  if(scope.SettlementId==Guid.Empty)throw new ArgumentException();await read.AuthorizeAsync(scope.Window,user,ct);
  if(await Permission(scope.Window,user,"pos.day-close.read",ct) is not {} id||id==Guid.Empty)throw new UnauthorizedAccessException();
  return await Permission(scope.Window,user,LateWorkCase.ReviewPermission,ct) is {} review&&review!=Guid.Empty;
 }
 public async Task<LateWorkPage> ListAsync(LateWorkScope scope,string? cursor,int limit,string user,CancellationToken ct)=>await store.ListAsync(scope,cursor,limit,await Authorize(scope,user,ct),ct);
 public async Task<LateWorkDetail?> ReadAsync(LateWorkScope scope,Guid id,string user,CancellationToken ct)
 {if(id==Guid.Empty)throw new ArgumentException();return await store.ReadAsync(scope,id,await Authorize(scope,user,ct),ct);}
 public async Task<LateReviewResult> ReviewAsync(LateWorkScope scope,LateReviewCommand command,string user,string actor,CancellationToken ct)
 {
  if(!await Authorize(scope,user,ct))throw new UnauthorizedAccessException();
  var current=await store.ReadAsync(scope,command.WorkId,true,ct)??throw new ArgumentException("Review case not found.");
  // Validate business command independently of replay; the store checks expected version only for a new operation.
  new LateWorkCase(command.ExpectedVersion,current.Item.Status).Review(command.ExpectedVersion,command.Decision,command.ReasonCode);
  if(!await Authorize(scope,user,ct)||await Permission(scope.Window,user,LateWorkCase.ReviewPermission,ct) is not {} id||id==Guid.Empty)throw new UnauthorizedAccessException();
  return await store.ReviewAsync(scope,command,actor,id,ct);
 }
}
