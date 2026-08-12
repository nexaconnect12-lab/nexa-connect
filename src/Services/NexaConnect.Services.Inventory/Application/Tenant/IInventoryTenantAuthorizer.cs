namespace NexaConnect.Services.Inventory.Application.Tenant;

public interface IInventoryTenantAuthorizer
{
    Task<bool> HasBranchAccessAsync(Guid organizationId, Guid branchId, string permission, string authorizationHeader,
        CancellationToken cancellationToken);
}
