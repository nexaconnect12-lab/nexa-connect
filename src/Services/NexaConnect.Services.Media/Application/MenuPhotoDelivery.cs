namespace NexaConnect.Services.Media.Application;
public interface IMenuPhotoEncoder { byte[] Encode(byte[] source); }
public sealed class MenuPhotoDelivery(IMediaManagementRepository repository, IMediaObjectStorage storage, IMenuPhotoEncoder encoder)
{
    public async Task<byte[]?> ReadAsync(Guid org, Guid id, CancellationToken ct)
    {
        var asset = await repository.FindAsync(org,id,ct);
        if(asset?.Asset is not {ProcessingStatus:"ready",OwnerService:"catalog",OwnerType:"product"})return null;
        var key=await repository.FindVariantKeyAsync(org,id,"thumbnail",ct);if(key is null)return null;
        var png=encoder.Encode(await storage.ReadAsync(key,2_097_152,ct));
        var current=await repository.FindAsync(org,id,ct);
        return current?.Asset is {ProcessingStatus:"ready",OwnerService:"catalog",OwnerType:"product"}
            &&current.Value.Asset.ConcurrencyVersion==asset.Value.Asset.ConcurrencyVersion
            &&await repository.FindVariantKeyAsync(org,id,"thumbnail",ct)==key ? png:null;
    }
}
