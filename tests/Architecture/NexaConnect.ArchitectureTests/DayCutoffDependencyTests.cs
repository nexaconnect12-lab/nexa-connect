using System.Reflection;
using NexaConnect.Services.POS.Domain.DayClose;

namespace NexaConnect.ArchitectureTests;

public sealed class DayCutoffDependencyTests
{
    [Fact]
    public void Cutoff_domain_models_depend_only_on_their_context_and_system_types()
    {
        Type[] models=[typeof(BranchDayClose),typeof(BranchDayCutoff),typeof(BranchDaySeal),typeof(DaySealEvidence),typeof(DaySealReference),typeof(DayIdentity),typeof(DayTender),typeof(DayEvidence),typeof(CutoffReference),
            typeof(DayCutoffEvidence),typeof(PreparationCommand),typeof(PreparationState)];
        foreach(var model in models)
        {
            var types=model.GetConstructors().SelectMany(x=>x.GetParameters().Select(p=>p.ParameterType))
                .Concat(model.GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly)
                    .SelectMany(x=>x.GetParameters().Select(p=>p.ParameterType).Append(x.ReturnType)));
            foreach(var type in types.SelectMany(Expand))
                Assert.True(type.Namespace?.StartsWith("System",StringComparison.Ordinal)==true || models.Contains(type),$"Cutoff Domain depends on {type.FullName}");
        }
    }
    private static IEnumerable<Type> Expand(Type type)=>type.IsArray||type.IsByRef?Expand(type.GetElementType()!):new[]{type}.Concat(type.IsGenericType?type.GetGenericArguments().SelectMany(Expand):[]);
}
