namespace NexaConnect.Services.Media.Application;

public sealed record StartMediaUploadCommand(string OwnerService, string OwnerType, Guid OwnerId, string FileName, string ContentType, long SizeBytes, string ChecksumSha256);
public sealed record UploadSession(MediaAssetSummary Asset, string UploadUrl, DateTimeOffset ExpiresAtUtc);
public sealed record DownloadSession(string DownloadUrl, DateTimeOffset ExpiresAtUtc);
public sealed record StoredObjectInfo(long SizeBytes, string? ChecksumSha256);
public sealed record MediaSafetyResult(bool Safe, string? RejectionCategory);
public sealed record MediaQuota(long MaximumStoredBytes, int MaximumPendingUploads);
public sealed class MediaLifecycleConflictException(string message) : Exception(message);
public sealed class MediaDependencyException(string message, Exception innerException) : Exception(message, innerException);

public interface IMediaObjectStorage
{
    Task<string> CreateUploadUrlAsync(string key, string type, long size, string checksum, TimeSpan lifetime, CancellationToken cancellationToken);
    Task<string> CreateDownloadUrlAsync(string key, TimeSpan lifetime, CancellationToken cancellationToken);
    Task<StoredObjectInfo?> InspectAsync(string key, CancellationToken cancellationToken);
    Task<byte[]> ReadAsync(string key, long maximumBytes, CancellationToken cancellationToken);
    Task PutAsync(string key, byte[] content, string contentType, string checksumSha256, CancellationToken cancellationToken);
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}

public interface IMediaContentSafety
{
    Task<MediaSafetyResult> InspectAsync(byte[] content, string declaredContentType, CancellationToken cancellationToken);
}

public interface IMediaOwnerValidator
{
    Task<bool> ExistsAsync(Guid organizationId, string ownerService, string ownerType, Guid ownerId, CancellationToken cancellationToken);
}

public interface IMediaManagementRepository
{
    Task<MediaAssetSummary> StartAsync(Guid organizationId, Guid id, string key, StartMediaUploadCommand command, string actor, DateTimeOffset expires, MediaQuota quota, CancellationToken cancellationToken);
    Task<(MediaAssetSummary Asset, string Key, string Checksum)?> FindAsync(Guid organizationId, Guid id, CancellationToken cancellationToken);
    Task<string?> FindVariantKeyAsync(Guid organizationId, Guid id, string variantName, CancellationToken cancellationToken);
    Task<MediaAssetSummary?> CompleteAsync(Guid organizationId, Guid id, long version, string actor, CancellationToken cancellationToken);
    Task<MediaAssetSummary?> QuarantineAsync(Guid organizationId, Guid id, long version, string actor, string category, CancellationToken cancellationToken);
    Task<MediaAssetSummary?> DeleteAsync(Guid organizationId, Guid id, long version, string actor, CancellationToken cancellationToken);
}

public sealed class MediaManagement(IMediaManagementRepository repository, IMediaObjectStorage storage, IMediaOwnerValidator owners, IMediaContentSafety safety, MediaQuota quota)
{
    private static readonly HashSet<string> Types = ["image/jpeg", "image/png", "image/webp"];

    public async Task<UploadSession> StartAsync(Guid organizationId, StartMediaUploadCommand command, string actor, CancellationToken cancellationToken)
    {
        string owner = command.OwnerService?.Trim().ToLowerInvariant() ?? "";
        string kind = command.OwnerType?.Trim().ToLowerInvariant() ?? "";
        string name = Path.GetFileName(command.FileName?.Trim() ?? "");
        string type = command.ContentType?.Trim().ToLowerInvariant() ?? "";
        string hash = command.ChecksumSha256?.Trim().ToLowerInvariant() ?? "";
        if (organizationId == Guid.Empty || command.OwnerId == Guid.Empty || string.IsNullOrWhiteSpace(actor) || owner != "catalog" || kind != "product")
            throw new ArgumentException("Organization, Catalog product owner, and actor are required.");
        if (name.Length is < 1 or > 200 || name.Any(char.IsControl) || !Types.Contains(type) || command.SizeBytes is < 1 or > 10_485_760 || !System.Text.RegularExpressions.Regex.IsMatch(hash, "^[0-9a-f]{64}$"))
            throw new ArgumentException("File name, type, size, or checksum is invalid.");
        if (!await owners.ExistsAsync(organizationId, owner, kind, command.OwnerId, cancellationToken))
            throw new KeyNotFoundException("Catalog product owner was not found in the active organization.");

        Guid id = Guid.NewGuid();
        string key = $"organizations/{organizationId:D}/assets/{id:D}/original";
        DateTimeOffset expires = DateTimeOffset.UtcNow.AddMinutes(10);
        var normalized = command with { OwnerService = owner, OwnerType = kind, FileName = name, ContentType = type, ChecksumSha256 = hash };
        MediaAssetSummary asset = await repository.StartAsync(organizationId, id, key, normalized, actor.Trim(), expires, quota, cancellationToken);
        return new(asset, await storage.CreateUploadUrlAsync(key, type, command.SizeBytes, hash, TimeSpan.FromMinutes(10), cancellationToken), expires);
    }

