using Npgsql;
using NexaConnect.Services.Payment.Application.Webhooks;

namespace NexaConnect.Services.Payment.Infrastructure.Webhooks;

public sealed class PostgresOmiseWebhookInbox(NpgsqlDataSource source) : IOmiseWebhookInbox
{
    public async Task EnqueueAsync(string eventId, Guid correlationId, CancellationToken token, string? traceCorrelationId = null)
    {
        if (!OmiseWebhookIdentity.Valid(eventId) || correlationId == Guid.Empty) throw new ArgumentException("Invalid webhook identity.");
        string trace = traceCorrelationId ?? correlationId.ToString("D");
        if (trace.Length > 128 || !System.Text.RegularExpressions.Regex.IsMatch(trace, @"\A[A-Za-z0-9._:-]+\z")) throw new ArgumentException("Invalid webhook correlation.");
        await using var command = source.CreateCommand("INSERT INTO omise_webhook_inbox(event_id,correlation_id,trace_correlation_id) VALUES($1,$2,$3) ON CONFLICT(event_id) DO NOTHING");
        command.Parameters.AddWithValue(eventId); command.Parameters.AddWithValue(correlationId);
        command.Parameters.AddWithValue(trace);
        await command.ExecuteNonQueryAsync(token);
    }
    public async Task<WebhookClaim?> ClaimAsync(TimeSpan lease, CancellationToken token)
    {
        Guid fence = Guid.NewGuid();
        await using var command = source.CreateCommand("""
            WITH candidate AS (
              SELECT event_id FROM omise_webhook_inbox
              WHERE (status='pending' AND next_attempt_at_utc<=now()) OR (status='processing' AND locked_until_utc<=now())
              ORDER BY received_at_utc FOR UPDATE SKIP LOCKED LIMIT 1
            ) UPDATE omise_webhook_inbox target SET status='processing',claim_id=$1,locked_until_utc=now()+$2,attempts=attempts+1
              FROM candidate WHERE target.event_id=candidate.event_id
              RETURNING target.event_id,target.correlation_id,target.attempts,target.trace_correlation_id
            """);
        command.Parameters.AddWithValue(fence); command.Parameters.AddWithValue(lease);
        await using var reader = await command.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token) ? new(reader.GetString(0), fence, reader.GetGuid(1), reader.GetInt32(2),reader.GetString(3)) : null;
    }
    public async Task FinishAsync(WebhookClaim claim, string outcome, TimeSpan retryDelay, int maximumAttempts, CancellationToken token)
    {
        if (outcome is not ("retry" or "completed" or "rejected")) throw new ArgumentException("Invalid webhook outcome.");
        string status = outcome == "retry" ? claim.Attempts >= maximumAttempts ? "exhausted" : "pending" : outcome;
        await using var command = source.CreateCommand("""
            UPDATE omise_webhook_inbox SET status=$1,claim_id=NULL,locked_until_utc=NULL,next_attempt_at_utc=now()+$2,
              completed_at_utc=CASE WHEN $1='pending' THEN NULL ELSE now() END
              WHERE event_id=$3 AND claim_id=$4 AND status='processing' AND locked_until_utc>now()
            """);
        command.Parameters.AddWithValue(status); command.Parameters.AddWithValue(retryDelay);
        command.Parameters.AddWithValue(claim.EventId); command.Parameters.AddWithValue(claim.Fence);
        await command.ExecuteNonQueryAsync(token);
    }
}
