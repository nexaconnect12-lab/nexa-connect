using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.POS.Domain;
namespace NexaConnect.Services.POS.Application.CashReviews;
public interface IPOSLateWorkStore
{
 Task<LateWorkPage> ListAsync(LateWorkScope scope,string? cursor,int limit,bool canReview,CancellationToken ct);
 Task<LateWorkDetail?> ReadAsync(LateWorkScope scope,Guid id,bool canReview,CancellationToken ct);
 Task<LateReviewResult> ReviewAsync(LateWorkScope scope,LateReviewCommand command,string actor,Guid authorization,CancellationToken ct);
}
public sealed class POSLateWork(PosDayRead read,NexaConnect.Services.POS.Application.Shifts.IRestaurantScopeReader scopes,NexaConnect.Services.POS.Application.Shifts.IAuthorizationDecisionClient permissions,IPOSLateWorkStore store)
{
 private async Task<Guid?> Permission(EndOfDayWindow w,NexaConnect.Services.POS.Application.Shifts.PosUserContext user,string permission,CancellationToken ct){var scope=await scopes.GetAsync(w.BranchId,ct);if(scope.OrganizationId!=w.OrganizationId||scope.RestaurantId!=w.RestaurantId)throw new UnauthorizedAccessException();var value=await permissions.DecideAsync(user,scope,permission,ct);return value.Granted&&value.DecisionId!=Guid.Empty?value.DecisionId:null;}
 private async Task<bool> Authorize(LateWorkScope scope,NexaConnect.Services.POS.Application.Shifts.PosUserContext user,CancellationToken ct)
 {
  if(scope.SettlementId==Guid.Empty)throw new ArgumentException();await read.AuthorizeAsync(scope.Window,user,ct);
  if(await Permission(scope.Window,user,"pos.day-close.read",ct) is not {} id||id==Guid.Empty)throw new UnauthorizedAccessException();
  return await Permission(scope.Window,user,LateWorkCase.ReviewPermission,ct) is {} review&&review!=Guid.Empty;
 }
 public async Task<LateWorkPage> ListAsync(LateWorkScope scope,string? cursor,int limit,NexaConnect.Services.POS.Application.Shifts.PosUserContext user,CancellationToken ct)=>await store.ListAsync(scope,cursor,limit,await Authorize(scope,user,ct),ct);
 public async Task<LateWorkDetail?> ReadAsync(LateWorkScope scope,Guid id,NexaConnect.Services.POS.Application.Shifts.PosUserContext user,CancellationToken ct)
 {if(id==Guid.Empty)throw new ArgumentException();return await store.ReadAsync(scope,id,await Authorize(scope,user,ct),ct);}
 public async Task<LateReviewResult> ReviewAsync(LateWorkScope scope,LateReviewCommand command,NexaConnect.Services.POS.Application.Shifts.PosUserContext user,string actor,CancellationToken ct)
 {
  if(!await Authorize(scope,user,ct))throw new UnauthorizedAccessException();
  var current=await store.ReadAsync(scope,command.WorkId,true,ct)??throw new ArgumentException("Review case not found.");
  // Validate business command independently of replay; the store checks expected version only for a new operation.
  new LateWorkCase(command.ExpectedVersion,current.Item.Status).Review(command.ExpectedVersion,command.Decision,command.ReasonCode);
  if(!await Authorize(scope,user,ct)||await Permission(scope.Window,user,LateWorkCase.ReviewPermission,ct) is not {} id||id==Guid.Empty)throw new UnauthorizedAccessException();
  return await store.ReviewAsync(scope,command,actor,id,ct);
 }
}