    public async Task<MediaAssetSummary> CompleteAsync(Guid organizationId, Guid id, long version, string actor, CancellationToken cancellationToken)
    {
        if (version <= 0) throw new ArgumentException("Expected version must be positive.");
        var found = await repository.FindAsync(organizationId, id, cancellationToken) ?? throw new KeyNotFoundException();
        StoredObjectInfo info;
        byte[] content;
        MediaSafetyResult result;
        try
        {
            info = await storage.InspectAsync(found.Key, cancellationToken) ?? throw new MediaLifecycleConflictException("Uploaded object was not found.");
            if (info.SizeBytes != found.Asset.SizeBytes || string.IsNullOrWhiteSpace(info.ChecksumSha256) || !string.Equals(info.ChecksumSha256, found.Checksum, StringComparison.OrdinalIgnoreCase))
                throw new MediaLifecycleConflictException("Uploaded object verification failed.");
            content = await storage.ReadAsync(found.Key, found.Asset.SizeBytes, cancellationToken);
            result = await safety.InspectAsync(content, found.Asset.ContentType, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (MediaLifecycleConflictException) { throw; }
        catch (Exception exception) { throw new MediaDependencyException("Media storage or safety inspection is unavailable.", exception); }
        if (!result.Safe)
        {
            _ = await repository.QuarantineAsync(organizationId, id, version, actor, result.RejectionCategory ?? "unsafe-content", cancellationToken)
                ?? throw new MediaLifecycleConflictException("Media changed concurrently or the upload expired.");
            throw new MediaLifecycleConflictException("Uploaded object was quarantined by content safety validation.");
        }
        return await repository.CompleteAsync(organizationId, id, version, actor, cancellationToken) ?? throw new MediaLifecycleConflictException("Media changed concurrently or the upload expired.");
    }

    public async Task<DownloadSession> DownloadAsync(Guid organizationId, Guid id, CancellationToken cancellationToken)
    {
        var found = await repository.FindAsync(organizationId, id, cancellationToken) ?? throw new KeyNotFoundException();
        if (found.Asset.ProcessingStatus != "ready") throw new MediaLifecycleConflictException("Media is not ready.");
        DateTimeOffset expires = DateTimeOffset.UtcNow.AddMinutes(5);
        return new(await storage.CreateDownloadUrlAsync(found.Key, TimeSpan.FromMinutes(5), cancellationToken), expires);
    }

    public async Task<DownloadSession> DownloadVariantAsync(Guid organizationId, Guid id, string variantName, CancellationToken cancellationToken)
    {
        string name = variantName?.Trim().ToLowerInvariant() ?? ""; if (name is not ("thumbnail" or "display")) throw new ArgumentException("Variant name is invalid.");
        string key = await repository.FindVariantKeyAsync(organizationId, id, name, cancellationToken) ?? throw new KeyNotFoundException();
        DateTimeOffset expires = DateTimeOffset.UtcNow.AddMinutes(5); return new(await storage.CreateDownloadUrlAsync(key, TimeSpan.FromMinutes(5), cancellationToken), expires);
    }

    public async Task<MediaAssetSummary> DeleteAsync(Guid organizationId, Guid id, long version, string actor, CancellationToken cancellationToken)
    {
        if (version <= 0) throw new ArgumentException("Expected version must be positive.");
        _ = await repository.FindAsync(organizationId, id, cancellationToken) ?? throw new KeyNotFoundException();
        return await repository.DeleteAsync(organizationId, id, version, actor, cancellationToken) ?? throw new MediaLifecycleConflictException("Media changed concurrently.");
    }
}
