using NexaConnect.Contracts.Platform;
using NexaConnect.Services.PlatformDirectory.Domain.Support;

namespace NexaConnect.Services.PlatformDirectory.Application.Support;

public interface ISupportElevationRepository
{
    Task CreateAsync(SupportElevation elevation, CancellationToken cancellationToken);
    Task<SupportElevation?> FindAsync(Guid elevationId, CancellationToken cancellationToken);
    Task<SupportElevation?> FindEffectiveAsync(Guid organizationId, string applicationCode, string supportSubjectId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> TryApproveAsync(SupportElevation elevation, CancellationToken cancellationToken);
    Task<bool> TryRevokeAsync(SupportElevation elevation, CancellationToken cancellationToken);
}

public sealed class SupportElevationApplicationService(
    ISupportElevationRepository repository,
    TimeProvider timeProvider)
{
    public async Task<SupportElevationSummary> RequestAsync(
        RequestSupportElevationRequest request,
        string supportSubjectId,
        CancellationToken cancellationToken)
    {
        SupportElevation elevation = SupportElevation.Request(
            Guid.NewGuid(),
            request.OrganizationId,
            request.ApplicationCode,
            supportSubjectId,
            request.Reason,
            request.DurationMinutes,
            timeProvider.GetUtcNow());
        await repository.CreateAsync(elevation, cancellationToken);
        return ToSummary(elevation);
    }

    public async Task<SupportElevationSummary?> GetAsync(Guid elevationId, CancellationToken cancellationToken)
    {
        if (elevationId == Guid.Empty) throw new ArgumentException("Elevation identifier is required.");
        SupportElevation? elevation = await repository.FindAsync(elevationId, cancellationToken);
        return elevation is null ? null : ToSummary(elevation, timeProvider.GetUtcNow());
    }

    public async Task<SupportElevationSummary?> GetEffectiveAsync(
        Guid organizationId,
        string applicationCode,
        string supportSubjectId,
        CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty) throw new ArgumentException("Organization identifier is required.");
        if (string.IsNullOrWhiteSpace(applicationCode)) throw new ArgumentException("Application code is required.");
        if (string.IsNullOrWhiteSpace(supportSubjectId)) throw new ArgumentException("Support subject is required.");
        DateTimeOffset now = timeProvider.GetUtcNow();
        SupportElevation? elevation = await repository.FindEffectiveAsync(
            organizationId, applicationCode.Trim(), supportSubjectId.Trim(), now, cancellationToken);
        return elevation is null ? null : ToSummary(elevation, now);
    }

    public async Task<SupportElevationSummary?> ApproveAsync(
        Guid elevationId,
        string approverSubjectId,
        CancellationToken cancellationToken)
    {
        SupportElevation? elevation = await RequireExistingAsync(elevationId, cancellationToken);
        if (elevation is null) return null;
        elevation.Approve(approverSubjectId, timeProvider.GetUtcNow());
        if (!await repository.TryApproveAsync(elevation, cancellationToken))
            throw new SupportElevationConflictException("The support elevation changed before approval.");
        return ToSummary(elevation);
    }

    public async Task<SupportElevationSummary?> RevokeAsync(
        Guid elevationId,
        string actorSubjectId,
        CancellationToken cancellationToken)
    {
        SupportElevation? elevation = await RequireExistingAsync(elevationId, cancellationToken);
        if (elevation is null) return null;
        elevation.Revoke(actorSubjectId, timeProvider.GetUtcNow());
        if (!await repository.TryRevokeAsync(elevation, cancellationToken))
            throw new SupportElevationConflictException("The support elevation changed before revocation.");
        return ToSummary(elevation);
    }

    private Task<SupportElevation?> RequireExistingAsync(Guid elevationId, CancellationToken cancellationToken)
    {
        if (elevationId == Guid.Empty) throw new ArgumentException("Elevation identifier is required.");
        return repository.FindAsync(elevationId, cancellationToken);
    }

    private static SupportElevationSummary ToSummary(SupportElevation elevation, DateTimeOffset? now = null)
    {
        string status = now.HasValue && elevation.Status == SupportElevationStatus.Active && !elevation.IsEffective(now.Value)
            ? "expired"
            : elevation.Status.ToString().ToLowerInvariant();
        return new SupportElevationSummary(
            elevation.Id, elevation.OrganizationId, elevation.ApplicationCode, elevation.SupportSubjectId,
            elevation.Reason, status, elevation.RequestedAtUtc, elevation.ApprovedAtUtc, elevation.ExpiresAtUtc,
            elevation.RevokedAtUtc, elevation.ApprovedBySubjectId, elevation.RevokedBySubjectId);
    }
}

public sealed class SupportElevationConflictException(string message) : Exception(message);
