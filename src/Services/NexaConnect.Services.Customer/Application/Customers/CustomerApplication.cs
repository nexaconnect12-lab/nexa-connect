namespace NexaConnect.Services.Customer.Application.Customers;

using NexaConnect.Services.Customer.Domain;
using NexaConnect.Services.Customer.Application.Tenant;
using NexaConnect.Contracts.Platform;

public sealed record CreateCustomer(Guid OrganizationId, string CustomerNumber, string DisplayName, string? IdentitySubjectId);
public sealed record CustomerMutationContext(string ActorSubjectId, Guid CorrelationId, string RequestCorrelationId);
public sealed record CustomerRequestContext(Guid OrganizationId, string ApplicationCode, string AuthorizationHeader,
    string ActorSubjectId, Guid CorrelationId, string RequestCorrelationId);
public sealed class CustomerAccessDeniedException(string permission) : InvalidOperationException("Customer access is denied.")
{
    public string Permission { get; } = permission;
}
public sealed record CustomerProfile(Guid Id, Guid OrganizationId, string CustomerNumber, string DisplayName,
    string? IdentitySubjectId, string Status, long ConcurrencyVersion, DateTimeOffset CreatedAtUtc)
{
    public static CustomerProfile From(CustomerProfileAggregate aggregate) => new(aggregate.Id, aggregate.OrganizationId,
        aggregate.CustomerNumber, aggregate.DisplayName, aggregate.IdentitySubjectId, aggregate.Status,
        aggregate.ConcurrencyVersion, aggregate.CreatedAtUtc);
}

public interface ICustomers
{
    Task<CustomerProfile> CreateAsync(CustomerProfileAggregate candidate, CustomerMutationContext context,
        CancellationToken cancellationToken);
    Task<CustomerProfile?> GetAsync(Guid organizationId, Guid id, CancellationToken cancellationToken);
}

public sealed class CustomerProfileService(ICustomers customers, ICustomerTenantAuthorizer tenantAuthorizer)
{
    public async Task<CustomerProfile> CreateAsync(CreateCustomer command, CustomerRequestContext context,
        CancellationToken cancellationToken)
    {
        await RequireAccessAsync(command.OrganizationId, ProductPermissions.CustomerProfileCreate, context,
            cancellationToken);
        CustomerProfileAggregate candidate = CustomerProfileAggregate.Create(command.OrganizationId,
            command.CustomerNumber, command.DisplayName, command.IdentitySubjectId, DateTimeOffset.UtcNow);
        return await customers.CreateAsync(candidate, MutationContext(context), cancellationToken);
    }

    public async Task<CustomerProfile?> GetAsync(Guid organizationId, Guid id, CustomerRequestContext context,
        CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty || id == Guid.Empty)
            throw new ArgumentException("Organization and customer profile are required.");
        await RequireAccessAsync(organizationId, ProductPermissions.CustomerProfileRead, context, cancellationToken);
        return await customers.GetAsync(organizationId, id, cancellationToken);
    }

    private async Task RequireAccessAsync(Guid organizationId, string permission, CustomerRequestContext context,
        CancellationToken cancellationToken)
    {
        bool valid = context.OrganizationId == organizationId
            && organizationId != Guid.Empty
            && string.Equals(context.ApplicationCode, "nexa_connect", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(context.AuthorizationHeader)
            && !string.IsNullOrWhiteSpace(context.ActorSubjectId)
            && context.CorrelationId != Guid.Empty
            && !string.IsNullOrWhiteSpace(context.RequestCorrelationId)
            && await tenantAuthorizer.HasOrganizationAccessAsync(organizationId, permission,
                context.AuthorizationHeader, cancellationToken);
        if (!valid) throw new CustomerAccessDeniedException(permission);
    }

    private static CustomerMutationContext MutationContext(CustomerRequestContext context) =>
        new(context.ActorSubjectId, context.CorrelationId, context.RequestCorrelationId);
}
