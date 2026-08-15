namespace NexaConnect.Services.Notification.Application.Tenant;

public interface INotificationTenantAuthorizer
{
    Task<bool> CanAccessAsync(Guid organizationId, string permission, string authorizationHeader, CancellationToken cancellationToken);
}
