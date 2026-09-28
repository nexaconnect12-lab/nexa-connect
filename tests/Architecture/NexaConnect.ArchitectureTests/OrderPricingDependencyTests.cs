using System.Reflection;
using NexaConnect.Services.Order.Domain;
namespace NexaConnect.ArchitectureTests;
public sealed class OrderPricingDependencyTests
{
    [Fact]
    public void Order_pricing_domain_has_no_external_framework_or_contract_dependencies()
    {
        Type[] models = [typeof(OrderAggregate), typeof(OrderLine), typeof(OrderPricing), typeof(PricingPolicy), typeof(ManualTenderSettlement), typeof(OrderStatus)];
        var references = models.SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType))
            .Concat(type.GetConstructors().SelectMany(c => c.GetParameters().Select(p => p.ParameterType))));
        foreach (var reference in references.SelectMany(Expand))
            Assert.True(reference.Namespace?.StartsWith("System", StringComparison.Ordinal) == true || reference.Namespace == typeof(OrderAggregate).Namespace,
                $"Order domain depends on {reference.FullName}");
    }
    private static IEnumerable<Type> Expand(Type type) => new[] { type }.Concat(type.IsGenericType ? type.GetGenericArguments().SelectMany(Expand) : []);
}
