namespace NexaConnect.Contracts.Catalog;

public sealed record MenuPhotoSelection(Guid OrganizationId, Guid BranchId, Guid ProductId, Guid? AssetId, long Version);
public sealed record SetMenuPhoto(Guid? AssetId, long ExpectedVersion, bool Approved);
