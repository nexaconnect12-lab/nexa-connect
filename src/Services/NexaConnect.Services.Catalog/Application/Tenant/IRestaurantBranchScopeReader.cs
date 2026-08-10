namespace NexaConnect.Services.Catalog.Application.Tenant;

public interface IRestaurantBranchScopeReader
{
    Task<RestaurantBranchScope?> GetAsync(Guid branchId, CancellationToken cancellationToken);
}

public sealed record RestaurantBranchScope(Guid OrganizationId, Guid RestaurantId, Guid BranchId);
