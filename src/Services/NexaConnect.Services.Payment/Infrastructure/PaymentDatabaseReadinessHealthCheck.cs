using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace NexaConnect.Services.Payment.Infrastructure;

public sealed class PaymentDatabaseReadinessHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public const int RequiredSchemaVersion = 6;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                "SELECT COALESCE(max(version), 0) FROM nexaconnect_schema_migrations",
                connection);
            int currentVersion = Convert.ToInt32(
                await command.ExecuteScalarAsync(cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture);
            var data = new Dictionary<string, object>
            {
                ["currentSchemaVersion"] = currentVersion,
                ["requiredSchemaVersion"] = RequiredSchemaVersion
            };

            return currentVersion >= RequiredSchemaVersion
                ? HealthCheckResult.Healthy("Payment database is reachable and current.", data)
                : HealthCheckResult.Unhealthy("Payment database migration is below the required version.", data: data);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy(
                "Payment database is unavailable or its migration history cannot be read.",
                exception);
        }
    }
}
