extern alias REPORTING;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Infrastructure.Messaging;
using Npgsql;
using RabbitMQ.Client;
using ReportingQueries = REPORTING::NexaConnect.Services.Reporting.Application.ReportingQueries;
using RefundFinancialReporting = REPORTING::NexaConnect.Services.Reporting.Application.RefundFinancialReporting;
using IRefundFinancialFactRepository = REPORTING::NexaConnect.Services.Reporting.Application.IRefundFinancialFactRepository;
using ReportingReadRepository = REPORTING::NexaConnect.Services.Reporting.Infrastructure.Persistence.PostgresReportingReadRepository;
using RefundFactRepository = REPORTING::NexaConnect.Services.Reporting.Infrastructure.Persistence.PostgresRefundFinancialFactRepository;
using RefundConsumer = REPORTING::NexaConnect.Services.Reporting.Infrastructure.Messaging.PaymentRefundFinancialConsumer;

namespace NexaConnect.IntegrationTests;

public sealed class RefundFinancialProjectionPostgresTests : IAsyncLifetime
{
    private readonly string? connectionString = Environment.GetEnvironmentVariable("NEXACONNECT_REPORTING_INTEGRATION_DB");
    private NpgsqlDataSource? dataSource;
    private string? schema;

    [ReportingDatabaseFact]
    public async Task Projection_is_idempotent_scoped_queryable_and_replayable_after_downgrade()
    {
        string migration = MigrationPath();
        await using NpgsqlConnection connection = await dataSource!.OpenConnectionAsync();
        await ExecuteAsync(connection, Path.Combine(migration, "up.sql"));
        var projection = new RefundFinancialReporting(new RefundFactRepository(dataSource));
        Guid organization = Guid.NewGuid(), branch = Guid.NewGuid();
        DateTimeOffset occurred = DateTimeOffset.UtcNow.AddMinutes(-2);
        PaymentRefundedV1 value = Event(organization, branch, occurred, 25m, 25m, 100m, "THB");

        Assert.True(await projection.ProjectAsync(value, default));
        Assert.False(await projection.ProjectAsync(value, default));
        await Assert.ThrowsAsync<ArgumentException>(() => projection.ProjectAsync(value with { Amount = 24m }, default));
        Assert.True(await projection.ProjectAsync(Event(Guid.NewGuid(), branch, occurred, 90m, 90m, 90m, "THB"), default));
        await SeedSaleAndPayment(connection, organization, branch, occurred.AddMinutes(-1));

        var queries = new ReportingQueries(new ReportingReadRepository(dataSource));
        var dashboard = await queries.DashboardAsync(organization, branch, occurred.AddHours(-1), occurred.AddHours(1), default);
        Assert.Equal(100m, dashboard.GrossSales);
        Assert.Equal(25m, dashboard.Refunded);
        Assert.Equal(75m, dashboard.NetSales);
        Assert.Equal(100m, dashboard.NetPaid);
        var sales = await queries.SalesAsync(organization, branch, occurred.AddHours(-1), occurred.AddHours(1), default);
        Assert.Equal(100m, sales.TotalSales);
        Assert.Equal(25m, sales.RefundedAmount);
        Assert.Equal(75m, sales.NetSales);
        Assert.Single(sales.Items);
        Assert.NotNull(sales.LatestGlobalCheckpointUpdatedAtUtc);

        await ExecuteAsync(connection, Path.Combine(migration, "down.sql"));
        Assert.Equal(DBNull.Value, await new NpgsqlCommand("SELECT to_regclass('refund_facts')::text", connection).ExecuteScalarAsync());
        Assert.Equal(0L, Convert.ToInt64(await new NpgsqlCommand(
            "SELECT count(*) FROM projection_checkpoints WHERE projector_name='payment-refund-financial'", connection).ExecuteScalarAsync()));
        await ExecuteAsync(connection, Path.Combine(migration, "up.sql"));
        Assert.True(await projection.ProjectAsync(value, default));
        Assert.Equal(1L, Convert.ToInt64(await new NpgsqlCommand("SELECT count(*) FROM refund_facts", connection).ExecuteScalarAsync()));
    }

