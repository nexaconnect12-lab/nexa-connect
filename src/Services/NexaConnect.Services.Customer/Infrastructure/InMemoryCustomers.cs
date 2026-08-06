using System.Collections.Concurrent;
using NexaConnect.Services.Customer.Application.Customers;

namespace NexaConnect.Services.Customer.Infrastructure;

public sealed class InMemoryCustomers : ICustomers
{
    private readonly ConcurrentDictionary<Guid, CustomerProfile> customers = new();

    public CustomerProfile Create(CreateCustomer command)
    {
        if (command.OrganizationId == Guid.Empty || string.IsNullOrWhiteSpace(command.CustomerNumber) || string.IsNullOrWhiteSpace(command.DisplayName))
            throw new ArgumentException("Organization, customer number, and display name are required.");
        var customer = new CustomerProfile(Guid.NewGuid(), command.OrganizationId, command.CustomerNumber.Trim(), command.DisplayName.Trim(), command.IdentitySubjectId, "active");
        customers[customer.Id] = customer;
        return customer;
    }

    public CustomerProfile? Get(Guid organizationId, Guid id) =>
        customers.TryGetValue(id, out CustomerProfile? customer) && customer.OrganizationId == organizationId ? customer : null;
}
