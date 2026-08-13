namespace NexaConnect.Services.Media.Application;

public sealed record MediaAssetSummary(Guid Id,string OwnerService,string OwnerType,Guid OwnerId,string OriginalFileName,string ContentType,long SizeBytes,string ProcessingStatus,DateTimeOffset UploadedAtUtc,DateTimeOffset? ProcessedAtUtc,long ConcurrencyVersion);
public interface IMediaAssetRepository { Task<IReadOnlyCollection<MediaAssetSummary>> ListAsync(Guid organizationId,CancellationToken cancellationToken); }
public sealed class MediaAssetQueries(IMediaAssetRepository repository)
{
 public Task<IReadOnlyCollection<MediaAssetSummary>> ListAsync(Guid organizationId,CancellationToken cancellationToken){if(organizationId==Guid.Empty)throw new ArgumentException("Organization identifier is required.");return repository.ListAsync(organizationId,cancellationToken);}
}
