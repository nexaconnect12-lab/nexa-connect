using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace NexaConnect.Services.Payment.Infrastructure.Webhooks;

public sealed class OmiseWebhookReadiness(NpgsqlDataSource source) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken token=default)
    {
        try
        {
            await using var command=source.CreateCommand("SELECT event_id,claim_id,locked_until_utc,trace_correlation_id FROM omise_webhook_inbox LIMIT 0");
            await command.ExecuteNonQueryAsync(token); return HealthCheckResult.Healthy();
        }
        catch (NpgsqlException) { return HealthCheckResult.Unhealthy("Omise webhook inbox schema unavailable."); }
    }
}
