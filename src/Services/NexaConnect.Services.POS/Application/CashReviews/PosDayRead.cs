using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.POS.Application.Shifts;

namespace NexaConnect.Services.POS.Application.CashReviews;

public interface IPosDayReader
{
    Task<PosDaySummary> ReadAsync(EndOfDayWindow window, CancellationToken ct);
}
public sealed class PosDayRead(IPosDayReader reader, IRestaurantScopeReader scopes, IAuthorizationDecisionClient authorization)
{
    public async Task<PosDaySummary> ReadAsync(EndOfDayWindow window, PosUserContext user, CancellationToken ct)
    {
        if (window.OrganizationId == Guid.Empty || window.RestaurantId == Guid.Empty || window.BranchId == Guid.Empty
            || window.FromUtc == default || window.ToUtc <= window.FromUtc
            || window.ToUtc - window.FromUtc > TimeSpan.FromHours(27) || window.ToUtc > DateTimeOffset.UtcNow)
            throw new ArgumentException("A closed branch day is required.");
        var scope = await scopes.GetAsync(window.BranchId, ct);
        if (scope.OrganizationId != window.OrganizationId || scope.RestaurantId != window.RestaurantId || scope.BranchId != window.BranchId
            || string.IsNullOrWhiteSpace(user.Subject) || string.IsNullOrWhiteSpace(user.AccessToken)
            || !(await authorization.DecideAsync(user, scope, CashReviewPermissions.Read, ct)).Granted)
            throw new UnauthorizedAccessException();
        return await reader.ReadAsync(window, ct);
    }
}
