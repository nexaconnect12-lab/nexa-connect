using NexaConnect.Contracts.Platform;

namespace NexaConnect.Services.PlatformDirectory.Application.Access;

public interface IOrganizationAccessReader
{
    Task<bool> HasNexaConnectAccessAsync(
        Guid organizationId,
        string subjectId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<OrganizationApplicationAccess>> GetCurrentAccessAsync(
        string subjectId,
        CancellationToken cancellationToken);
}
