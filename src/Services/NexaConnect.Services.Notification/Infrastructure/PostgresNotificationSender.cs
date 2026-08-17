using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Notification.Application.Messages;
using NexaConnect.Services.Notification.Domain;
using Npgsql;

namespace NexaConnect.Services.Notification.Infrastructure;

public sealed class PostgresNotificationSender(NpgsqlDataSource dataSource) : INotificationSender
{
    public NotificationMessage Send(SendNotification command, NotificationMutationContext context)
    {
        NotificationAggregate.ValidateActor(context.ActorSubjectId);
        var value = NotificationAggregate.Normalize(command.OrganizationId, command.Channel, command.Recipient,
            command.Subject, command.Body);
        if (context.CorrelationId == Guid.Empty || string.IsNullOrWhiteSpace(context.RequestCorrelationId)
            || context.RequestCorrelationId.Length > 128 || context.RequestCorrelationId.Any(char.IsControl))
            throw new ArgumentException("A bounded correlation identifier is required.");
        Guid notificationId = Guid.NewGuid();
        Guid eventId = Guid.NewGuid();
        DateTimeOffset occurredAt = DateTimeOffset.UtcNow;
        var queued = new NotificationQueuedV1(eventId, context.CorrelationId, occurredAt, notificationId,
            command.OrganizationId, value.Channel);
        var audit = new PlatformAuditEventV1(Guid.NewGuid(), context.CorrelationId, occurredAt,
            context.ActorSubjectId.Trim(), command.OrganizationId, "notification.queued", "notification",
            notificationId.ToString("D"), "succeeded");
        using NpgsqlConnection connection = dataSource.OpenConnection();
        using NpgsqlTransaction transaction = connection.BeginTransaction();
        const string insert = """
            INSERT INTO notifications(id,organization_id,source_event_id,channel,recipient,subject,body,status,
                created_at_utc,updated_at_utc,correlation_id,next_delivery_attempt_at_utc)
            VALUES($1,$2,$3,$4,$5,$6,$7,'queued',$8,$8,$9,$8)
            ON CONFLICT DO NOTHING
            RETURNING id,organization_id,channel,recipient,subject,body,status,created_at_utc;
            """;
        using var sql = new NpgsqlCommand(insert, connection, transaction);
        sql.Parameters.AddWithValue(notificationId); sql.Parameters.AddWithValue(command.OrganizationId);
        sql.Parameters.AddWithValue((object?)command.SourceEventId ?? DBNull.Value); sql.Parameters.AddWithValue(value.Channel);
        sql.Parameters.AddWithValue(value.Recipient); sql.Parameters.AddWithValue(value.Subject); sql.Parameters.AddWithValue(value.Body);
        sql.Parameters.AddWithValue(occurredAt); sql.Parameters.AddWithValue(context.RequestCorrelationId);
        using NpgsqlDataReader reader = sql.ExecuteReader();
        bool created = reader.Read();
        NotificationMessage? result = created ? Read(reader) : null;
        reader.Close();
        if (!created)
        {
            if (command.SourceEventId is null) throw new InvalidOperationException("Notification insert conflicted unexpectedly.");
            result = ReadBySource(connection, transaction, command.SourceEventId.Value)
                ?? throw new InvalidOperationException("Notification source-event lookup returned no row.");
            if (result.OrganizationId != command.OrganizationId || result.Channel != value.Channel
                || result.Recipient != value.Recipient || result.Subject != value.Subject || result.Body != value.Body)
                throw new NotificationIdempotencyConflictException(
                    "The source event is already associated with a different notification request.");
        }
        else
        {
            InsertAudit(connection, transaction, audit, notificationId);
            Enqueue(connection, transaction, queued.EventId, "notification.queued.v1", notificationId, queued,
                context.RequestCorrelationId, occurredAt);
            Enqueue(connection, transaction, audit.EventId, "notification.audit.v1", notificationId, audit,
                context.RequestCorrelationId, occurredAt);
        }
        transaction.Commit();
        return result!;
    }

    public NotificationMessage? Get(Guid organizationId, Guid id)
    {
        using var sql = dataSource.CreateCommand("SELECT id,organization_id,channel,recipient,subject,body,status,created_at_utc FROM notifications WHERE organization_id=$1 AND id=$2");
        sql.Parameters.AddWithValue(organizationId); sql.Parameters.AddWithValue(id);
        using NpgsqlDataReader reader = sql.ExecuteReader(); return reader.Read() ? Read(reader) : null;
    }

    private static NotificationMessage? ReadBySource(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid source)
    { using var sql = new NpgsqlCommand("SELECT id,organization_id,channel,recipient,subject,body,status,created_at_utc FROM notifications WHERE source_event_id=$1", connection, transaction); sql.Parameters.AddWithValue(source); using var reader = sql.ExecuteReader(); return reader.Read() ? Read(reader) : null; }
    private static NotificationMessage Read(NpgsqlDataReader reader) => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetFieldValue<DateTimeOffset>(7));
    private static void InsertAudit(NpgsqlConnection c, NpgsqlTransaction t, PlatformAuditEventV1 audit, Guid id) { using var sql = new NpgsqlCommand("INSERT INTO notification_audit_records(id,organization_id,notification_id,action,actor_subject_id,occurred_at_utc) VALUES($1,$2,$3,$4,$5,$6)", c, t); sql.Parameters.AddWithValue(audit.EventId); sql.Parameters.AddWithValue(audit.OrganizationId!.Value); sql.Parameters.AddWithValue(id); sql.Parameters.AddWithValue(audit.Action); sql.Parameters.AddWithValue(audit.SubjectId); sql.Parameters.AddWithValue(audit.OccurredAtUtc); sql.ExecuteNonQuery(); }
    internal static void Enqueue(NpgsqlConnection c, NpgsqlTransaction t, Guid id, string type, Guid aggregate, object payload, string correlation, DateTimeOffset at) { using var sql = new NpgsqlCommand("INSERT INTO outbox_messages(id,event_type,contract_version,aggregate_type,aggregate_id,payload,correlation_id,occurred_at_utc) VALUES($1,$2,1,'notification',$3,$4::jsonb,$5,$6)", c, t); sql.Parameters.AddWithValue(id); sql.Parameters.AddWithValue(type); sql.Parameters.AddWithValue(aggregate); sql.Parameters.AddWithValue(JsonSerializer.Serialize(payload)); sql.Parameters.AddWithValue(correlation); sql.Parameters.AddWithValue(at); sql.ExecuteNonQuery(); }
}
