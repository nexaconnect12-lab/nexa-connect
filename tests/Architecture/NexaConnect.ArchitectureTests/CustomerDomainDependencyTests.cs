using NexaConnect.Services.Customer.Domain;

namespace NexaConnect.ArchitectureTests;

public sealed class CustomerDomainDependencyTests
{
    [Fact]
    public void Customer_domain_public_contracts_do_not_depend_on_application_or_infrastructure_types()
    {
        Type[] domainTypes = [typeof(CustomerProfileAggregate), typeof(CustomerIdempotencyConflictException)];

        Assert.DoesNotContain(domainTypes.SelectMany(ReferencedTypes), type =>
            type.Namespace?.StartsWith("NexaConnect.Services.Customer.Application", StringComparison.Ordinal) == true
            || type.Namespace?.StartsWith("NexaConnect.Services.Customer.Infrastructure", StringComparison.Ordinal) == true);
    }

    private static IEnumerable<Type> ReferencedTypes(Type type) =>
        type.GetMethods().SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType)
            .Append(method.ReturnType))
            .Concat(type.GetProperties().Select(property => property.PropertyType));
}
