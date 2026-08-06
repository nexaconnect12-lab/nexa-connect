using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using NexaConnect.Services.Notification.Application.Messages;

namespace NexaConnect.Services.Notification.Infrastructure;

public sealed class NotificationProviderOptions
{
    public string BaseUrl { get; set; } = "";
    public string Path { get; set; } = "notifications";
}

public sealed class HttpNotificationSender(HttpClient client, IOptions<NotificationProviderOptions> options) : INotificationSender
{
    public NotificationMessage Send(SendNotification command)
    {
        var message = new NotificationMessage(Guid.NewGuid(), command.Channel.Trim().ToLowerInvariant(), command.Recipient.Trim(), command.Subject.Trim(), command.Body, "queued", DateTimeOffset.UtcNow);
        using var response = client.PostAsJsonAsync(options.Value.Path, message).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Notification provider returned {(int)response.StatusCode}.");
        return message;
    }

    public NotificationMessage? Get(Guid id) => null;
}
