using NexaConnect.Services.Catalog.Domain.Organization;
namespace NexaConnect.ArchitectureTests;
public sealed class MenuOrganizationDependencyTests
{
    [Fact]public void Menu_layout_domain_public_contracts_use_only_domain_and_system_types()
    {
        var types=new[]{typeof(BranchMenuOrganizationState),typeof(Category),typeof(Placement),typeof(MenuOrganizationConflictException),typeof(NexaConnect.Services.Catalog.Domain.Availability.MenuAvailabilityState),typeof(NexaConnect.Services.Catalog.Domain.Availability.MenuAvailabilityConflictException),typeof(NexaConnect.Services.Catalog.Domain.Details.MenuDetailsState),typeof(NexaConnect.Services.Catalog.Domain.Details.MenuDetailsConflictException)};
        var references=types.SelectMany(t=>t.GetMethods().SelectMany(m=>m.GetParameters().Select(p=>p.ParameterType).Append(m.ReturnType))
            .Concat(t.GetProperties().Select(p=>p.PropertyType)).Concat(t.GetConstructors().SelectMany(c=>c.GetParameters().Select(p=>p.ParameterType))));
        var visited=new HashSet<Type>();foreach(var type in references)Check(type,visited);
    }
    private static void Check(Type type,HashSet<Type> visited)
    {
        if(!visited.Add(type))return;
        Assert.True(type.Namespace is null||type.Namespace.StartsWith("System",StringComparison.Ordinal)||type.Namespace==typeof(BranchMenuOrganizationState).Namespace||type.Namespace==typeof(NexaConnect.Services.Catalog.Domain.Availability.MenuAvailabilityState).Namespace||type.Namespace==typeof(NexaConnect.Services.Catalog.Domain.Details.MenuDetailsState).Namespace,$"Domain contract references {type.FullName}");
        foreach(var argument in type.GetGenericArguments())Check(argument,visited);
        if(type.HasElementType)Check(type.GetElementType()!,visited);
    }
}
