namespace NexaConnect.Contracts.Media;

public sealed record BranchPromptPayQrV1(Guid OrganizationId,Guid RestaurantId,Guid BranchId,long Version,Guid? AssetId);
public sealed record UpdateBranchPromptPayQrV1(long ExpectedVersion,Guid? AssetId,bool Approved);
public sealed record PromptPayQrBundleV1(BranchPromptPayQrV1 Selection,DateTimeOffset IssuedAtUtc,DateTimeOffset ExpiresAtUtc,GalleryImage? Image);
public sealed record StartPromptPayQrUploadV1(string FileName,string ContentType,long SizeBytes,string ChecksumSha256);
public sealed record CompletePromptPayQrUploadV1(long ExpectedVersion);
public sealed record PromptPayQrUploadSessionV1(Guid OrganizationId,Guid BranchId,Guid AssetId,long Version,string UploadUrl,DateTimeOffset ExpiresAtUtc);
public sealed record PromptPayQrUploadResultV1(Guid OrganizationId,Guid BranchId,Guid AssetId,long Version,string Status);
