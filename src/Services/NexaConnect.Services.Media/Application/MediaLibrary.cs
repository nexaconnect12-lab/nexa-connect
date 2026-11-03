using NexaConnect.Contracts.Platform;

namespace NexaConnect.Services.Media.Application;

public sealed record MediaLibraryQuery(string Search = "", int Page = 1, bool ReadyOnly = false)
{
    public const int PageSize = 24;
    public MediaLibraryQuery Validate()
    {
        string search = Search?.Trim() ?? "";
        if (search.Length > 100 || search.Any(char.IsControl) || Page is < 1 or > 10000) throw new ArgumentException("Invalid photo library query.");
        return this with { Search = search };
    }
}
public sealed record MediaLibraryPage(IReadOnlyList<MediaAssetSummary> Items, bool HasMore, int Page);
public interface IMediaLibraryRepository
{
    Task<MediaLibraryPage> BrowseAsync(Guid organization, MediaLibraryQuery query, CancellationToken ct);
}
public sealed class MediaLibrary(IMediaLibraryRepository repository, MenuPhotoDelivery photos, IMediaCustomerAuthorizer authorizer)
{
    private async Task Require(Guid organization, string bearer, CancellationToken ct)
    {
        if (organization == Guid.Empty || !await authorizer.IsGrantedAsync(organization, ProductPermissions.MediaAssetRead, bearer, ct))
            throw new UnauthorizedAccessException();
    }
    public async Task<MediaLibraryPage> BrowseAsync(Guid organization, MediaLibraryQuery query, string bearer, CancellationToken ct)
    {
        query = query.Validate(); await Require(organization, bearer, ct);
        var page = await repository.BrowseAsync(organization, query, ct);
        await Require(organization, bearer, ct); return page;
    }
    public async Task<byte[]?> ThumbnailAsync(Guid organization, Guid asset, string bearer, CancellationToken ct)
    {
        await Require(organization, bearer, ct);
        if (asset == Guid.Empty) throw new ArgumentException("Photo is required.");
        var png = await photos.ReadAsync(organization, asset, ct);
        if (png?.Length > 524288) throw new InvalidDataException("Thumbnail exceeds its bound.");
        await Require(organization, bearer, ct); return png;
    }
}
