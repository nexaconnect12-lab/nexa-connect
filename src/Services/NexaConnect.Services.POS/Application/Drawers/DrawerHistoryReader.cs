using NexaConnect.Contracts.POS;
using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Domain.Drawers;
namespace NexaConnect.Services.POS.Application.Drawers;

public interface IDrawerHistoryRepository
{
    Task<bool> MatchesAsync(DrawerHistoryScope scope,CancellationToken ct);
    Task<IReadOnlyList<DrawerIntent>> ReadAsync(DrawerHistoryQuery query,CancellationToken ct);
    Task<DrawerIntent?> FindHistoryAsync(DrawerHistoryScope scope,string actor,Guid operation,CancellationToken ct);
}
public sealed class DrawerHistoryReader(IDrawerHistoryRepository repository,IRestaurantScopeReader hierarchy,IAuthorizationDecisionClient authorization,TimeProvider clock)
{
    public async Task<DrawerHistoryItemV1?> ReviewAsync(DrawerHistoryScope scope,Guid operation,PosUserContext user,CancellationToken ct)
    {
        if(operation==Guid.Empty)throw new ArgumentException();
        scope.Validate(user.Subject);
        await Require(scope,user,ct);var original=await repository.FindHistoryAsync(scope,user.Subject,operation,ct);await Require(scope,user,ct);
        if(original is not null&&(original.Command.OperationId!=operation||!InScope(original,scope,user.Subject)))throw new InvalidDataException();
        return original is null?null:Item(original);
    }
    public async Task<DrawerHistoryPageV1> ReadAsync(DrawerHistoryScope scope,DateTimeOffset from,DateTimeOffset to,int limit,string? cursor,PosUserContext user,CancellationToken ct)
    {
        var query=DrawerHistoryQuery.Create(scope,user.Subject,from,to,limit,cursor,clock.GetUtcNow());
        await Require(scope,user,ct);
        var rows=await repository.ReadAsync(query,ct);
        // Repeat live hierarchy/permission/terminal relationship checks after I/O.
        await Require(scope,user,ct);
        if(rows.Count>limit+1||rows.Any(x=>!InScope(x,scope,user.Subject)))throw new InvalidDataException();
        var page=rows.Take(limit).ToArray();var items=page.Select(Item).ToArray();
        return new(scope.OrganizationId,scope.RestaurantId,scope.BranchId,scope.StoreId,scope.TerminalId,query.FromUtc,query.ToUtc,items,rows.Count>limit?query.Cursor(page[^1]):null);
    }
    private static DrawerHistoryItemV1 Item(DrawerIntent x)=>new(x.Command.OperationId,x.Command.Cause,x.Command.Reason,x.Command.OrderId,x.Command.Scope.ShiftId,x.Command.Scope.CashSessionId,x.Status,x.ReservedAtUtc,x.ExpiresAtUtc,x.ReportedAtUtc);
    private static bool InScope(DrawerIntent x,DrawerHistoryScope scope,string actor)=>x.Actor==actor&&x.Command.Scope.OrganizationId==scope.OrganizationId&&x.Command.Scope.RestaurantId==scope.RestaurantId&&x.Command.Scope.BranchId==scope.BranchId&&x.Command.Scope.StoreId==scope.StoreId&&x.Command.Scope.TerminalId==scope.TerminalId;
    private async Task Require(DrawerHistoryScope scope,PosUserContext user,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(user.AccessToken))throw new UnauthorizedAccessException();
        var actual=await hierarchy.GetAsync(scope.BranchId,ct);
        if(actual.OrganizationId!=scope.OrganizationId||actual.RestaurantId!=scope.RestaurantId||actual.BranchId!=scope.BranchId)throw new UnauthorizedAccessException();
        var decision=await authorization.DecideAsync(user,actual,DrawerIntents.ReadPermission,ct);
        if(!decision.Granted||decision.DecisionId==Guid.Empty||!await repository.MatchesAsync(scope,ct))throw new UnauthorizedAccessException();
    }
}
