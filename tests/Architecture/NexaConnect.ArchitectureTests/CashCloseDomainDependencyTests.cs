using System.Reflection;
using NexaConnect.Services.Reporting.Domain;
using NexaConnect.Services.POS.Domain.CashReviews;

namespace NexaConnect.ArchitectureTests;

public sealed class CashCloseDomainDependencyTests
{
    [Fact]
    public void Cash_close_domain_models_have_no_framework_or_cross_context_contract_dependencies()
    {
        Type[] models = [typeof(CashCloseSnapshot), typeof(CashClosePublication)];
        var references = models.SelectMany(type =>
            type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .SelectMany(method => method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType))
                .Concat(type.GetConstructors().SelectMany(c => c.GetParameters().Select(p => p.ParameterType)))
                .Concat(type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Select(f => f.FieldType)));
        foreach (var reference in references.SelectMany(Expand))
            Assert.True(reference.Namespace == "System" || models.Contains(reference),
                $"Domain contract depends on {reference.FullName}");
    }

    private static IEnumerable<Type> Expand(Type type) => new[] { type }
        .Concat(type.IsGenericType ? type.GetGenericArguments().SelectMany(Expand) : []);
}
