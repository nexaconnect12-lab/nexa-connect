using NexaConnect.Contracts.Media;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Restaurant.Application.Branches;

namespace NexaConnect.Services.Restaurant.Application.Configuration;

public interface IBranchGalleryRepository
{
    Task<BranchGallery?> GetAsync(Guid org, Guid branch, CancellationToken ct);
    Task<BranchGallery?> SaveAsync(Guid org, Guid branch, UpdateBranchGallery update, string actor, CancellationToken ct);
}
public interface IGalleryAssetValidator
{
    Task<bool> IsReadyAsync(Guid org, Guid asset, string bearer, CancellationToken ct);
}
public sealed class BranchGalleryService(IBranchGalleryRepository repository, IBranchCustomerAuthorizer authorizer, IGalleryAssetValidator assets)
{
    public async Task<BranchGallery?> GetAsync(Guid org, Guid branch, string bearer, CancellationToken ct)
    {
        await Require(org, branch, ProductPermissions.CatalogMenuRead, bearer, ct);
        return await repository.GetAsync(org, branch, ct);
    }
    public async Task<BranchGallery?> SaveAsync(Guid org, Guid branch, UpdateBranchGallery update, string actor, string bearer, CancellationToken ct)
    {
        await Require(org, branch, ProductPermissions.RestaurantConfigurationManage, bearer, ct);
        Validate(update);
        if (string.IsNullOrWhiteSpace(actor)) throw new ArgumentException("Actor required.");
        foreach (var id in update.Staff.Concat(update.Customer).Select(s => s.AssetId).Distinct())
            if (!await assets.IsReadyAsync(org, id, bearer, ct)) throw new ArgumentException("Select ready, processed Media assets from this organization.");
        return await repository.SaveAsync(org, branch, update, actor, ct);
    }
    public static void Validate(UpdateBranchGallery update)
    {
        if (update.ExpectedVersion < 1 || update.Staff is null || update.Customer is null
            || update.Staff.Length > 6 || update.Customer.Length > 6 || update.SecondsPerSlide is < 5 or > 60)
            throw new ArgumentException("Use at most six slides per screen and a 5–60 second interval with the current version.");
        foreach (var list in new[] { update.Staff, update.Customer })
        {
            if (list.Any(s => s is null || s.AssetId == Guid.Empty || s.Title is null || s.Caption is null
                || s.Title.Length > 80 || s.Caption.Length > 160 || s.Title.Any(char.IsControl) || s.Caption.Any(char.IsControl))
                || list.Select(s => s.AssetId).Distinct().Count() != list.Length)
                throw new ArgumentException("Slides require unique asset IDs and bounded plain text.");
        }
    }
    private async Task Require(Guid org, Guid branch, string permission, string bearer, CancellationToken ct)
    {
        if (org == Guid.Empty || branch == Guid.Empty || !await authorizer.IsGrantedAsync(org, null, branch, permission, bearer, ct))
            throw new UnauthorizedAccessException();
    }
}
