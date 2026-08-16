using NexaConnect.Services.Customer.Application.Customers;
using NexaConnect.Services.Customer.Domain;

namespace NexaConnect.Services.Customer.Infrastructure;

public sealed class InMemoryCustomers : ICustomers
{
    private readonly Dictionary<Guid, CustomerProfile> customers = [];
    private readonly object gate = new();

    public Task<CustomerProfile> CreateAsync(
        CustomerProfileAggregate aggregate,
        CustomerMutationContext context,
        CancellationToken cancellationToken)
    {
        ValidateContext(context);
        CustomerProfile candidate = CustomerProfile.From(aggregate);
        lock (gate)
        {
            CustomerProfile? existing = customers.Values.FirstOrDefault(customer =>
                customer.OrganizationId == candidate.OrganizationId
                && string.Equals(customer.CustomerNumber, candidate.CustomerNumber, StringComparison.Ordinal));
            if (existing is not null)
            {
                EnsureSameRequest(existing, candidate);
                return Task.FromResult(existing);
            }

            if (candidate.IdentitySubjectId is not null && customers.Values.Any(customer =>
                    customer.OrganizationId == candidate.OrganizationId
                    && string.Equals(customer.IdentitySubjectId, candidate.IdentitySubjectId, StringComparison.Ordinal)))
                throw new CustomerIdempotencyConflictException(
                    "The identity subject is already associated with a different customer profile.");

            customers.Add(candidate.Id, candidate);
            return Task.FromResult(candidate);
        }
    }

    public Task<CustomerProfile?> GetAsync(Guid organizationId, Guid id, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(customers.TryGetValue(id, out CustomerProfile? customer)
                && customer.OrganizationId == organizationId && customer.Status != "anonymized" ? customer : null);
        }
    }

    internal static void ValidateContext(CustomerMutationContext context)
    {
        if (context is null || string.IsNullOrWhiteSpace(context.ActorSubjectId) || context.ActorSubjectId.Length > 200
            || context.ActorSubjectId.Any(char.IsControl) || context.CorrelationId == Guid.Empty
            || string.IsNullOrWhiteSpace(context.RequestCorrelationId) || context.RequestCorrelationId.Length > 128
            || context.RequestCorrelationId.Any(char.IsControl))
            throw new ArgumentException("A bounded mutation actor and correlation identifier are required.");
    }

    internal static void EnsureSameRequest(CustomerProfile existing, CustomerProfile candidate)
    {
        if (!string.Equals(existing.DisplayName, candidate.DisplayName, StringComparison.Ordinal)
            || !string.Equals(existing.IdentitySubjectId, candidate.IdentitySubjectId, StringComparison.Ordinal))
            throw new CustomerIdempotencyConflictException(
                "The customer number is already associated with a different profile request.");
    }
}
