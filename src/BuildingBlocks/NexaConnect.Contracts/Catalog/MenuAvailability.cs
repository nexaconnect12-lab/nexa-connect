namespace NexaConnect.Contracts.Catalog;

public sealed record MenuAvailabilityItem(Guid ProductId, string Name, bool Available, long Version);
public sealed record BranchMenuAvailability(Guid OrganizationId, Guid BranchId, IReadOnlyList<MenuAvailabilityItem> Items);
public sealed record UpdateMenuAvailability(bool? Available, long? ExpectedVersion);
