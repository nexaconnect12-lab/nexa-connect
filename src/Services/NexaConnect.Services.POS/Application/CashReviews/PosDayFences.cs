using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.POS.Domain;
namespace NexaConnect.Services.POS.Application.CashReviews;
public interface IPosDayFenceStore
{
 Task<SourceDayFence> ExecuteAsync(SourceFenceCommand command,string actor,bool cancel,CancellationToken ct);
 Task<SourceDayFence?> ReadAsync(EndOfDayWindow window,Guid operation,CancellationToken ct);
}
public sealed class PosDayFences(PosDayRead authorization,NexaConnect.Services.POS.Application.Shifts.IRestaurantScopeReader scopes,NexaConnect.Services.POS.Application.Shifts.IAuthorizationDecisionClient permissions,IPosDayFenceStore? store=null)
{
 public async Task<SourceDayFence> ExecuteAsync(SourceFenceCommand command,NexaConnect.Services.POS.Application.Shifts.PosUserContext bearer,string actor,bool cancel,CancellationToken ct)
 {
  await authorization.AuthorizeAsync(command.Window,bearer,ct);var scope=await scopes.GetAsync(command.Window.BranchId,ct);var decision=await permissions.DecideAsync(bearer,scope,FinancialDayFence.PreparePermission,ct);if(!decision.Granted||decision.DecisionId==Guid.Empty)throw new UnauthorizedAccessException();
  return await (store??throw new InvalidOperationException("Durable fences required.")).ExecuteAsync(command,actor,cancel,ct);
 }
 public async Task<SourceDayFence?> ReadAsync(EndOfDayWindow window,Guid operation,NexaConnect.Services.POS.Application.Shifts.PosUserContext bearer,CancellationToken ct)
 {if(operation==Guid.Empty)throw new ArgumentException();await authorization.AuthorizeAsync(window,bearer,ct);return await (store??throw new InvalidOperationException("Durable fences required.")).ReadAsync(window,operation,ct);}
}
