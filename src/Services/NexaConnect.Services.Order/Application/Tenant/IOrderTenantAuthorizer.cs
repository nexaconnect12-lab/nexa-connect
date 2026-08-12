namespace NexaConnect.Services.Order.Application.Tenant;

public interface IOrderTenantAuthorizer
{
    Task<bool> HasBranchAccessAsync(Guid organizationId, Guid branchId, string permission, string authorizationHeader, CancellationToken cancellationToken);
}
