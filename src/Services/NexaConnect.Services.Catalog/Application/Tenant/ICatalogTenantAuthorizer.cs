namespace NexaConnect.Services.Catalog.Application.Tenant;

public interface ICatalogTenantAuthorizer
{
    Task<bool> HasAccessAsync(Guid organizationId, string permission, string authorizationHeader, CancellationToken cancellationToken);
    Task<bool> HasBranchAccessAsync(Guid organizationId, Guid branchId, string permission, string authorizationHeader, CancellationToken cancellationToken);
}
