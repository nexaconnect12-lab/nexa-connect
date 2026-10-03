using System.Reflection;
using NexaConnect.Services.Authorization.Domain;

namespace NexaConnect.ArchitectureTests;

public sealed class AuthorizationDomainDependencyTests
{
    [Fact]
    public void Authorization_policy_has_no_framework_or_cross_context_contract_dependencies()
    {
        Type[] models = [typeof(AuthorizationPolicy), typeof(AuthorizationEvidence)];
        var references = models.SelectMany(type =>
            type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .SelectMany(method => method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType))
                .Concat(type.GetConstructors().SelectMany(c => c.GetParameters().Select(p => p.ParameterType)))
                .Concat(type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Select(f => f.FieldType)));
        foreach (var reference in references.SelectMany(Expand))
            Assert.True(reference.Namespace == "System" || models.Contains(reference),
                $"Authorization domain contract depends on {reference.FullName}");
    }

    private static IEnumerable<Type> Expand(Type type) => new[] { type }
        .Concat(type.IsGenericType ? type.GetGenericArguments().SelectMany(Expand) : []);
}
