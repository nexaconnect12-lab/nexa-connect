using NexaConnect.Contracts.Media;

namespace NexaConnect.Services.Media.Application;

public interface IBranchGallerySelectionPort
{
    Task<BranchGallery> ReadAsync(Guid org,Guid branch,string bearer,CancellationToken ct);
}
public interface IGalleryImageEncoder { byte[] ToPng(byte[] displayVariant); }

public sealed class BranchGalleryDelivery(IBranchGallerySelectionPort selections, IMediaManagementRepository repository,
    IMediaObjectStorage storage, IGalleryImageEncoder encoder,TimeProvider clock)
{
    public async Task<bool> ReadyAsync(Guid org,Guid id,CancellationToken ct)
    {
        var asset=await repository.FindAsync(org,id,ct);
        return asset?.Asset is {ProcessingStatus:"ready",OwnerService:"catalog",OwnerType:"product"}
            && await repository.FindVariantKeyAsync(org,id,"display",ct) is not null;
    }
    public async Task<GalleryBundle> DeliverAsync(Guid org,Guid branch,string bearer,CancellationToken ct)
    {
        // Restaurant repeats live membership/branch/menu-read authorization. Caller cannot supply selection IDs.
        var selection=await selections.ReadAsync(org,branch,bearer,ct);
        return await DeliverSelectionAsync(org,branch,selection,ct);
    }
    public async Task<GalleryBundle> DeliverSelectionAsync(Guid org,Guid branch,BranchGallery selection,CancellationToken ct)
    {
        if(selection.OrganizationId!=org||selection.BranchId!=branch||selection.Staff is null||selection.Customer is null
            ||selection.Staff.Length>6||selection.Customer.Length>6||selection.SecondsPerSlide is <5 or >60)
            throw new InvalidDataException("Invalid gallery scope.");
        var images=new List<GalleryImage>();long bytes=0;
        foreach(var id in selection.Staff.Concat(selection.Customer).Select(s=>s.AssetId).Distinct())
        {
            if(!await ReadyAsync(org,id,ct))continue;
            var key=await repository.FindVariantKeyAsync(org,id,"display",ct);
            if(key is null)continue;
            var png=encoder.ToPng(await storage.ReadAsync(key,2_097_152,ct));
            if(png.Length>2_097_152||(bytes+=png.Length)>16_777_216)throw new InvalidDataException("Gallery size exceeded.");
            // A deletion during the provider read must not renew that image's lease.
            if(!await ReadyAsync(org,id,ct))continue;
            images.Add(new(id,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(png)),png));
        }
        var allowed=images.Select(i=>i.AssetId).ToHashSet();
        selection=selection with {Staff=selection.Staff.Where(s=>allowed.Contains(s.AssetId)).ToArray(),Customer=selection.Customer.Where(s=>allowed.Contains(s.AssetId)).ToArray()};
        var now=clock.GetUtcNow();return new(selection,now,now.AddMinutes(5),images.ToArray());
    }
}
