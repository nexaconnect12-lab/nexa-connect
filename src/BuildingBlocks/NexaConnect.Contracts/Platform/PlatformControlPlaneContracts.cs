namespace NexaConnect.Contracts.Platform;

// These records are transport contracts. Platform and product bounded contexts
// keep their own aggregates and persistence models behind these boundaries.
public sealed record OrganizationSummary(
    Guid OrganizationId,
    string Code,
    string Name,
    string Status,
    string DefaultTimeZone);

public sealed record OrganizationMembershipSummary(
    Guid OrganizationId,
    string SubjectId,
    string Status,
    DateTimeOffset? JoinedAtUtc);

public sealed record ProductRegistration(
    string ApplicationCode,
    string Name,
    string Status);

public sealed record OrganizationProductAccess(
    Guid OrganizationId,
    string ApplicationCode,
    string Status,
    DateTimeOffset EnabledAtUtc);

public sealed record CreateOrganizationRequest(
    string Code,
    string Name,
    string DefaultTimeZone);

public sealed record UpdateOrganizationRequest(
    string Name,
    string Status,
    string DefaultTimeZone);

public sealed record ChangeOrganizationMembershipRequest(
    string SubjectId,
    string Status);

public sealed record CustomerMembershipSummary(
    Guid OrganizationId,
    string SubjectId,
    string Status,
    DateTimeOffset? InvitedAtUtc,
    DateTimeOffset? JoinedAtUtc,
    DateTimeOffset? SuspendedAtUtc,
    DateTimeOffset? RemovedAtUtc,
    long ConcurrencyVersion);

public sealed record ChangeCustomerMembershipRequest(
    string Status,
    long? ExpectedVersion);

public sealed record RegisterProductRequest(
    string ApplicationCode,
    string Name);

public sealed record ChangeOrganizationProductAccessRequest(
    string ApplicationCode,
    string Status);

public sealed record RequestSupportElevationRequest(
    Guid OrganizationId,
    string ApplicationCode,
    string Reason,
    int DurationMinutes);

public sealed record SupportElevationSummary(
    Guid ElevationId,
    Guid OrganizationId,
    string ApplicationCode,
    string SupportSubjectId,
    string Reason,
    string Status,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? ApprovedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? RevokedAtUtc,
    string? ApprovedBySubjectId,
    string? RevokedBySubjectId);

public sealed record PlatformUserSummary(
    string SubjectId,
    string Username,
    string? Email,
    bool Enabled,
    IReadOnlyCollection<string> Roles);

public sealed record CreatePlatformUserRequest(
    string Username,
    string? Email,
    bool Enabled,
    IReadOnlyCollection<string> Roles);

public sealed record UpdatePlatformUserRequest(
    string? Email,
    bool Enabled);

public sealed record ChangePlatformUserRolesRequest(IReadOnlyCollection<string> Roles);

public sealed record PlatformPermissionSummary(string Code, string Description);

public sealed record PlatformRoleSummary(
    string Code,
    string Description,
    IReadOnlyCollection<string> Permissions);

public sealed record PlatformAuditRecord(
    Guid AuditId,
    string Action,
    string ResourceType,
    string ResourceId,
    string ActorSubjectId,
    string Outcome,
    DateTimeOffset OccurredAtUtc);

public sealed record PlatformAuditQuery(
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    string? ActorSubjectId,
    string? Action,
    int Limit = 100);

public sealed record PlatformSummary(
    long OrganizationCount,
    long ActiveOrganizationCount,
    long ActiveMembershipCount,
    long RegisteredProductCount,
    long EnabledProductAccessCount,
    long ActiveSupportElevationCount,
    DateTimeOffset AsOfUtc);
