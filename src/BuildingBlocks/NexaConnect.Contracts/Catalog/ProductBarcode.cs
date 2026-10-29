namespace NexaConnect.Contracts.Catalog;

public sealed record ProductBarcode(Guid OrganizationId, Guid BranchId, Guid ProductId, string? Barcode, long Version);
public sealed record SetProductBarcode(string? Barcode, long ExpectedVersion);
public sealed record BarcodeLookupRequest(string Barcode);
