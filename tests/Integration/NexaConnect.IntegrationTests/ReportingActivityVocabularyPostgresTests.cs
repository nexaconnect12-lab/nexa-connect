extern alias REPORTING;

using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using RabbitMQ.Client;
using ReportingActivityCommand = REPORTING::NexaConnect.Services.Reporting.Application.ProjectAuditActivityCommand;
using ReportingActivityRepository = REPORTING::NexaConnect.Services.Reporting.Infrastructure.Persistence.PostgresActivityProjectionRepository;
using ReportingActivityService = REPORTING::NexaConnect.Services.Reporting.Application.ActivityService;
using ReportingActivityProjectionRepository = REPORTING::NexaConnect.Services.Reporting.Application.IActivityProjectionRepository;
using ReportingConsumer = REPORTING::NexaConnect.Services.Reporting.Infrastructure.Messaging.ActivityProjectionConsumer;
using ReportingConsumerOptions = REPORTING::NexaConnect.Services.Reporting.Infrastructure.Messaging.ActivityConsumerOptions;

namespace NexaConnect.IntegrationTests;

public sealed class ReportingActivityVocabularyPostgresTests : IAsyncLifetime
{
    private readonly string? connectionString = Environment.GetEnvironmentVariable("NEXACONNECT_REPORTING_INTEGRATION_DB");
    private NpgsqlDataSource? dataSource;
    private string? schema;

    [ReportingDatabaseFact]
    public async Task Migration_4_accepts_payment_audit_and_downgrade_removes_incompatible_projection()
    {
        string migration = Path.Combine(FindRepositoryRoot(), "src", "Tools", "NexaConnect.DataMigration", "Scripts", "Reporting", "0004_activity_vocabulary");
        await using NpgsqlConnection connection = await dataSource!.OpenConnectionAsync();
        await ExecuteAsync(connection, Path.Combine(migration, "up.sql"));
        var repository = new ReportingActivityRepository(dataSource!);
        var audit = new PlatformAuditEventV1(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, "payment-service", Guid.NewGuid(), "payment.intent.created", "payment-intent", Guid.NewGuid().ToString("D"), "succeeded");
        Assert.True(await repository.ProjectAsync(new ReportingActivityCommand(audit, "nexa_connect", "payment"), CancellationToken.None));
        await using (var inbox = new NpgsqlCommand("INSERT INTO inbox_messages(message_id,consumer_name,status,attempts,processed_at_utc) VALUES($1,'reporting.activity.v1','completed',1,now())", connection)) { inbox.Parameters.AddWithValue(audit.EventId); await inbox.ExecuteNonQueryAsync(); }
        await ExecuteAsync(connection, Path.Combine(migration, "down.sql"));
        Assert.Equal(0L, (long)(await new NpgsqlCommand("SELECT count(*) FROM activity_records", connection).ExecuteScalarAsync() ?? 0L));
        Assert.Equal(0L, (long)(await new NpgsqlCommand("SELECT count(*) FROM inbox_messages", connection).ExecuteScalarAsync() ?? 0L));
        await ExecuteAsync(connection, Path.Combine(migration, "up.sql"));
        var inboxStore = new PostgresInboxStore(dataSource!);
        Assert.Equal(InboxClaimResult.Claimed, await inboxStore.ClaimAsync(audit.EventId, "reporting.activity.v1", TimeSpan.FromMinutes(2), CancellationToken.None));
        Assert.True(await repository.ProjectAsync(new ReportingActivityCommand(audit, "nexa_connect", "payment"), CancellationToken.None));
        await inboxStore.MarkCompletedAsync(audit.EventId, "reporting.activity.v1", CancellationToken.None);
    }

