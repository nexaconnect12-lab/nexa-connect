using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Application.CashReviews;

namespace NexaConnect.Services.POS.Application.DayClose;

public interface ICashCorrectionInventoryStore
{
    Task<CashCorrectionManifest> ReadAsync(Guid organization, Guid restaurant, Guid branch, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
public sealed class CashCorrectionInventory(ICashCorrectionInventoryStore store, IRestaurantScopeReader scopes, IAuthorizationDecisionClient permissions)
{
    public static void Validate(Guid organization, Guid branch, DateTimeOffset from, DateTimeOffset to)
    {
        if (organization == Guid.Empty || branch == Guid.Empty || from == default || from >= to
            || to - from > TimeSpan.FromDays(31) || to > DateTimeOffset.UtcNow) throw new ArgumentException("Invalid correction inventory range.");
    }
    public async Task<CashCorrectionManifest> ReadAsync(Guid organization, Guid branch, DateTimeOffset from, DateTimeOffset to, PosUserContext user, CancellationToken ct)
    {
        Validate(organization, branch, from, to);
        var scope = await scopes.GetAsync(branch, ct);
        if (scope.OrganizationId != organization || scope.BranchId != branch || scope.RestaurantId == Guid.Empty
            || string.IsNullOrWhiteSpace(user.Subject) || string.IsNullOrWhiteSpace(user.AccessToken)) throw new UnauthorizedAccessException();
        foreach (var permission in new[] { DayClosePreparation.ReadPermission, CashReviewPermissions.Read })
        {
            var decision = await permissions.DecideAsync(user, scope, permission, ct);
            if (!decision.Granted || decision.DecisionId == Guid.Empty) throw new UnauthorizedAccessException();
        }
        return await store.ReadAsync(organization, scope.RestaurantId, branch, from.ToUniversalTime(), to.ToUniversalTime(), ct);
    }
}
