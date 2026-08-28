extern alias PAYMENT;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using PaymentReadiness = PAYMENT::NexaConnect.Services.Payment.Infrastructure.PaymentDatabaseReadinessHealthCheck;

namespace NexaConnect.IntegrationTests;

public sealed class PaymentDatabaseReadinessHealthCheckTests
{
    [PaymentDatabaseFact]
    public async Task Readiness_requires_reachable_payment_migration_7()
    {
        string configured = Environment.GetEnvironmentVariable("NEXACONNECT_PAYMENT_INTEGRATION_DB")!;
        string schema = $"payment_readiness_it_{Guid.NewGuid():N}";
        var builder = new NpgsqlConnectionStringBuilder(configured) { SearchPath = schema };
        await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        await new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection).ExecuteNonQueryAsync();

        try
        {
            await new NpgsqlCommand(
                "CREATE TABLE nexaconnect_schema_migrations(version integer NOT NULL); INSERT INTO nexaconnect_schema_migrations(version) VALUES (7)",
                connection).ExecuteNonQueryAsync();
            var check = new PaymentReadiness(dataSource);

            HealthCheckResult current = await check.CheckHealthAsync(new HealthCheckContext());
            Assert.Equal(HealthStatus.Healthy, current.Status);
            Assert.Equal(7, current.Data["currentSchemaVersion"]);

            await new NpgsqlCommand("UPDATE nexaconnect_schema_migrations SET version=6", connection).ExecuteNonQueryAsync();
            HealthCheckResult stale = await check.CheckHealthAsync(new HealthCheckContext());
            Assert.Equal(HealthStatus.Unhealthy, stale.Status);
            Assert.Equal(6, stale.Data["currentSchemaVersion"]);

            var unavailableBuilder = new NpgsqlConnectionStringBuilder(configured)
            {
                Host = "127.0.0.1",
                Port = 1,
                Timeout = 1,
                CommandTimeout = 1,
                Pooling = false
            };
            await using NpgsqlDataSource unavailable = NpgsqlDataSource.Create(unavailableBuilder.ConnectionString);
            HealthCheckResult unreachable = await new PaymentReadiness(unavailable)
                .CheckHealthAsync(new HealthCheckContext());
            Assert.Equal(HealthStatus.Unhealthy, unreachable.Status);
        }
        finally
        {
            await new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", connection).ExecuteNonQueryAsync();
        }
    }
}
