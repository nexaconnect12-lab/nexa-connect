using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.POS.Application.Shifts;
namespace NexaConnect.Services.POS.Application.DayClose;

public interface ISettlementInventoryStore
{Task<SettlementManifest> ReadAsync(SettlementReportRange range,Guid restaurant,CancellationToken ct);}
public sealed class SettlementInventory(ISettlementInventoryStore store,IRestaurantScopeReader scopes,IAuthorizationDecisionClient permissions)
{
    public static void Validate(SettlementReportRange r)
    {
        if(r.OrganizationId==Guid.Empty||r.BranchId==Guid.Empty||r.FromDate==default||r.FromDate>=r.ToDate
            ||r.ToDate.DayNumber-r.FromDate.DayNumber>31||r.ToDate>DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1))throw new ArgumentException("Invalid settlement date range.");
    }
    public async Task<SettlementManifest> ReadAsync(SettlementReportRange range,PosUserContext user,CancellationToken ct)
    {
        Validate(range);var scope=await scopes.GetAsync(range.BranchId,ct);
        if(scope.OrganizationId!=range.OrganizationId||scope.BranchId!=range.BranchId||scope.RestaurantId==Guid.Empty
            ||string.IsNullOrWhiteSpace(user.Subject)||string.IsNullOrWhiteSpace(user.AccessToken))throw new UnauthorizedAccessException();
        var decision=await permissions.DecideAsync(user,scope,DayClosePreparation.ReadPermission,ct);
        if(!decision.Granted||decision.DecisionId==Guid.Empty)throw new UnauthorizedAccessException();
        return await store.ReadAsync(range,scope.RestaurantId,ct);
    }
}
