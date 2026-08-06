namespace NexaConnect.Services.Restaurant.Application.Authorization;

public sealed record AuthorizationScope(Guid OrganizationId, Guid RestaurantId, Guid BranchId);

public interface IAuthorizationScopeReader
{
    Task<AuthorizationScope?> GetAsync(Guid branchId, CancellationToken cancellationToken);
}
