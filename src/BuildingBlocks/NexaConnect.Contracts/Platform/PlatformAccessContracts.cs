namespace NexaConnect.Contracts.Platform;

public sealed record OrganizationApplicationAccess(
    Guid OrganizationId,
    string OrganizationCode,
    string OrganizationName,
    string ApplicationCode);

public sealed record CurrentPlatformAccessResponse(
    string SubjectId,
    IReadOnlyList<OrganizationApplicationAccess> Organizations);
