using NexaConnect.Contracts.Media;
using NexaConnect.Contracts.Platform;

namespace NexaConnect.Services.Authorization.Application.Decisions;

public sealed class GalleryPermission(IAuthorizationDecisionService decisions)
{
    public async Task<GalleryAccess> CheckAsync(GalleryPermissionQuery query,CancellationToken ct)
    {
        if(query.OrganizationId==Guid.Empty || query.RestaurantId==Guid.Empty || query.BranchId==Guid.Empty
            || string.IsNullOrWhiteSpace(query.Subject) || query.Subject.Length>256 || query.Subject.Any(char.IsControl))return new(false);
        var decision=await decisions.DecideAsync(query.Subject,query.OrganizationId,query.RestaurantId,query.BranchId,
            ProductPermissions.CatalogMenuRead,null,null,ct);
        return new(decision.Granted);
    }
}
