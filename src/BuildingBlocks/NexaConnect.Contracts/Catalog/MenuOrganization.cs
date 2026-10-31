using System.Text.Json.Serialization;
namespace NexaConnect.Contracts.Catalog;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MenuCategory(Guid Id,string Name);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MenuPlacement(Guid ProductId,Guid? CategoryId);
public sealed record BranchMenuOrganization(Guid OrganizationId,Guid BranchId,long Version,IReadOnlyList<MenuCategory> Categories,IReadOnlyList<MenuPlacement> Items);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateMenuOrganization(long ExpectedVersion,IReadOnlyList<MenuCategory> Categories,IReadOnlyList<MenuPlacement> Items);
