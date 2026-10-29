using NexaConnect.Contracts.Media;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Restaurant.Application.Branches;
using NexaConnect.Services.Restaurant.Domain;

namespace NexaConnect.Services.Restaurant.Application.Configuration;

public interface IBranchPromptPayQrRepository
{
    Task<BranchPromptPayQrV1?> GetAsync(Guid org,Guid branch,CancellationToken ct);
    Task<BranchPromptPayQrV1?> SaveAsync(Guid org,Guid branch,PromptPayQrApproval approval,string actor,CancellationToken ct);
}
public interface IPromptPayQrAssetValidator
{
    Task<bool> ReadyAsync(Guid org,Guid branch,Guid asset,string bearer,CancellationToken ct);
}
public sealed class BranchPromptPayQrService(IBranchPromptPayQrRepository repository,IBranchCustomerAuthorizer authorizer,IPromptPayQrAssetValidator assets)
{
    public async Task<bool> OwnerExistsAsync(Guid org,Guid branch,CancellationToken ct)=>org!=Guid.Empty&&branch!=Guid.Empty&&await repository.GetAsync(org,branch,ct) is not null;
    public Task<BranchPromptPayQrV1> ManageAccessAsync(Guid org,Guid branch,string bearer,CancellationToken ct)=>Require(org,branch,ProductPermissions.RestaurantConfigurationManage,bearer,ct);
    public async Task<BranchPromptPayQrV1?> ReadAsync(Guid org,Guid branch,string bearer,CancellationToken ct)
    {
        var selection=await Require(org,branch,ProductPermissions.CatalogMenuRead,bearer,ct);
        return selection;
    }
    public async Task<BranchPromptPayQrV1?> SaveAsync(Guid org,Guid branch,UpdateBranchPromptPayQrV1 update,string actor,string bearer,CancellationToken ct)
    {
        await Require(org,branch,ProductPermissions.RestaurantConfigurationManage,bearer,ct);
        if(string.IsNullOrWhiteSpace(actor))throw new UnauthorizedAccessException();
        var approval=new PromptPayQrApproval(update.ExpectedVersion,update.AssetId,update.Approved);
        if(approval.AssetId is {} id&&!await assets.ReadyAsync(org,branch,id,bearer,ct))
            throw new ArgumentException("Select a safe PNG uploaded for this branch.");
        return await repository.SaveAsync(org,branch,approval,actor,ct);
    }
    private async Task<BranchPromptPayQrV1> Require(Guid org,Guid branch,string permission,string bearer,CancellationToken ct)
    {
        if(org==Guid.Empty||branch==Guid.Empty)throw new UnauthorizedAccessException();
        var selection=await repository.GetAsync(org,branch,ct)??throw new KeyNotFoundException();
        if(!await authorizer.IsGrantedAsync(org,selection.RestaurantId,branch,permission,bearer,ct))throw new UnauthorizedAccessException();
        return selection;
    }
}
