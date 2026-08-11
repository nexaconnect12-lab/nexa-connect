namespace NexaConnect.Services.PlatformDirectory.Domain.Support;

public enum SupportElevationStatus
{
    Pending,
    Active,
    Revoked
}

public sealed class SupportElevation
{
    private SupportElevation(
        Guid id,
        Guid organizationId,
        string applicationCode,
        string supportSubjectId,
        string reason,
        int durationMinutes,
        SupportElevationStatus status,
        DateTimeOffset requestedAtUtc,
        DateTimeOffset? approvedAtUtc,
        DateTimeOffset? expiresAtUtc,
        DateTimeOffset? revokedAtUtc,
        string? approvedBySubjectId,
        string? revokedBySubjectId)
    {
        Id = id;
        OrganizationId = organizationId;
        ApplicationCode = applicationCode;
        SupportSubjectId = supportSubjectId;
        Reason = reason;
        DurationMinutes = durationMinutes;
        Status = status;
        RequestedAtUtc = requestedAtUtc;
        ApprovedAtUtc = approvedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        RevokedAtUtc = revokedAtUtc;
        ApprovedBySubjectId = approvedBySubjectId;
        RevokedBySubjectId = revokedBySubjectId;
    }

    public Guid Id { get; }
    public Guid OrganizationId { get; }
    public string ApplicationCode { get; }
    public string SupportSubjectId { get; }
    public string Reason { get; }
    public int DurationMinutes { get; }
    public SupportElevationStatus Status { get; private set; }
    public DateTimeOffset RequestedAtUtc { get; }
    public DateTimeOffset? ApprovedAtUtc { get; private set; }
    public DateTimeOffset? ExpiresAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public string? ApprovedBySubjectId { get; private set; }
    public string? RevokedBySubjectId { get; private set; }

    public static SupportElevation Request(
        Guid id,
        Guid organizationId,
        string applicationCode,
        string supportSubjectId,
        string reason,
        int durationMinutes,
        DateTimeOffset requestedAtUtc)
    {
        if (id == Guid.Empty || organizationId == Guid.Empty) throw new ArgumentException("Elevation and organization identifiers are required.");
        if (string.IsNullOrWhiteSpace(applicationCode)) throw new ArgumentException("Application code is required.");
        if (string.IsNullOrWhiteSpace(supportSubjectId)) throw new ArgumentException("Support subject is required.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 10) throw new ArgumentException("A support reason of at least 10 characters is required.");
        if (durationMinutes is < 5 or > 240) throw new ArgumentOutOfRangeException(nameof(durationMinutes), "Support elevation must last between 5 and 240 minutes.");
        return new SupportElevation(id, organizationId, applicationCode.Trim(), supportSubjectId.Trim(), reason.Trim(), durationMinutes,
            SupportElevationStatus.Pending, requestedAtUtc, null, null, null, null, null);
    }

    public static SupportElevation Rehydrate(
        Guid id, Guid organizationId, string applicationCode, string supportSubjectId, string reason,
        int durationMinutes, SupportElevationStatus status, DateTimeOffset requestedAtUtc,
        DateTimeOffset? approvedAtUtc, DateTimeOffset? expiresAtUtc, DateTimeOffset? revokedAtUtc,
        string? approvedBySubjectId, string? revokedBySubjectId) =>
        new(id, organizationId, applicationCode, supportSubjectId, reason, durationMinutes, status,
            requestedAtUtc, approvedAtUtc, expiresAtUtc, revokedAtUtc, approvedBySubjectId, revokedBySubjectId);

    public void Approve(string approverSubjectId, DateTimeOffset approvedAtUtc)
    {
        if (Status != SupportElevationStatus.Pending) throw new InvalidOperationException("Only a pending elevation can be approved.");
        if (string.IsNullOrWhiteSpace(approverSubjectId)) throw new ArgumentException("Approver subject is required.");
        if (string.Equals(SupportSubjectId, approverSubjectId.Trim(), StringComparison.Ordinal))
            throw new InvalidOperationException("A support operator cannot approve their own elevation.");
        Status = SupportElevationStatus.Active;
        ApprovedBySubjectId = approverSubjectId.Trim();
        ApprovedAtUtc = approvedAtUtc;
        ExpiresAtUtc = approvedAtUtc.AddMinutes(DurationMinutes);
    }

    public void Revoke(string actorSubjectId, DateTimeOffset revokedAtUtc)
    {
        if (Status == SupportElevationStatus.Revoked) throw new InvalidOperationException("The elevation is already revoked.");
        if (string.IsNullOrWhiteSpace(actorSubjectId)) throw new ArgumentException("Revoking subject is required.");
        Status = SupportElevationStatus.Revoked;
        RevokedBySubjectId = actorSubjectId.Trim();
        RevokedAtUtc = revokedAtUtc;
    }

    public bool IsEffective(DateTimeOffset now) =>
        Status == SupportElevationStatus.Active && ExpiresAtUtc > now && RevokedAtUtc is null;
}