    [ReportingDatabaseFact]
    public async Task Migration_5_accepts_kitchen_audit_and_replays_after_re_upgrade()
    {
        string root = Path.Combine(FindRepositoryRoot(), "src", "Tools", "NexaConnect.DataMigration", "Scripts", "Reporting");
        await using NpgsqlConnection connection = await dataSource!.OpenConnectionAsync(); await ExecuteAsync(connection, Path.Combine(root, "0004_activity_vocabulary", "up.sql")); string migration = Path.Combine(root, "0005_kitchen_activity_vocabulary"); await ExecuteAsync(connection, Path.Combine(migration, "up.sql")); var repository = new ReportingActivityRepository(dataSource!); var audit = new PlatformAuditEventV1(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, "kitchen-operator", Guid.NewGuid(), "kitchen.ticket.ready", "kitchen-ticket", Guid.NewGuid().ToString("D"), "succeeded"); Assert.True(await repository.ProjectAsync(new ReportingActivityCommand(audit, "nexa_connect", "kitchen"), default)); await using (var inbox = new NpgsqlCommand("INSERT INTO inbox_messages(message_id,consumer_name,status,attempts,processed_at_utc) VALUES($1,'reporting.activity.v1','completed',1,now())", connection)) { inbox.Parameters.AddWithValue(audit.EventId); await inbox.ExecuteNonQueryAsync(); }
        await ExecuteAsync(connection, Path.Combine(migration, "down.sql")); Assert.Equal(0L, (long)(await new NpgsqlCommand("SELECT count(*) FROM activity_records", connection).ExecuteScalarAsync() ?? 0L)); await ExecuteAsync(connection, Path.Combine(migration, "up.sql")); var store = new PostgresInboxStore(dataSource!); Assert.Equal(InboxClaimResult.Claimed, await store.ClaimAsync(audit.EventId, "reporting.activity.v1", TimeSpan.FromMinutes(2), default)); Assert.True(await repository.ProjectAsync(new ReportingActivityCommand(audit, "nexa_connect", "kitchen"), default));
    }

    [ReportingDatabaseFact]
    public async Task Migration_6_accepts_customer_audit_and_replays_after_re_upgrade()
    {
        string root = Path.Combine(FindRepositoryRoot(), "src", "Tools", "NexaConnect.DataMigration", "Scripts", "Reporting");
        await using NpgsqlConnection connection = await dataSource!.OpenConnectionAsync();
        await ExecuteAsync(connection, Path.Combine(root, "0004_activity_vocabulary", "up.sql"));
        await ExecuteAsync(connection, Path.Combine(root, "0005_kitchen_activity_vocabulary", "up.sql"));
        string migration = Path.Combine(root, "0006_customer_activity_vocabulary");
        await ExecuteAsync(connection, Path.Combine(migration, "up.sql"));
        var repository = new ReportingActivityRepository(dataSource!);
        var audit = new PlatformAuditEventV1(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, "customer-user", Guid.NewGuid(), "customer.profile.created", "customer-profile", Guid.NewGuid().ToString("D"), "succeeded");
        Assert.True(await repository.ProjectAsync(new ReportingActivityCommand(audit, "nexa_connect", "customer"), default));
        await using (var inbox = new NpgsqlCommand("INSERT INTO inbox_messages(message_id,consumer_name,status,attempts,processed_at_utc) VALUES($1,'reporting.activity.v1','completed',1,now())", connection)) { inbox.Parameters.AddWithValue(audit.EventId); await inbox.ExecuteNonQueryAsync(); }
        await ExecuteAsync(connection, Path.Combine(migration, "down.sql"));
        await using (var count = new NpgsqlCommand("SELECT count(*) FROM activity_records WHERE event_id=$1", connection)) { count.Parameters.AddWithValue(audit.EventId); Assert.Equal(0L, (long)(await count.ExecuteScalarAsync() ?? 0L)); }
        await ExecuteAsync(connection, Path.Combine(migration, "up.sql"));
        var store = new PostgresInboxStore(dataSource!);
        Assert.Equal(InboxClaimResult.Claimed, await store.ClaimAsync(audit.EventId, "reporting.activity.v1", TimeSpan.FromMinutes(2), default));
        Assert.True(await repository.ProjectAsync(new ReportingActivityCommand(audit, "nexa_connect", "customer"), default));
    }

