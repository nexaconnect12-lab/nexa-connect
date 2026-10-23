using NexaConnect.Contracts.Platform;

namespace NexaConnect.Services.Restaurant.Application.Branches;

public sealed class OperationalBranchDiscovery(IBranchManagementRepository repository, IBranchCustomerAuthorizer authorizer)
{
    public static string PermissionFor(string capability) => capability switch
    {
        "kitchen" => "kitchen.ticket.read",
        "day-close" => "pos.day-close.read",
        "cash-close" => "pos.cash-review.read",
        "reports" => "reporting.dashboard.read",
        "payment-review" => "order.payment-review.read",
        "catalog" => ProductPermissions.CatalogMenuRead,
        "qr-entry" => ProductPermissions.RestaurantConfigurationManage,
        _ => throw new ArgumentException("Unsupported operational capability.")
    };

    public async Task<IReadOnlyCollection<BranchSummary>> ListAsync(Guid organizationId, string capability,
        string authorizationHeader, CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty) throw new ArgumentException("Organization is required.");
        string permission = PermissionFor(capability);
        var result = new List<BranchSummary>();
        foreach (var branch in await repository.ListAsync(organizationId, cancellationToken))
        {
            if (branch.Status == "active" && await authorizer.IsGrantedAsync(organizationId, branch.RestaurantId,
                branch.BranchId, permission, authorizationHeader, cancellationToken)) result.Add(branch);
        }
        return result;
    }
}
