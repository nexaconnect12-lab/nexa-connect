using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Domain.DayClose;
namespace NexaConnect.Services.POS.Application.DayClose;

public sealed record DayLateWorkRequest(Guid BranchId,DateOnly BusinessDate,string Source,Guid WorkId=default,LateReviewCommand? Command=null,string? Cursor=null,int Limit=25);
public interface IDayLateWorkPort
{ Task<object?> ExecuteAsync(LateWorkScope scope,DayLateWorkRequest input,string action,PosUserContext user,CancellationToken ct); }
public sealed class DayLateWork(IDaySettlementStore settlements,IRestaurantScopeReader scopes,IAuthorizationDecisionClient authorization,IDayLateWorkPort port)
{
 public async Task<object?> ExecuteAsync(Guid organization,DayLateWorkRequest input,string action,PosUserContext user,CancellationToken ct)
 {
  if(organization==Guid.Empty||input.BranchId==Guid.Empty||input.BusinessDate==default||input.BusinessDate==DateOnly.MaxValue
   ||input.Source is not("Order" or "Payment" or "POS")||action is not("list" or "detail" or "review")||input.Limit is <1 or >50
   ||input.Cursor?.Length>200||action=="detail"&&input.WorkId==Guid.Empty||action=="review"&&(input.WorkId==Guid.Empty||input.Command is null||input.Command.WorkId!=input.WorkId))throw new ArgumentException();
  var branch=await scopes.GetAsync(input.BranchId,ct);
  if(branch.OrganizationId!=organization||branch.BranchId!=input.BranchId||branch.RestaurantId==Guid.Empty)throw new UnauthorizedAccessException();
  var read=await authorization.DecideAsync(user,branch,DayClosePreparation.ReadPermission,ct);
  if(!read.Granted||read.DecisionId==Guid.Empty)throw new UnauthorizedAccessException();
  if(action=="review"){
   var review=await authorization.DecideAsync(user,branch,Domain.LateWorkCase.ReviewPermission,ct);
   if(!review.Granted||review.DecisionId==Guid.Empty)throw new UnauthorizedAccessException();
  }
  var state=await settlements.ReadAsync(new(organization,branch.RestaurantId,input.BranchId,input.BusinessDate),ct);
  if(state?.Receipt is null)throw new DayCloseConflictException("settlement_not_committed");
  var snapshot=state.Preparation.Approval.Snapshot;
  return await port.ExecuteAsync(new(new(organization,branch.RestaurantId,input.BranchId,snapshot.FromUtc,snapshot.ToUtc),state.Id),input,action,user,ct);
 }
}
