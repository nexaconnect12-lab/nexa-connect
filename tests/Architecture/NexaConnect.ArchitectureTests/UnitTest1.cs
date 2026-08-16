using NexaConnect.Services.Kitchen.Domain;

namespace NexaConnect.ArchitectureTests;

public sealed class KitchenDomainDependencyTests
{
    [Fact]
    public void Kitchen_domain_public_contracts_do_not_depend_on_application_or_infrastructure_types()
    {
        Type[] domainTypes = [typeof(KitchenTicketLifecycle), typeof(KitchenTicketStatus), typeof(KitchenConflictException)];

        Assert.NotEmpty(domainTypes);
        Assert.DoesNotContain(domainTypes.SelectMany(ReferencedTypes), IsForbidden);
    }

    private static IEnumerable<Type> ReferencedTypes(Type type) =>
        type.GetMethods().SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType)
            .Append(method.ReturnType))
            .Concat(type.GetProperties().Select(property => property.PropertyType));

    private static bool IsForbidden(Type type) =>
        type.Namespace?.StartsWith("NexaConnect.Services.Kitchen.Application", StringComparison.Ordinal) == true
        || type.Namespace?.StartsWith("NexaConnect.Services.Kitchen.Infrastructure", StringComparison.Ordinal) == true;
}
