namespace NexaConnect.Services.Notification.Application.Messages;

public sealed record SendNotification(string Channel, string Recipient, string Subject, string Body);
public sealed record NotificationMessage(Guid Id, string Channel, string Recipient, string Subject, string Body, string Status, DateTimeOffset CreatedAtUtc);

public interface INotificationSender
{
    NotificationMessage Send(SendNotification command);
    NotificationMessage? Get(Guid id);
}
