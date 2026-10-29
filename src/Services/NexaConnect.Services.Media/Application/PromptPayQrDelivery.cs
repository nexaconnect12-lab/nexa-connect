using System.Security.Cryptography;
using NexaConnect.Contracts.Media;

namespace NexaConnect.Services.Media.Application;

public interface IBranchPromptPayQrSelectionPort {Task<BranchPromptPayQrV1> ReadAsync(Guid org,Guid branch,string bearer,CancellationToken ct,bool manage=false);}
public interface IPromptPayPngEncoder {byte[] Normalize(byte[] png);}
public sealed class PromptPayQrDelivery(IBranchPromptPayQrSelectionPort selections,IMediaManagementRepository repository,IMediaObjectStorage storage,IPromptPayPngEncoder encoder,TimeProvider clock)
{
    public async Task<byte[]?> ReadyPngAsync(Guid org,Guid branch,Guid asset,CancellationToken ct)
    {
        var found=await repository.FindAsync(org,asset,ct);
        if(found is null||!Ready(found.Value.Asset,branch))return null;
        var bytes=await storage.ReadAsync(found.Value.Key,2_097_152,ct);
        if(bytes.Length!=found.Value.Asset.SizeBytes||!Convert.ToHexString(SHA256.HashData(bytes)).Equals(found.Value.Checksum,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException();
        byte[] png;try{png=encoder.Normalize(bytes);}catch(InvalidDataException){return null;}
        var current=await repository.FindAsync(org,asset,ct);
        return current is not null&&Ready(current.Value.Asset,branch)&&current.Value.Asset.ConcurrencyVersion==found.Value.Asset.ConcurrencyVersion?png:null;
    }
    private static bool Ready(MediaAssetSummary asset,Guid branch)=>asset is {ProcessingStatus:"ready",OwnerService:"restaurant",OwnerType:"promptpay_qr",ContentType:"image/png",SizeBytes:>0 and <=2_097_152}&&asset.OwnerId==branch;
    public async Task<PromptPayQrBundleV1> DeliverAsync(Guid org,Guid branch,string bearer,CancellationToken ct,bool manage=false)
    {
        var selection=await selections.ReadAsync(org,branch,bearer,ct,manage);
        if(selection.OrganizationId!=org||selection.BranchId!=branch||selection.RestaurantId==Guid.Empty||selection.Version<1||selection.AssetId==Guid.Empty)throw new InvalidDataException();
        GalleryImage? image=null;
        if(selection.AssetId is {} asset){var png=await ReadyPngAsync(org,branch,asset,ct);if(png is not null)image=new(asset,Convert.ToHexString(SHA256.HashData(png)),png);}
        // Repeat live selection/authorization after the object read; never lease an image
        // removed/replaced while storage was being read.
        var latest=await selections.ReadAsync(org,branch,bearer,ct,manage);
        if(latest!=selection)throw new InvalidDataException("PromptPay selection changed. Refresh.");
        var now=clock.GetUtcNow();return new(selection,now,now.AddSeconds(60),image);
    }
}
