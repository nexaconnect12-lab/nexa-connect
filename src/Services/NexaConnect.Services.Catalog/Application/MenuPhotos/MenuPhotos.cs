using NexaConnect.Contracts.Catalog;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Catalog.Application.Menu;
using NexaConnect.Services.Catalog.Application.Tenant;
using NexaConnect.Services.Catalog.Domain.Photos;

namespace NexaConnect.Services.Catalog.Application.MenuPhotos;

public interface IMenuPhotoRepository
{
    Task<MenuPhotoSelection?> ReadAsync(Guid org, Guid branch, Guid product, CancellationToken ct);
    Task<MenuPhotoSelection> ChangeAsync(Guid org, Guid branch, Guid product, Func<MenuPhotoSelection, MenuPhotoChange> decide, MenuMutationContext actor, CancellationToken ct);
}
public interface IMenuPhotoAssetReader { Task<byte[]?> ReadAsync(Guid org, Guid asset, CancellationToken ct); }
public sealed class MenuPhotos(IMenuPhotoRepository repository, IMenuPhotoAssetReader assets, ICatalogTenantAuthorizer authorizer)
{
    private async Task Require(Guid org, Guid branch, string permission, string bearer, CancellationToken ct)
    {
        if (org == Guid.Empty || branch == Guid.Empty || !await authorizer.HasBranchAccessAsync(org, branch, permission, bearer, ct)) throw new UnauthorizedAccessException();
    }
    public async Task<MenuPhotoSelection> ReadAsync(Guid org, Guid branch, Guid product, string bearer, CancellationToken ct)
    {
        await Require(org, branch, ProductPermissions.CatalogMenuRead, bearer, ct);
        var selection = await repository.ReadAsync(org, branch, product, ct) ?? throw new KeyNotFoundException();
        await Require(org, branch, ProductPermissions.CatalogMenuRead, bearer, ct);
        return selection;
    }
    public async Task<MenuPhotoSelection> SetAsync(Guid org, Guid branch, Guid product, SetMenuPhoto input, string bearer, MenuMutationContext actor, CancellationToken ct)
    {
        await Require(org, branch, ProductPermissions.CatalogMenuWrite, bearer, ct);
        if (product == Guid.Empty || actor.CorrelationId == Guid.Empty || string.IsNullOrWhiteSpace(actor.ActorSubjectId)
            || actor.ActorSubjectId.Length > 256 || actor.ActorSubjectId.Any(char.IsControl) || input.ExpectedVersion < 0 || !input.Approved || input.AssetId == Guid.Empty) throw new ArgumentException();
        _ = await repository.ReadAsync(org, branch, product, ct) ?? throw new KeyNotFoundException();
        if (input.AssetId is Guid asset) {
            if(!await authorizer.HasAccessAsync(org,ProductPermissions.MediaAssetRead,bearer,ct))throw new UnauthorizedAccessException();
            if(await assets.ReadAsync(org, asset, ct) is null)throw new ArgumentException("Choose a ready processed product image.");
            if(!await authorizer.HasAccessAsync(org,ProductPermissions.MediaAssetRead,bearer,ct))throw new UnauthorizedAccessException();
        }
        await Require(org, branch, ProductPermissions.CatalogMenuWrite, bearer, ct);
        return await repository.ChangeAsync(org, branch, product, current => MenuPhotoAssignment.Change(current.AssetId, current.Version, input.AssetId, input.ExpectedVersion, input.Approved), actor, ct);
    }
    public async Task<byte[]?> PreviewAsync(Guid org,Guid branch,Guid product,Guid asset,string bearer,CancellationToken ct)
    {
        await Require(org,branch,ProductPermissions.CatalogMenuWrite,bearer,ct);
        if(asset==Guid.Empty||!await authorizer.HasAccessAsync(org,ProductPermissions.MediaAssetRead,bearer,ct))throw new UnauthorizedAccessException();
        _=await repository.ReadAsync(org,branch,product,ct)??throw new KeyNotFoundException();
        var png=await assets.ReadAsync(org,asset,ct);
        await Require(org,branch,ProductPermissions.CatalogMenuWrite,bearer,ct);
        if(!await authorizer.HasAccessAsync(org,ProductPermissions.MediaAssetRead,bearer,ct))throw new UnauthorizedAccessException();
        return png;
    }
    public async Task<byte[]?> DeliverAsync(Guid org, Guid branch, Guid product, string? bearer, CancellationToken ct)
    {
        if (org == Guid.Empty || branch == Guid.Empty || product == Guid.Empty) throw new ArgumentException();
        if (bearer is not null) await Require(org, branch, ProductPermissions.CatalogMenuRead, bearer, ct);
        var selection = await repository.ReadAsync(org, branch, product, ct) ?? throw new KeyNotFoundException();
        var bytes = selection.AssetId is Guid asset ? await assets.ReadAsync(org, asset, ct) : null;
        var latest = await repository.ReadAsync(org, branch, product, ct);
        if (bearer is not null) await Require(org, branch, ProductPermissions.CatalogMenuRead, bearer, ct);
        if (latest != selection) return null;
        if (bytes is { Length: > 524288 }) throw new InvalidDataException();
        return bytes;
    }
}
