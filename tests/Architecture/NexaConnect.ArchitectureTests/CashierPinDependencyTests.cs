using System.Reflection;
using NexaConnect.Services.POS.Domain.CashierPins;

namespace NexaConnect.ArchitectureTests;

public sealed class CashierPinDependencyTests
{
    [Fact]
    public void Credential_model_remains_independent_of_transport_persistence_and_cryptographic_frameworks()
    {
        var model=typeof(CashierPinCredential);
        var references=model.GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly)
            .SelectMany(m=>m.GetParameters().Select(p=>p.ParameterType).Append(m.ReturnType))
            .Concat(model.GetFields(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Select(f=>f.FieldType));
        foreach(var type in references.SelectMany(Expand)) Assert.True(type==model||type.Namespace=="System",type.FullName);
    }
    private static IEnumerable<Type> Expand(Type type)=>new[]{type}.Concat(type.IsGenericType?type.GetGenericArguments().SelectMany(Expand):[]);
}
