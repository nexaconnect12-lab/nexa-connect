namespace NexaConnect.Services.Media.Application;

public sealed record MediaAssetSummary(Guid Id,string OwnerService,string OwnerType,Guid OwnerId,string OriginalFileName,string ContentType,long SizeBytes,string ProcessingStatus,DateTimeOffset UploadedAtUtc,DateTimeOffset? ProcessedAtUtc,long ConcurrencyVersion);
public sealed record MediaVariantSummary(string Name,string ContentType,long SizeBytes,int WidthPixels,int HeightPixels,string Status);
public interface IMediaAssetRepository { Task<IReadOnlyCollection<MediaAssetSummary>> ListAsync(Guid organizationId,CancellationToken cancellationToken); Task<IReadOnlyCollection<MediaVariantSummary>> ListVariantsAsync(Guid organizationId,Guid assetId,CancellationToken cancellationToken); }
public sealed class MediaAssetQueries(IMediaAssetRepository repository)
{
 public Task<IReadOnlyCollection<MediaAssetSummary>> ListAsync(Guid organizationId,CancellationToken cancellationToken){if(organizationId==Guid.Empty)throw new ArgumentException("Organization identifier is required.");return repository.ListAsync(organizationId,cancellationToken);}
 public Task<IReadOnlyCollection<MediaVariantSummary>> ListVariantsAsync(Guid organizationId,Guid assetId,CancellationToken cancellationToken){if(organizationId==Guid.Empty||assetId==Guid.Empty)throw new ArgumentException("Organization and asset identifiers are required.");return repository.ListVariantsAsync(organizationId,assetId,cancellationToken);}
}