    [ReportingDatabaseFact]
    public async Task Migration_7_accepts_notification_delivery_audit_and_replays_after_re_upgrade()
    {
        string root = Path.Combine(FindRepositoryRoot(), "src", "Tools", "NexaConnect.DataMigration", "Scripts", "Reporting");
        await using NpgsqlConnection connection = await dataSource!.OpenConnectionAsync();
        foreach (string version in new[] { "0004_activity_vocabulary", "0005_kitchen_activity_vocabulary", "0006_customer_activity_vocabulary" }) await ExecuteAsync(connection, Path.Combine(root, version, "up.sql"));
        string migration = Path.Combine(root, "0007_notification_delivery_vocabulary");
        await ExecuteAsync(connection, Path.Combine(migration, "up.sql"));
        var repository = new ReportingActivityRepository(dataSource!);
        var audit = new PlatformAuditEventV1(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, "service:notification-delivery", Guid.NewGuid(), "notification.delivered", "notification", Guid.NewGuid().ToString("D"), "succeeded");
        Assert.True(await repository.ProjectAsync(new ReportingActivityCommand(audit, "nexa_connect", "notification"), default));
        await using (var inbox = new NpgsqlCommand("INSERT INTO inbox_messages(message_id,consumer_name,status,attempts,processed_at_utc) VALUES($1,'reporting.activity.v1','completed',1,now())", connection)) { inbox.Parameters.AddWithValue(audit.EventId); await inbox.ExecuteNonQueryAsync(); }
        await ExecuteAsync(connection, Path.Combine(migration, "down.sql"));
        Assert.Equal(0L, (long)(await new NpgsqlCommand("SELECT count(*) FROM activity_records", connection).ExecuteScalarAsync() ?? 0L));
        await ExecuteAsync(connection, Path.Combine(migration, "up.sql"));
        var store = new PostgresInboxStore(dataSource!);
        Assert.Equal(InboxClaimResult.Claimed, await store.ClaimAsync(audit.EventId, "reporting.activity.v1", TimeSpan.FromMinutes(2), default));
        Assert.True(await repository.ProjectAsync(new ReportingActivityCommand(audit, "nexa_connect", "notification"), default));
    }

    [ReportingDatabaseFact]
    public async Task Migration_11_accepts_capture_reconciliation_audit_and_replays_after_re_upgrade()
    {
        string root = Path.Combine(FindRepositoryRoot(), "src", "Tools", "NexaConnect.DataMigration", "Scripts", "Reporting");
        await using NpgsqlConnection connection = await dataSource!.OpenConnectionAsync();
        foreach (string version in new[] { "0004_activity_vocabulary", "0005_kitchen_activity_vocabulary", "0006_customer_activity_vocabulary", "0007_notification_delivery_vocabulary", "0008_payment_authorization_vocabulary", "0009_payment_authorization_reconciliation", "0010_payment_capture_vocabulary" })
            await ExecuteAsync(connection, Path.Combine(root, version, "up.sql"));
        string migration = Path.Combine(root, "0011_payment_capture_reconciliation");
        await ExecuteAsync(connection, Path.Combine(migration, "up.sql"));
        var repository = new ReportingActivityRepository(dataSource!);
        var audit = new PlatformAuditEventV1(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, "payment-recovery-worker", Guid.NewGuid(), "payment.capture.reconciled", "payment-intent", Guid.NewGuid().ToString("D"), "succeeded");
        Assert.True(await repository.ProjectAsync(new ReportingActivityCommand(audit, "nexa_connect", "payment"), default));
        await using (var inbox = new NpgsqlCommand("INSERT INTO inbox_messages(message_id,consumer_name,status,attempts,processed_at_utc) VALUES($1,'reporting.activity.v1','completed',1,now())", connection)) { inbox.Parameters.AddWithValue(audit.EventId); await inbox.ExecuteNonQueryAsync(); }
        await ExecuteAsync(connection, Path.Combine(migration, "down.sql"));
        await using (var count = new NpgsqlCommand("SELECT count(*) FROM activity_records WHERE event_id=$1", connection)) { count.Parameters.AddWithValue(audit.EventId); Assert.Equal(0L, (long)(await count.ExecuteScalarAsync() ?? 0L)); }
        await ExecuteAsync(connection, Path.Combine(migration, "up.sql"));
        var store = new PostgresInboxStore(dataSource!);
        Assert.Equal(InboxClaimResult.Claimed, await store.ClaimAsync(audit.EventId, "reporting.activity.v1", TimeSpan.FromMinutes(2), default));
        Assert.True(await repository.ProjectAsync(new ReportingActivityCommand(audit, "nexa_connect", "payment"), default));
    }

