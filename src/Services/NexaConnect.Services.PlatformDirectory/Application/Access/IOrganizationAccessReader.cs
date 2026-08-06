namespace NexaConnect.Services.PlatformDirectory.Application.Access;

public interface IOrganizationAccessReader
{
    Task<bool> HasNexaConnectAccessAsync(
        Guid organizationId,
        string subjectId,
        CancellationToken cancellationToken);
}
