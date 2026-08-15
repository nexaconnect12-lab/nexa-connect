namespace NexaConnect.Services.Notification.Application.Messages;

public sealed record SendNotification(Guid OrganizationId, string Channel, string Recipient, string Subject, string Body, Guid? SourceEventId = null);
public sealed record NotificationMessage(Guid Id, Guid OrganizationId, string Channel, string Recipient, string Subject, string Body, string Status, DateTimeOffset CreatedAtUtc);

public interface INotificationSender
{
    NotificationMessage Send(SendNotification command, string actorSubjectId);
    NotificationMessage? Get(Guid organizationId, Guid id);
}
