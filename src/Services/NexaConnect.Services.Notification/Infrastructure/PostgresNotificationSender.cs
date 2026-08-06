using Npgsql;
using NexaConnect.Services.Notification.Application.Messages;

namespace NexaConnect.Services.Notification.Infrastructure;

public sealed class PostgresNotificationSender(NpgsqlDataSource dataSource) : INotificationSender
{
    public NotificationMessage Send(SendNotification command)
    {
        if (string.IsNullOrWhiteSpace(command.Channel) || string.IsNullOrWhiteSpace(command.Recipient) || string.IsNullOrWhiteSpace(command.Subject) || string.IsNullOrWhiteSpace(command.Body)) throw new ArgumentException("Channel, recipient, subject, and body are required.");
        var message = new NotificationMessage(Guid.NewGuid(), command.Channel.Trim().ToLowerInvariant(), command.Recipient.Trim(), command.Subject.Trim(), command.Body, "queued", DateTimeOffset.UtcNow);
        using var sql = dataSource.CreateCommand("INSERT INTO notifications (id,channel,recipient,subject,body,status,created_at_utc) VALUES (@id,@channel,@recipient,@subject,@body,@status,@created)"); sql.Parameters.AddWithValue("id", message.Id); sql.Parameters.AddWithValue("channel", message.Channel); sql.Parameters.AddWithValue("recipient", message.Recipient); sql.Parameters.AddWithValue("subject", message.Subject); sql.Parameters.AddWithValue("body", message.Body); sql.Parameters.AddWithValue("status", message.Status); sql.Parameters.AddWithValue("created", message.CreatedAtUtc.UtcDateTime); sql.ExecuteNonQuery(); return message;
    }
    public NotificationMessage? Get(Guid id)
    {
        using var sql = dataSource.CreateCommand("SELECT channel,recipient,subject,body,status,created_at_utc FROM notifications WHERE id=@id"); sql.Parameters.AddWithValue("id", id); using var reader = sql.ExecuteReader(); if (!reader.Read()) return null; return new NotificationMessage(id, reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), new DateTimeOffset(reader.GetDateTime(5), TimeSpan.Zero));
    }
}