    [ReportingDatabaseFact]
    public async Task Migration_13_accepts_payment_review_resolution_and_replays_after_re_upgrade()
    {
        string root = Path.Combine(FindRepositoryRoot(), "src", "Tools", "NexaConnect.DataMigration", "Scripts", "Reporting");
        await using NpgsqlConnection connection = await dataSource!.OpenConnectionAsync();
        foreach (string version in new[] { "0004_activity_vocabulary", "0005_kitchen_activity_vocabulary", "0006_customer_activity_vocabulary", "0007_notification_delivery_vocabulary", "0008_payment_authorization_vocabulary", "0009_payment_authorization_reconciliation", "0010_payment_capture_vocabulary", "0011_payment_capture_reconciliation", "0012_payment_void_vocabulary" })
            await ExecuteAsync(connection, Path.Combine(root, version, "up.sql"));
        string migration = Path.Combine(root, "0013_order_payment_review_vocabulary");
        await ExecuteAsync(connection, Path.Combine(migration, "up.sql"));
        var repository = new ReportingActivityRepository(dataSource!);
        var audit = new PlatformAuditEventV1(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, "payment-review-operator", Guid.NewGuid(), "order.payment-review.resolved", "order", Guid.NewGuid().ToString("D"), "succeeded");
        Assert.True(await repository.ProjectAsync(new ReportingActivityCommand(audit, "nexa_connect", "order"), default));
        await using (var inbox = new NpgsqlCommand("INSERT INTO inbox_messages(message_id,consumer_name,status,attempts,processed_at_utc) VALUES($1,'reporting.activity.v1','completed',1,now())", connection)) { inbox.Parameters.AddWithValue(audit.EventId); await inbox.ExecuteNonQueryAsync(); }
        await ExecuteAsync(connection, Path.Combine(migration, "down.sql"));
        await using (var count = new NpgsqlCommand("SELECT count(*) FROM activity_records WHERE event_id=$1", connection)) { count.Parameters.AddWithValue(audit.EventId); Assert.Equal(0L, (long)(await count.ExecuteScalarAsync() ?? 0L)); }
        await using (var count = new NpgsqlCommand("SELECT count(*) FROM inbox_messages WHERE message_id=$1 AND consumer_name='reporting.activity.v1'", connection)) { count.Parameters.AddWithValue(audit.EventId); Assert.Equal(0L, (long)(await count.ExecuteScalarAsync() ?? 0L)); }
        await ExecuteAsync(connection, Path.Combine(migration, "up.sql"));
        var store = new PostgresInboxStore(dataSource!);
        Assert.Equal(InboxClaimResult.Claimed, await store.ClaimAsync(audit.EventId, "reporting.activity.v1", TimeSpan.FromMinutes(2), default));
        Assert.True(await repository.ProjectAsync(new ReportingActivityCommand(audit, "nexa_connect", "order"), default));
        await store.MarkCompletedAsync(audit.EventId, "reporting.activity.v1", default);
    }

