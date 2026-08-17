namespace NexaConnect.Services.Notification.Application.Messages;

public sealed record SendNotification(Guid OrganizationId, string Channel, string Recipient, string Subject, string Body, Guid? SourceEventId = null);
public sealed record NotificationMessage(Guid Id, Guid OrganizationId, string Channel, string Recipient, string Subject, string Body, string Status, DateTimeOffset CreatedAtUtc);
public sealed record NotificationMutationContext(string ActorSubjectId, Guid CorrelationId, string RequestCorrelationId);

public interface INotificationSender
{
    NotificationMessage Send(SendNotification command, NotificationMutationContext context);
    NotificationMessage? Get(Guid organizationId, Guid id);
}
