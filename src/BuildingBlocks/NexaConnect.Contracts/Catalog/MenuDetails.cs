namespace NexaConnect.Contracts.Catalog;

public sealed record EditableMenuItem(Guid ProductId, string Name, decimal UnitPrice, string Currency,
    string PreparationStation, bool Available, long Version);
public sealed record BranchMenuDetails(Guid OrganizationId, Guid BranchId, IReadOnlyList<EditableMenuItem> Items);
public sealed record UpdateMenuDetails(string? Name, decimal? UnitPrice, string? PreparationStation, long? ExpectedVersion);
