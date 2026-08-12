namespace NexaConnect.Services.Customer.Application.Tenant;

public interface ICustomerTenantAuthorizer
{
    Task<bool> HasOrganizationAccessAsync(
        Guid organizationId, string permission, string authorizationHeader, CancellationToken cancellationToken);
}
