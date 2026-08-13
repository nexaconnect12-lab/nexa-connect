using System.Text.Json;using NexaConnect.Contracts.IntegrationEvents;using Npgsql;
namespace NexaConnect.Infrastructure.Messaging;
public static class TransactionalAuditOutbox
{
 public static async Task EnqueueAuditAsync(NpgsqlConnection connection,NpgsqlTransaction transaction,PlatformAuditEventV1 audit,string eventType,CancellationToken c){const string sql="INSERT INTO outbox_messages(id,event_type,contract_version,aggregate_type,aggregate_id,payload,correlation_id,occurred_at_utc) VALUES($1,$2,1,'audit',$3,$4::jsonb,$5,$6);";await using var cmd=new NpgsqlCommand(sql,connection,transaction);cmd.Parameters.AddWithValue(audit.EventId);cmd.Parameters.AddWithValue(eventType);cmd.Parameters.AddWithValue(audit.OrganizationId!.Value);cmd.Parameters.AddWithValue(JsonSerializer.Serialize(audit));cmd.Parameters.AddWithValue(audit.CorrelationId.ToString("D"));cmd.Parameters.AddWithValue(audit.OccurredAtUtc);await cmd.ExecuteNonQueryAsync(c);}
}
