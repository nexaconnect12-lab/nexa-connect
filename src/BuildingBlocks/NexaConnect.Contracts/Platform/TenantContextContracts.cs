namespace NexaConnect.Contracts.Platform;

public sealed record TenantContext(
    string SubjectId,
    Guid OrganizationId,
    string ApplicationCode);

public sealed record TenantContextResponse(
    string SubjectId,
    IReadOnlyList<TenantContext> Tenants);
