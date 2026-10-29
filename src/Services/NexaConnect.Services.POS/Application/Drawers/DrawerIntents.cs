using NexaConnect.Contracts.POS;
using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Domain.Drawers;
namespace NexaConnect.Services.POS.Application.Drawers;

public interface IDrawerIntentRepository
{
    Task<(DrawerIntent Intent,bool Created)> ReserveAsync(DrawerCommand command,string actor,Guid decision,
        Func<DrawerCustody?,DateTimeOffset,DrawerIntent> create,CancellationToken ct);
    Task<DrawerIntent?> FindAsync(Guid organization,Guid operation,CancellationToken ct);
    Task<DrawerIntent> ReportAsync(DrawerIntent original,string outcome,Guid decision,CancellationToken ct);
}
public sealed class DrawerIntents(IDrawerIntentRepository repository,IRestaurantScopeReader hierarchy,IAuthorizationDecisionClient authorization)
{
    public const string ManualPermission="pos.drawer.manual-open";
    public const string SalePermission="pos.drawer.cash-sale-open";
    public const string ReadPermission="pos.drawer.read";
    public async Task<DrawerIntentV1> ReserveAsync(DrawerIntentRequestV1 input,PosUserContext user,CancellationToken ct)
    {
        var command=new DrawerCommand(new(input.OrganizationId,input.RestaurantId,input.BranchId,input.StoreId,input.TerminalId,input.ShiftId,input.CashSessionId),input.OperationId,input.Cause,input.OrderId,input.Reason);
        DrawerIntent.Validate(command,user.Subject);
        var decision=await Authorize(command.Scope,user,command.Cause=="manual"?ManualPermission:SalePermission,ct);
        var saved=await repository.ReserveAsync(command,user.Subject,decision,(custody,now)=>DrawerIntent.Reserve(command,user.Subject,decision,custody,now),ct);
        return View(saved.Intent,saved.Created);
    }
    public async Task<DrawerIntentV1?> ReadAsync(Guid org,Guid operation,PosUserContext user,CancellationToken ct)
    {
        var original=await Original(org,operation,user,ReadPermission,ct);return original is null?null:View(original.Value.Intent,false);
    }
    public async Task<DrawerIntentV1?> ReportAsync(Guid operation,DrawerOutcomeRequestV1 input,PosUserContext user,CancellationToken ct)
    {
        var original=await Original(input.OrganizationId,operation,user,null,ct);
        if(original is null)return null;
        return View(await repository.ReportAsync(original.Value.Intent,input.Outcome,original.Value.Decision,ct),false);
    }
    private async Task<(DrawerIntent Intent,Guid Decision)?> Original(Guid org,Guid operation,PosUserContext user,string? permission,CancellationToken ct)
    {
        if(org==Guid.Empty||operation==Guid.Empty||string.IsNullOrWhiteSpace(user.Subject))throw new ArgumentException();
        var original=await repository.FindAsync(org,operation,ct);
        if(original is null)return null;
        if(original.Actor!=user.Subject)throw new UnauthorizedAccessException();
        var decision=await Authorize(original.Command.Scope,user,permission??(original.Command.Cause=="manual"?ManualPermission:SalePermission),ct);
        return (original,decision);
    }
    private async Task<Guid> Authorize(DrawerScope s,PosUserContext user,string permission,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(user.Subject)||string.IsNullOrWhiteSpace(user.AccessToken))throw new UnauthorizedAccessException();
        var scope=await hierarchy.GetAsync(s.BranchId,ct);
        if(scope.OrganizationId!=s.OrganizationId||scope.RestaurantId!=s.RestaurantId||scope.BranchId!=s.BranchId)throw new UnauthorizedAccessException();
        var decision=await authorization.DecideAsync(user,scope,permission,ct);
        if(!decision.Granted||decision.DecisionId==Guid.Empty)throw new UnauthorizedAccessException();
        return decision.DecisionId;
    }
    private static DrawerIntentV1 View(DrawerIntent value,bool created)=>new(value.Command.OperationId,value.Status,created,value.ReservedAtUtc,value.ExpiresAtUtc,value.ReportedAtUtc);
}
