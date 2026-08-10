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

public sealed record ChangeOrganizationMembershipRequest(
    string SubjectId,
    string Status);

public sealed record RegisterProductRequest(
    string ApplicationCode,
    string Name);

public sealed record ChangeOrganizationProductAccessRequest(
    string ApplicationCode,
    string Status);
