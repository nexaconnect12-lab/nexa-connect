using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Infrastructure.Messaging;

namespace NexaConnect.Services.Notification.Application.Messages;

public enum NotificationHandlingResult { Processed, Duplicate, Busy }

public sealed class NotificationIntegrationHandler(IDurableInboxStore inbox, INotificationSender sender)
{
    private const string Consumer = "notification.requested.v1";

    public async Task<NotificationHandlingResult> HandleAsync(NotificationRequestedV1 message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.SourceService) || message.SourceService.Length > 64
            || message.SourceService.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '.')))
            throw new ArgumentException("SourceService must be a bounded service identifier.", nameof(message));

        InboxClaimResult claim = await inbox.ClaimAsync(message.EventId, Consumer, TimeSpan.FromMinutes(2), cancellationToken);
        if (claim == InboxClaimResult.Completed) return NotificationHandlingResult.Duplicate;
        if (claim == InboxClaimResult.Busy) return NotificationHandlingResult.Busy;

        try
        {
            sender.Send(new SendNotification(message.OrganizationId, message.Channel, message.Recipient, message.Subject,
                message.Body, message.EventId), new NotificationMutationContext($"service:{message.SourceService}",
                message.CorrelationId, message.CorrelationId.ToString("D")));
            await inbox.MarkCompletedAsync(message.EventId, Consumer, cancellationToken);
            return NotificationHandlingResult.Processed;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await inbox.ReleaseAsync(message.EventId, Consumer, exception.GetType().Name, cancellationToken);
            throw;
        }
    }
}
