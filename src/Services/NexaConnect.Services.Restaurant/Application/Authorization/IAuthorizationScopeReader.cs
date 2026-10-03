namespace NexaConnect.Services.Restaurant.Application.Authorization;

public sealed record AuthorizationScope(Guid OrganizationId, Guid RestaurantId, Guid BranchId,
    string? TimeZone = null, string? Currency = null);

public interface IAuthorizationScopeReader
{
    Task<AuthorizationScope?> GetAsync(Guid branchId, CancellationToken cancellationToken);
}