    [ReportingRabbitFact]
    public async Task Durable_consumer_acknowledges_duplicates_and_projects_after_restart()
    {
        await using NpgsqlConnection database = await dataSource!.OpenConnectionAsync();
        await ExecuteAsync(database, Path.Combine(MigrationPath(), "up.sql"));
        string uri = Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_INTEGRATION_URI")!;
        string exchange = $"nexaconnect.reporting.refunds.{Guid.NewGuid():N}";
        string queue = $"nexaconnect.reporting.refunds.{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddSingleton(dataSource!);
        services.AddScoped<IRefundFinancialFactRepository, RefundFactRepository>();
        services.AddScoped<RefundFinancialReporting>();
        using ServiceProvider provider = services.BuildServiceProvider();
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PaymentRefundConsumer:ConnectionString"] = uri,
            ["PaymentRefundConsumer:Exchange"] = exchange,
            ["PaymentRefundConsumer:Queue"] = queue,
            ["PaymentRefundConsumer:PrefetchCount"] = "1"
        }).Build();
        await using IConnection rabbit = await new ConnectionFactory { Uri = new Uri(uri) }.CreateConnectionAsync();
        try
        {
            PaymentRefundedV1 first = Event(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, 10m, 10m, 50m, "THB");
            PaymentRefundedV1 marker = Event(first.OrganizationId, first.BranchId, DateTimeOffset.UtcNow, 5m, 15m, 50m, "THB");
            using (var consumer = new RefundConsumer(provider.GetRequiredService<IServiceScopeFactory>(), configuration,
                       NullLogger<RefundConsumer>.Instance))
            {
                await consumer.StartAsync(default);
                using (var ready = new CancellationTokenSource(TimeSpan.FromSeconds(10))) await consumer.WaitUntilReadyAsync(ready.Token);
                await Publish(rabbit, exchange, first);
                await Publish(rabbit, exchange, first);
                await Publish(rabbit, exchange, marker);
                await WaitUntilAsync(async () => await Count(database, "SELECT count(*) FROM refund_facts") == 2,
                    TimeSpan.FromSeconds(10));
                await consumer.StopAsync(default);
            }

            PaymentRefundedV1 afterRestart = Event(first.OrganizationId, first.BranchId, DateTimeOffset.UtcNow, 5m, 20m, 50m, "THB");
            using (var consumer = new RefundConsumer(provider.GetRequiredService<IServiceScopeFactory>(), configuration,
                       NullLogger<RefundConsumer>.Instance))
            {
                await consumer.StartAsync(default);
                using (var ready = new CancellationTokenSource(TimeSpan.FromSeconds(10))) await consumer.WaitUntilReadyAsync(ready.Token);
                await Publish(rabbit, exchange, afterRestart);
                await WaitUntilAsync(async () => await Count(database, "SELECT count(*) FROM refund_facts") == 3,
                    TimeSpan.FromSeconds(10));
                await consumer.StopAsync(default);
            }
            Assert.Equal(3L, await Count(database, "SELECT count(*) FROM refund_fact_event_receipts"));
            await using IChannel inspection = await rabbit.CreateChannelAsync();
            Assert.Equal(0u, (await inspection.QueueDeclarePassiveAsync(queue + ".dead")).MessageCount);
        }
        finally
        {
            await using IChannel cleanup = await rabbit.CreateChannelAsync();
            await cleanup.QueueDeleteAsync(queue);
            await cleanup.QueueDeleteAsync(queue + ".dead");
            await cleanup.ExchangeDeleteAsync(exchange);
        }
    }

    [ReportingDatabaseFact]
    public async Task Refund_time_range_is_half_open_and_mixed_currency_fails_closed()
    {
        await using NpgsqlConnection connection = await dataSource!.OpenConnectionAsync();
        await ExecuteAsync(connection, Path.Combine(MigrationPath(), "up.sql"));
        var projection = new RefundFinancialReporting(new RefundFactRepository(dataSource));
        var queries = new ReportingQueries(new ReportingReadRepository(dataSource));
        Guid organization = Guid.NewGuid(), branch = Guid.NewGuid();
        DateTimeOffset start = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), end = start.AddDays(1);
        await projection.ProjectAsync(Event(organization, branch, start, 25m, 25m, 100m, "THB"), default);
        await projection.ProjectAsync(Event(organization, branch, end, 20m, 20m, 100m, "USD"), default);
        await projection.ProjectAsync(Event(organization, Guid.NewGuid(), start, 10m, 10m, 100m, "USD"), default);
        var report = await queries.SalesAsync(organization, branch, start, end, default);
        Assert.Equal(25m, report.RefundedAmount);
        Assert.Equal(-25m, report.NetSales);
        Assert.Equal("THB", report.Currency?.Trim());
        Assert.Empty(report.Items);
        await Assert.ThrowsAsync<REPORTING::NexaConnect.Services.Reporting.Application.MixedReportingCurrencyException>(
            () => queries.SalesAsync(organization, branch, start, end.AddSeconds(1), default));
        await Assert.ThrowsAsync<REPORTING::NexaConnect.Services.Reporting.Application.MixedReportingCurrencyException>(
            () => queries.DashboardAsync(organization, branch, start, end.AddSeconds(1), default));
    }

    public async Task InitializeAsync()
    {
        string? environment = Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (string.IsNullOrWhiteSpace(connectionString) || environment is not ("Development" or "Test" or "Testing")) return;
        schema = $"reporting_refund_it_{Guid.NewGuid():N}";
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema };
        dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        await new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection).ExecuteNonQueryAsync();
        await new NpgsqlCommand(SchemaSql, connection).ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (dataSource is null || schema is null) return;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        await new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", connection).ExecuteNonQueryAsync();
        await dataSource.DisposeAsync();
    }

    private static PaymentRefundedV1 Event(Guid organization, Guid branch, DateTimeOffset occurred,
        decimal amount, decimal cumulative, decimal captured, string currency) =>
        new(Guid.NewGuid(), Guid.NewGuid(), occurred, organization, Guid.NewGuid(), branch, Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), amount, currency, "customer_request", cumulative, captured,
            $"RF-{Guid.NewGuid():N}".ToUpperInvariant());

    private static async Task SeedSaleAndPayment(NpgsqlConnection connection, Guid organization, Guid branch, DateTimeOffset occurred)
    {
        Guid order = Guid.NewGuid();
        await using (var command = new NpgsqlCommand("""
            INSERT INTO sales_facts(order_id,organization_id,branch_id,channel,service_type,currency,subtotal_amount,
              discount_amount,service_charge_amount,tax_amount,total_amount,order_status,ordered_at_utc,completed_at_utc)
            VALUES($1,$2,$3,'pos','dine_in','THB',100,0,0,0,100,'completed',$4,$4)
            """, connection))
        {
            command.Parameters.AddWithValue(order); command.Parameters.AddWithValue(organization);
            command.Parameters.AddWithValue(branch); command.Parameters.AddWithValue(occurred);
            await command.ExecuteNonQueryAsync();
        }
        await using (var command = new NpgsqlCommand("""
            INSERT INTO payment_facts(payment_intent_id,organization_id,branch_id,order_id,currency,paid_amount,
              refunded_amount,paid_at_utc) VALUES($1,$2,$3,$4,'THB',100,0,$5)
            """, connection))
        {
            command.Parameters.AddWithValue(Guid.NewGuid()); command.Parameters.AddWithValue(organization);
            command.Parameters.AddWithValue(branch); command.Parameters.AddWithValue(order);
            command.Parameters.AddWithValue(occurred);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task Publish(IConnection connection, string exchange, PaymentRefundedV1 value)
    {
        await using var transport = new RabbitMqOutboxTransport(connection, Options.Create(new OutboxOptions { Exchange = exchange }));
        await transport.PublishAsync(new OutboxMessage(value.EventId, RefundConsumer.RoutingKey, 1, "payment-refund",
            value.RefundId, System.Text.Json.JsonSerializer.Serialize(value), value.CorrelationId.ToString("D"), value.OccurredAtUtc), default);
    }

    private static async Task<long> Count(NpgsqlConnection connection, string sql) =>
        Convert.ToInt64(await new NpgsqlCommand(sql, connection).ExecuteScalarAsync());
    private static async Task ExecuteAsync(NpgsqlConnection connection, string path) =>
        await new NpgsqlCommand(await File.ReadAllTextAsync(path), connection).ExecuteNonQueryAsync();
    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        DateTimeOffset end = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < end) { if (await condition()) return; await Task.Delay(50); }
        throw new TimeoutException("Refund financial projection did not reach the expected state.");
    }
    private static string MigrationPath() => Path.Combine(FindRepositoryRoot(), "src", "Tools",
        "NexaConnect.DataMigration", "Scripts", "Reporting", "0018_payment_refund_facts");
    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NexaConnect.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private const string SchemaSql = """
        CREATE TABLE sales_facts(order_id uuid PRIMARY KEY,organization_id uuid NOT NULL,branch_id uuid NOT NULL,
          channel text NOT NULL,service_type text NOT NULL,currency char(3) NOT NULL,subtotal_amount numeric(19,4) NOT NULL,
          discount_amount numeric(19,4) NOT NULL,service_charge_amount numeric(19,4) NOT NULL,tax_amount numeric(19,4) NOT NULL,
          total_amount numeric(19,4) NOT NULL,order_status text NOT NULL,ordered_at_utc timestamptz NOT NULL,completed_at_utc timestamptz NULL);
        CREATE TABLE payment_facts(payment_intent_id uuid PRIMARY KEY,organization_id uuid NOT NULL,branch_id uuid NOT NULL,
          order_id uuid NOT NULL,currency char(3) NOT NULL,paid_amount numeric(19,4) NOT NULL,refunded_amount numeric(19,4) NOT NULL,
          paid_at_utc timestamptz NULL);
        CREATE TABLE projection_checkpoints(projector_name text NOT NULL,source_stream text NOT NULL,position bigint NOT NULL,
          last_event_id uuid NULL,last_event_occurred_at_utc timestamptz NULL,updated_at_utc timestamptz NOT NULL,
          PRIMARY KEY(projector_name,source_stream));
        """;
}
