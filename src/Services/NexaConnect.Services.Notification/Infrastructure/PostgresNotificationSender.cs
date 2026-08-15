using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Notification.Application.Messages;
using Npgsql;

namespace NexaConnect.Services.Notification.Infrastructure;

public sealed class PostgresNotificationSender(NpgsqlDataSource dataSource) : INotificationSender
{
    public NotificationMessage Send(SendNotification command, string actorSubjectId)
    {
        Validate(command, actorSubjectId);
        Guid notificationId = Guid.NewGuid();
        Guid eventId = Guid.NewGuid();
        Guid correlationId = command.SourceEventId ?? eventId;
        DateTimeOffset occurredAt = DateTimeOffset.UtcNow;
        var queued = new NotificationQueuedV1(eventId, correlationId, occurredAt, notificationId, command.OrganizationId, command.Channel.Trim().ToLowerInvariant());
        var audit = new PlatformAuditEventV1(Guid.NewGuid(), correlationId, occurredAt, actorSubjectId.Trim(), command.OrganizationId,
            "notification.queued", "notification", notificationId.ToString("D"), "succeeded");

        using NpgsqlConnection connection = dataSource.OpenConnection();
        using NpgsqlTransaction transaction = connection.BeginTransaction();
        const string insert = """
            INSERT INTO notifications (id,organization_id,source_event_id,channel,recipient,subject,body,status,created_at_utc)
            VALUES ($1,$2,$3,$4,$5,$6,$7,'queued',$8)
            ON CONFLICT (source_event_id) WHERE source_event_id IS NOT NULL
            DO UPDATE SET source_event_id=EXCLUDED.source_event_id
            RETURNING id,organization_id,channel,recipient,subject,body,status,created_at_utc;
            """;
        using var commandSql = new NpgsqlCommand(insert, connection, transaction);
        commandSql.Parameters.AddWithValue(notificationId);
        commandSql.Parameters.AddWithValue(command.OrganizationId);
        commandSql.Parameters.AddWithValue((object?)command.SourceEventId ?? DBNull.Value);
        commandSql.Parameters.AddWithValue(command.Channel.Trim().ToLowerInvariant());
        commandSql.Parameters.AddWithValue(command.Recipient.Trim());
        commandSql.Parameters.AddWithValue(command.Subject.Trim());
        commandSql.Parameters.AddWithValue(command.Body);
        commandSql.Parameters.AddWithValue(occurredAt);
        using NpgsqlDataReader reader = commandSql.ExecuteReader();
        reader.Read();
        NotificationMessage result = Read(reader);
        reader.Close();

        if (result.Id == notificationId)
        {
            using var auditSql = new NpgsqlCommand("INSERT INTO notification_audit_records(id,organization_id,notification_id,action,actor_subject_id,occurred_at_utc) VALUES($1,$2,$3,$4,$5,$6);", connection, transaction);
            auditSql.Parameters.AddWithValue(audit.EventId); auditSql.Parameters.AddWithValue(command.OrganizationId); auditSql.Parameters.AddWithValue(notificationId);
            auditSql.Parameters.AddWithValue(audit.Action); auditSql.Parameters.AddWithValue(audit.SubjectId); auditSql.Parameters.AddWithValue(occurredAt); auditSql.ExecuteNonQuery();
            Enqueue(connection, transaction, queued.EventId, "notification.queued.v1", notificationId, JsonSerializer.Serialize(queued), correlationId, occurredAt);
            Enqueue(connection, transaction, audit.EventId, "notification.audit.v1", notificationId, JsonSerializer.Serialize(audit), correlationId, occurredAt);
        }
        transaction.Commit();
        return result;
    }

    public NotificationMessage? Get(Guid organizationId, Guid id)
    {
        using var sql = dataSource.CreateCommand("SELECT id,organization_id,channel,recipient,subject,body,status,created_at_utc FROM notifications WHERE organization_id=$1 AND id=$2");
        sql.Parameters.AddWithValue(organizationId); sql.Parameters.AddWithValue(id);
        using NpgsqlDataReader reader = sql.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    private static NotificationMessage Read(NpgsqlDataReader reader) => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetFieldValue<DateTimeOffset>(7));

    private static void Enqueue(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, string type, Guid aggregateId, string payload, Guid correlationId, DateTimeOffset occurredAt)
    {
        using var sql = new NpgsqlCommand("INSERT INTO outbox_messages(id,event_type,contract_version,aggregate_type,aggregate_id,payload,correlation_id,occurred_at_utc) VALUES($1,$2,1,'notification',$3,$4::jsonb,$5,$6);", connection, transaction);
        sql.Parameters.AddWithValue(id); sql.Parameters.AddWithValue(type); sql.Parameters.AddWithValue(aggregateId); sql.Parameters.AddWithValue(payload); sql.Parameters.AddWithValue(correlationId.ToString("D")); sql.Parameters.AddWithValue(occurredAt); sql.ExecuteNonQuery();
    }

    private static void Validate(SendNotification command, string actorSubjectId)
    {
        if (command.OrganizationId == Guid.Empty || string.IsNullOrWhiteSpace(actorSubjectId) || string.IsNullOrWhiteSpace(command.Channel)
            || string.IsNullOrWhiteSpace(command.Recipient) || string.IsNullOrWhiteSpace(command.Subject) || string.IsNullOrWhiteSpace(command.Body))
            throw new ArgumentException("Organization, actor, channel, recipient, subject, and body are required.");
    }
}
