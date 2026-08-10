namespace NexaConnect.Services.Order.Application.Tenant;

public interface IOrderTenantAuthorizer
{
    Task<bool> HasBranchAccessAsync(Guid organizationId, Guid branchId, string authorizationHeader, CancellationToken cancellationToken);
}
