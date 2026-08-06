using System.Collections.Concurrent;
using NexaConnect.Services.Notification.Application.Messages;

namespace NexaConnect.Services.Notification.Infrastructure;

public sealed class InMemoryNotificationSender : INotificationSender
{
    private readonly ConcurrentDictionary<Guid, NotificationMessage> notifications = new();

    public NotificationMessage Send(SendNotification command)
    {
        if (string.IsNullOrWhiteSpace(command.Channel) || string.IsNullOrWhiteSpace(command.Recipient) ||
            string.IsNullOrWhiteSpace(command.Subject) || string.IsNullOrWhiteSpace(command.Body))
            throw new ArgumentException("Channel, recipient, subject, and body are required.");
        var notification = new NotificationMessage(Guid.NewGuid(), command.Channel.Trim().ToLowerInvariant(), command.Recipient.Trim(),
            command.Subject.Trim(), command.Body, "queued", DateTimeOffset.UtcNow);
        notifications[notification.Id] = notification;
        return notification;
    }

    public NotificationMessage? Get(Guid id) => notifications.GetValueOrDefault(id);
}
