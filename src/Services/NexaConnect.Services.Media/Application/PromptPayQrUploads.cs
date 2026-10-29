using NexaConnect.Contracts.Media;
namespace NexaConnect.Services.Media.Application;

public sealed class PromptPayQrUploads(IBranchPromptPayQrSelectionPort selection,MediaManagement media,IMediaManagementRepository repository)
{
    public async Task<UploadSession> StartAsync(Guid org,Guid branch,StartPromptPayQrUploadV1 input,string actor,string bearer,CancellationToken ct)
    {
        await Require(org,branch,bearer,ct);
        return await media.StartPromptPayAsync(org,branch,input,actor,ct);
    }
    public async Task<MediaAssetSummary> CompleteAsync(Guid org,Guid branch,Guid asset,long version,string actor,string bearer,CancellationToken ct)
    {
        await Require(org,branch,bearer,ct);
        var found=await repository.FindAsync(org,asset,ct)??throw new KeyNotFoundException();
        if(found.Asset.OwnerService!="restaurant"||found.Asset.OwnerType!="promptpay_qr"||found.Asset.OwnerId!=branch)throw new KeyNotFoundException();
        return await media.CompletePromptPayAsync(org,asset,version,actor,ct);
    }
    public async Task Require(Guid org,Guid branch,string bearer,CancellationToken ct)
    {
        if(org==Guid.Empty||branch==Guid.Empty)throw new UnauthorizedAccessException();
        var value=await selection.ReadAsync(org,branch,bearer,ct,true);
        if(value.OrganizationId!=org||value.BranchId!=branch||value.RestaurantId==Guid.Empty)throw new UnauthorizedAccessException();
    }
}
