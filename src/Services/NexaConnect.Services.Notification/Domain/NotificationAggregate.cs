namespace NexaConnect.Services.Notification.Domain;

public sealed class NotificationIdempotencyConflictException(string message) : InvalidOperationException(message);

public enum NotificationDeliveryStatus
{
    RetryScheduled,
    ProviderAccepted,
    Delivered,
    DeliveryFailed
}

public static class NotificationAggregate
{
    private static readonly HashSet<string> Channels = ["email", "sms", "push", "in_app"];

    public static (string Channel, string Recipient, string Subject, string Body) Normalize(
        Guid organizationId, string channel, string recipient, string subject, string body)
    {
        if (organizationId == Guid.Empty) throw new ArgumentException("Organization is required.");
        string normalizedChannel = Required(channel, 32, "Channel").ToLowerInvariant();
        if (!Channels.Contains(normalizedChannel))
            throw new ArgumentException("Channel must be email, sms, push, or in_app.");
        return (normalizedChannel, Required(recipient, 320, "Recipient"), Required(subject, 200, "Subject"),
            Required(body, 10000, "Body", allowLineBreaks: true));
    }

    public static void ValidateActor(string actor)
    {
        Required(actor, 200, "Actor");
    }

    private static string Required(string value, int maximum, string name, bool allowLineBreaks = false)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.");
        string normalized = value.Trim();
        if (normalized.Length > maximum || (!allowLineBreaks && normalized.Any(char.IsControl))
            || (allowLineBreaks && normalized.Any(character => char.IsControl(character)
                && character is not '\r' and not '\n' and not '\t')))
            throw new ArgumentException($"{name} is invalid.");
        return normalized;
    }
}