    [ReportingRabbitFact]
    public async Task Hosted_consumer_projects_repository_compatible_order_audit_once_after_duplicate_delivery()
    {
        string root = Path.Combine(FindRepositoryRoot(), "src", "Tools", "NexaConnect.DataMigration", "Scripts", "Reporting");
        await using NpgsqlConnection database = await dataSource!.OpenConnectionAsync();
        foreach (string version in new[] { "0004_activity_vocabulary", "0005_kitchen_activity_vocabulary", "0006_customer_activity_vocabulary", "0007_notification_delivery_vocabulary", "0008_payment_authorization_vocabulary", "0009_payment_authorization_reconciliation", "0010_payment_capture_vocabulary", "0011_payment_capture_reconciliation", "0012_payment_void_vocabulary", "0013_order_payment_review_vocabulary" })
            await ExecuteAsync(database, Path.Combine(root, version, "up.sql"));

        string exchange = $"nexaconnect.reporting.payment-review.{Guid.NewGuid():N}";
        string queue = $"nexaconnect.reporting.payment-review.{Guid.NewGuid():N}";
        await using IConnection rabbit = await new ConnectionFactory { Uri = new Uri(Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_INTEGRATION_URI")!) }.CreateConnectionAsync();
        var services = new ServiceCollection();
        services.AddSingleton(dataSource!); services.AddScoped<ReportingActivityProjectionRepository, ReportingActivityRepository>(); services.AddScoped<ReportingActivityService>();
        using ServiceProvider provider = services.BuildServiceProvider();
        var consumer = new ReportingConsumer(rabbit, Options.Create(new ReportingConsumerOptions { Enabled = true, Exchange = exchange, Queue = queue, PrefetchCount = 1 }), new PostgresInboxStore(dataSource!), provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<ReportingConsumer>.Instance);
        try
        {
            await consumer.StartAsync(default);
            using (var readiness = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                await consumer.WaitUntilReadyAsync(readiness.Token);
            var audit = new PlatformAuditEventV1(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, "payment-review-operator", Guid.NewGuid(), "order.payment-review.resolved", "order", Guid.NewGuid().ToString("D"), "succeeded");
            await using var transport = new RabbitMqOutboxTransport(rabbit, Options.Create(new OutboxOptions { Exchange = exchange }));
            var message = new OutboxMessage(audit.EventId, "order.audit.v1", 1, "Order", Guid.Parse(audit.ResourceId), System.Text.Json.JsonSerializer.Serialize(audit), audit.CorrelationId.ToString("D"), audit.OccurredAtUtc);
            await transport.PublishAsync(message, default); await transport.PublishAsync(message, default);
            // Prefetch=1 makes this subsequent delivery a barrier: the duplicate must
            // have been acknowledged before the broker can deliver the marker.
            var marker = audit with { EventId = Guid.NewGuid() };
            await transport.PublishAsync(message with { Id = marker.EventId, Payload = System.Text.Json.JsonSerializer.Serialize(marker) }, default);
            await WaitUntilAsync(async () => await CountAsync(database, "SELECT count(*) FROM inbox_messages WHERE message_id=$1 AND consumer_name='reporting.activity.v1' AND status='completed'", marker.EventId) == 1, TimeSpan.FromSeconds(5));
            await WaitUntilAsync(async () => await CountAsync(database, "SELECT count(*) FROM activity_records WHERE event_id=$1", audit.EventId) == 1 && await CountAsync(database, "SELECT count(*) FROM inbox_messages WHERE message_id=$1 AND consumer_name='reporting.activity.v1' AND status='completed'", audit.EventId) == 1, TimeSpan.FromSeconds(5));
            Assert.Equal(1, await CountAsync(database, "SELECT attempts FROM inbox_messages WHERE message_id=$1 AND consumer_name='reporting.activity.v1'", audit.EventId));
            await using (var inspection = await rabbit.CreateChannelAsync())
            { Assert.Equal(0u, (await inspection.QueueDeclarePassiveAsync(queue + ".dead")).MessageCount); }
            Assert.Equal(1, await CountAsync(database, "SELECT count(*) FROM activity_records WHERE event_id=$1", audit.EventId));
        }
        finally
        {
            await consumer.StopAsync(default); consumer.Dispose();
            await using IChannel channel = await rabbit.CreateChannelAsync();
            await channel.QueueDeleteAsync(queue); await channel.QueueDeleteAsync(queue + ".dead"); await channel.ExchangeDeleteAsync(exchange);
        }
    }

    public async Task InitializeAsync() { string? environment = Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"); if (string.IsNullOrWhiteSpace(connectionString) || environment is not ("Development" or "Test" or "Testing")) return; schema = $"reporting_vocabulary_it_{Guid.NewGuid():N}"; var builder = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema }; dataSource = NpgsqlDataSource.Create(builder.ConnectionString); await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(); await new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection).ExecuteNonQueryAsync(); await new NpgsqlCommand(SchemaSql, connection).ExecuteNonQueryAsync(); }
    public async Task DisposeAsync() { if (dataSource is null || schema is null) return; await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(); await new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", connection).ExecuteNonQueryAsync(); await dataSource.DisposeAsync(); }
    private static async Task ExecuteAsync(NpgsqlConnection connection, string path) { await using var command = new NpgsqlCommand(await File.ReadAllTextAsync(path), connection); await command.ExecuteNonQueryAsync(); }
    private static async Task<long> CountAsync(NpgsqlConnection connection, string sql, Guid id) { await using var command = new NpgsqlCommand(sql, connection); command.Parameters.AddWithValue(id); return Convert.ToInt64(await command.ExecuteScalarAsync()); }
    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout) { DateTimeOffset end = DateTimeOffset.UtcNow + timeout; while (DateTimeOffset.UtcNow < end) { if (await condition()) return; await Task.Delay(50); } throw new TimeoutException("Reporting consumer did not reach the expected state."); }
    private static string FindRepositoryRoot() { DirectoryInfo? directory = new(AppContext.BaseDirectory); while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NexaConnect.sln"))) directory = directory.Parent; return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root."); }
    private const string SchemaSql = """
        CREATE TABLE activity_records(event_id uuid PRIMARY KEY,organization_id uuid NOT NULL,application_code text NOT NULL,source_service text NOT NULL,actor_subject_id text NOT NULL,action text NOT NULL,resource_type text NOT NULL,resource_id text NOT NULL,outcome text NOT NULL,occurred_at_utc timestamptz NOT NULL,projected_at_utc timestamptz NOT NULL,CONSTRAINT ck_activity_records_text CHECK(application_code='nexa_connect' AND char_length(source_service) BETWEEN 1 AND 64 AND char_length(actor_subject_id) BETWEEN 1 AND 200 AND char_length(resource_id) BETWEEN 1 AND 300),CONSTRAINT ck_activity_records_action CHECK(action IN('customer-membership.changed','branch.created','branch.updated','branch.configuration.updated','media.asset.created','media.asset.deleted')),CONSTRAINT ck_activity_records_resource CHECK(resource_type IN('organization-membership','branch','branch-configuration','media-asset')),CONSTRAINT ck_activity_records_outcome CHECK(outcome IN('succeeded','failed','denied')));
        CREATE TABLE inbox_messages(message_id uuid NOT NULL,consumer_name text NOT NULL,status text NOT NULL,attempts integer NOT NULL DEFAULT 0,locked_until_utc timestamptz NULL,processed_at_utc timestamptz NULL,last_error_category text NULL,PRIMARY KEY(message_id,consumer_name));
        """;
}

public class ReportingDatabaseFactAttribute : FactAttribute
{
    public ReportingDatabaseFactAttribute() { string? environment = Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"); if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NEXACONNECT_REPORTING_INTEGRATION_DB")) || environment is not ("Development" or "Test" or "Testing")) Skip = "NEXACONNECT_REPORTING_INTEGRATION_DB and a safe environment are required."; }
}

public sealed class ReportingRabbitFactAttribute : ReportingDatabaseFactAttribute
{
    public ReportingRabbitFactAttribute() { if (Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_ACCEPTANCE") != "1" || !Uri.TryCreate(Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_INTEGRATION_URI"), UriKind.Absolute, out _)) Skip = "Reporting payment-review RabbitMQ acceptance requires its opt-in flag and URI."; }
}
