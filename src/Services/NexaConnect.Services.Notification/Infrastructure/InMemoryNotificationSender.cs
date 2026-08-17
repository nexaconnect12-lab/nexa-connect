using System.Collections.Concurrent;
using NexaConnect.Services.Notification.Application.Messages;
using NexaConnect.Services.Notification.Domain;

namespace NexaConnect.Services.Notification.Infrastructure;

public sealed class InMemoryNotificationSender : INotificationSender
{
    private readonly ConcurrentDictionary<Guid, NotificationMessage> notifications = new();

    public NotificationMessage Send(SendNotification command, NotificationMutationContext context)
    {
        NotificationAggregate.ValidateActor(context.ActorSubjectId);
        var normalized = NotificationAggregate.Normalize(command.OrganizationId, command.Channel, command.Recipient,
            command.Subject, command.Body);
        var notification = new NotificationMessage(Guid.NewGuid(), command.OrganizationId, normalized.Channel,
            normalized.Recipient, normalized.Subject, normalized.Body, "queued", DateTimeOffset.UtcNow);
        notifications[notification.Id] = notification;
        return notification;
    }

    public NotificationMessage? Get(Guid organizationId, Guid id) => notifications.GetValueOrDefault(id) is { } value && value.OrganizationId == organizationId ? value : null;
}
