namespace NexaConnect.Contracts.Media;

// Version-one, gallery-only delegation. No caller-selected permission or financial fields.
public sealed record GalleryViewer(string Subject);
public sealed record GalleryMembershipQuery(Guid OrganizationId, string Subject);
public sealed record GalleryPermissionQuery(Guid OrganizationId, Guid RestaurantId, Guid BranchId, string Subject);
public sealed record GalleryAccess(bool Granted);
public sealed record GalleryReaderCredential(string Credential, Guid OrganizationId, Guid BranchId,
    DateTimeOffset IssuedAtUtc, DateTimeOffset ExpiresAtUtc);
