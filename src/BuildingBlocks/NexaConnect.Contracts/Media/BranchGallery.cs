namespace NexaConnect.Contracts.Media;

// Version-one HTTP presentation contracts, not shared domain entities.
public sealed record GallerySlide(Guid AssetId, string Title = "", string Caption = "");
public sealed record BranchGallery(Guid OrganizationId, Guid BranchId, long Version,
    GallerySlide[] Staff, GallerySlide[] Customer, int SecondsPerSlide = 10);
public sealed record UpdateBranchGallery(long ExpectedVersion, GallerySlide[] Staff,
    GallerySlide[] Customer, int SecondsPerSlide = 10);
public sealed record GalleryImage(Guid AssetId, string Sha256, byte[] Png);
public sealed record GalleryBundle(BranchGallery Selection, DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc, GalleryImage[] Images);
