namespace NexaConnect.Services.Customer.Application.Customers;

public sealed record CreateCustomer(Guid OrganizationId, string CustomerNumber, string DisplayName, string? IdentitySubjectId);
public sealed record CustomerProfile(Guid Id, Guid OrganizationId, string CustomerNumber, string DisplayName, string? IdentitySubjectId, string Status);

public interface ICustomers
{
    CustomerProfile Create(CreateCustomer command);
    CustomerProfile? Get(Guid organizationId, Guid id);
}
