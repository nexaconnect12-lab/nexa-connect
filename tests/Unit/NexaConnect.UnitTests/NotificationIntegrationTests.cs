using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Infrastructure.Messaging;
using NexaConnect.Services.Notification.Application.Messages;
using NexaConnect.Services.Notification.Infrastructure;

namespace NexaConnect.UnitTests;

public sealed class NotificationIntegrationTests
{
    [Fact]
    public async Task Requested_event_is_consumed_once_and_remains_tenant_scoped()
    {
        var sender = new InMemoryNotificationSender();
        var handler = new NotificationIntegrationHandler(new InMemoryInboxStore(), sender);
        Guid organizationId = Guid.NewGuid();
        var message = new NotificationRequestedV1(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
            organizationId, "email", "operator@example.test", "Order ready", "Your order is ready.", "order");

        Assert.Equal(NotificationHandlingResult.Processed, await handler.HandleAsync(message, default));
        Assert.Equal(NotificationHandlingResult.Duplicate, await handler.HandleAsync(message, default));
    }

    [Fact]
    public void Notification_lookup_does_not_cross_organization_boundary()
    {
        var sender = new InMemoryNotificationSender();
        Guid organizationId = Guid.NewGuid();
        NotificationMessage result = sender.Send(new(organizationId, "email", "operator@example.test", "Subject", "Body"),
            new NotificationMutationContext("actor", Guid.NewGuid(), Guid.NewGuid().ToString("D")));

        Assert.NotNull(sender.Get(organizationId, result.Id));
        Assert.Null(sender.Get(Guid.NewGuid(), result.Id));
    }
}
