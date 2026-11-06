namespace NexaConnect.Services.Media.Application;
public interface IMenuPhotoEncoder { byte[] Encode(byte[] source); }
internal static class ProductPhotoPurpose
{
    internal static bool IsReady(MediaAssetSummary? asset, Guid organization) => asset is { ProcessingStatus: "ready" }
        && (asset is { OwnerService: "catalog", OwnerType: "product" }
            || asset is { OwnerService: "media", OwnerType: "photo_library" } && asset.OwnerId == organization);
}

public sealed class MenuPhotoDelivery(IMediaManagementRepository repository, IMediaObjectStorage storage, IMenuPhotoEncoder encoder)
{
    public async Task<byte[]?> ReadAsync(Guid org, Guid id, CancellationToken ct)
    {
        var asset = await repository.FindAsync(org,id,ct);
        if(asset is not {} original || !ProductPhotoPurpose.IsReady(original.Asset,org))return null;
        var key=await repository.FindVariantKeyAsync(org,id,"thumbnail",ct);if(key is null)return null;
        var png=encoder.Encode(await storage.ReadAsync(key,2_097_152,ct));
        var current=await repository.FindAsync(org,id,ct);
        return current is {} latest && ProductPhotoPurpose.IsReady(latest.Asset,org)
            &&latest.Asset.ConcurrencyVersion==original.Asset.ConcurrencyVersion
            &&await repository.FindVariantKeyAsync(org,id,"thumbnail",ct)==key ? png:null;
    }
}
