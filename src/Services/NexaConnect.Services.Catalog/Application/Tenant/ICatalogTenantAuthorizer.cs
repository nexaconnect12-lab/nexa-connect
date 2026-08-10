namespace NexaConnect.Services.Catalog.Application.Tenant;

public interface ICatalogTenantAuthorizer
{
    Task<bool> HasAccessAsync(Guid organizationId, string authorizationHeader, CancellationToken cancellationToken);
    Task<bool> HasBranchAccessAsync(Guid organizationId, Guid branchId, string authorizationHeader, CancellationToken cancellationToken);
}
