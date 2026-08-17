extern alias NOTIFICATION;

using Microsoft.Extensions.Options;
using NexaConnect.Infrastructure.Messaging;
using Npgsql;
using RabbitMQ.Client;
using DeliveryOperation = NOTIFICATION::NexaConnect.Services.Notification.Application.Delivery.NotificationDeliveryOperation;
using DeliveryPolicy = NOTIFICATION::NexaConnect.Services.Notification.Application.Delivery.NotificationDeliveryPolicy;
using MutationContext = NOTIFICATION::NexaConnect.Services.Notification.Application.Messages.NotificationMutationContext;
using ProviderOutcome = NOTIFICATION::NexaConnect.Services.Notification.Application.Delivery.NotificationProviderOutcome;
using ProviderResult = NOTIFICATION::NexaConnect.Services.Notification.Application.Delivery.NotificationProviderResult;
using DeliveryRepository = NOTIFICATION::NexaConnect.Services.Notification.Infrastructure.PostgresNotificationDeliveryRepository;
using NotificationSender = NOTIFICATION::NexaConnect.Services.Notification.Infrastructure.PostgresNotificationSender;
using SendNotification = NOTIFICATION::NexaConnect.Services.Notification.Application.Messages.SendNotification;
using IdempotencyConflict = NOTIFICATION::NexaConnect.Services.Notification.Domain.NotificationIdempotencyConflictException;

namespace NexaConnect.IntegrationTests;

public sealed class NotificationDeliveryPostgresTests : IAsyncLifetime
{
    private readonly string? connectionString = Environment.GetEnvironmentVariable("NEXACONNECT_NOTIFICATION_INTEGRATION_DB");
    private NpgsqlDataSource? dataSource;
    private string? schema;

    [NotificationDatabaseFact]
    public async Task Accepted_notification_is_reconciled_to_delivered_without_content_in_events()
    {
        var sender = new NotificationSender(dataSource!);
        Guid correlation = Guid.NewGuid();
        var queued = sender.Send(new SendNotification(Guid.NewGuid(), "email", "private@example.test", "Private subject",
            "Private body"), new MutationContext("operator", correlation, correlation.ToString("D")));
        var repository = new DeliveryRepository(dataSource!);

        var submission = await repository.ClaimDueAsync(TimeSpan.FromMinutes(1), default);
        Assert.NotNull(submission);
        Assert.Equal(DeliveryOperation.Submit, submission!.Operation);
        Assert.Null(await repository.ClaimDueAsync(TimeSpan.FromMinutes(1), default));
        var accepted = new ProviderResult(ProviderOutcome.Accepted, "test-provider", "receipt-1");
        await repository.RecordAsync(submission, accepted,
            DeliveryPolicy.Decide(submission, accepted, 4, DateTimeOffset.UtcNow), default);

        await ExecuteAsync("UPDATE notifications SET next_receipt_attempt_at_utc=now() WHERE id=$1", queued.Id);
        var receipt = await repository.ClaimDueAsync(TimeSpan.FromMinutes(1), default);
        Assert.NotNull(receipt);
        Assert.Equal(DeliveryOperation.Reconcile, receipt!.Operation);
        var delivered = new ProviderResult(ProviderOutcome.Delivered, "test-provider", "receipt-1");
        await repository.RecordAsync(receipt, delivered,
            DeliveryPolicy.Decide(receipt, delivered, 4, DateTimeOffset.UtcNow), default);

        Assert.Equal("delivered", await ScalarAsync<string>("SELECT status FROM notifications WHERE id=$1", queued.Id));
        Assert.Equal(2L, await ScalarAsync<long>("SELECT count(*) FROM notification_delivery_attempts WHERE notification_id=$1", queued.Id));
        Assert.Equal(3L, await ScalarAsync<long>("SELECT count(*) FROM notification_audit_records WHERE notification_id=$1", queued.Id));
        string payloads = await ScalarAsync<string>("SELECT COALESCE(string_agg(payload::text,' '),'') FROM outbox_messages WHERE aggregate_id=$1", queued.Id);
        Assert.DoesNotContain("private@example.test", payloads, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Private subject", payloads, StringComparison.Ordinal);
        Assert.DoesNotContain("Private body", payloads, StringComparison.Ordinal);
    }

    [NotificationDatabaseFact]
    public void Source_event_replay_cannot_cross_an_organization_boundary()
    {
        var sender = new NotificationSender(dataSource!);
        Guid sourceEvent = Guid.NewGuid();
        Guid correlation = Guid.NewGuid();
        var command = new SendNotification(Guid.NewGuid(), "email", "private@example.test", "Subject", "Body", sourceEvent);
        var first = sender.Send(command, new MutationContext("service:order", correlation, correlation.ToString("D")));
        var replay = sender.Send(command, new MutationContext("service:order", correlation, correlation.ToString("D")));
        Assert.Equal(first.Id, replay.Id);
        Assert.Throws<IdempotencyConflict>(() => sender.Send(command with { OrganizationId = Guid.NewGuid() },
            new MutationContext("service:order", correlation, correlation.ToString("D"))));
    }

    [NotificationDatabaseFact]
    public async Task Transient_submission_failure_is_bounded_and_becomes_terminal()
    {
        var sender = new NotificationSender(dataSource!);
        Guid correlation = Guid.NewGuid();
        var queued = sender.Send(new SendNotification(Guid.NewGuid(), "sms", "+15555550100", "Alert", "Private body"),
            new MutationContext("operator", correlation, correlation.ToString("D")));
        var repository = new DeliveryRepository(dataSource!);

        var first = Assert.IsType<NOTIFICATION::NexaConnect.Services.Notification.Application.Delivery.NotificationDeliveryWork>(
            await repository.ClaimDueAsync(TimeSpan.FromMinutes(1), default));
        var firstFailure = new ProviderResult(ProviderOutcome.TransientFailure, "test-provider",
            ErrorCategory: "http_503");
        await repository.RecordAsync(first, firstFailure,
            DeliveryPolicy.Decide(first, firstFailure, 2, DateTimeOffset.UtcNow), default);
        Assert.Equal("retry_scheduled", await ScalarAsync<string>("SELECT status FROM notifications WHERE id=$1", queued.Id));

        await ExecuteAsync("UPDATE notifications SET next_delivery_attempt_at_utc=now() WHERE id=$1", queued.Id);
        var second = Assert.IsType<NOTIFICATION::NexaConnect.Services.Notification.Application.Delivery.NotificationDeliveryWork>(
            await repository.ClaimDueAsync(TimeSpan.FromMinutes(1), default));
        var secondFailure = new ProviderResult(ProviderOutcome.TransientFailure, "test-provider",
            ErrorCategory: "http_503");
        await repository.RecordAsync(second, secondFailure,
            DeliveryPolicy.Decide(second, secondFailure, 2, DateTimeOffset.UtcNow), default);

        Assert.Equal("delivery_failed", await ScalarAsync<string>("SELECT status FROM notifications WHERE id=$1", queued.Id));
        Assert.Equal(2L, await ScalarAsync<long>("SELECT count(*) FROM notification_delivery_attempts WHERE notification_id=$1", queued.Id));
        Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM notification_audit_records WHERE notification_id=$1 AND action='notification.delivery.failed'", queued.Id));
    }

    [NotificationDatabaseFact]
    public async Task Migration_3_downgrades_and_reapplies_cleanly()
    {
        string migration = Path.Combine(FindRepositoryRoot(), "src", "Tools", "NexaConnect.DataMigration", "Scripts",
            "Notification", "0003_provider_delivery");
        await ExecuteScriptAsync(Path.Combine(migration, "down.sql"));
        Assert.Equal(0L, await ScalarAsync<long>(
            "SELECT count(*) FROM information_schema.tables WHERE table_schema=current_schema() AND table_name='notification_delivery_attempts'"));
        await ExecuteScriptAsync(Path.Combine(migration, "up.sql"));
        Assert.Equal(1L, await ScalarAsync<long>(
            "SELECT count(*) FROM information_schema.tables WHERE table_schema=current_schema() AND table_name='notification_delivery_attempts'"));
    }

    [NotificationDatabaseFact]
    public async Task Expired_submission_lease_is_reclaimed_with_the_same_notification_identity()
    {
        var sender = new NotificationSender(dataSource!);
        Guid correlation = Guid.NewGuid();
        var queued = sender.Send(new SendNotification(Guid.NewGuid(), "push", "device-reference", "Alert", "Body"),
            new MutationContext("operator", correlation, correlation.ToString("D")));
        var repository = new DeliveryRepository(dataSource!);
        var abandoned = Assert.IsType<NOTIFICATION::NexaConnect.Services.Notification.Application.Delivery.NotificationDeliveryWork>(
            await repository.ClaimDueAsync(TimeSpan.FromMinutes(1), default));
        await ExecuteAsync("UPDATE notifications SET delivery_locked_until_utc=now()-interval '1 second' WHERE id=$1", queued.Id);

        var reclaimed = Assert.IsType<NOTIFICATION::NexaConnect.Services.Notification.Application.Delivery.NotificationDeliveryWork>(
            await repository.ClaimDueAsync(TimeSpan.FromMinutes(1), default));

        Assert.Equal(abandoned.NotificationId, reclaimed.NotificationId);
        Assert.NotEqual(abandoned.LeaseId, reclaimed.LeaseId);
        Assert.Equal(2, reclaimed.AttemptNumber);
    }

    [NotificationDatabaseFact]
    public async Task Lifecycle_state_and_attempt_roll_back_when_outbox_write_fails()
    {
        var sender = new NotificationSender(dataSource!);
        Guid correlation = Guid.NewGuid();
        var queued = sender.Send(new SendNotification(Guid.NewGuid(), "email", "private@example.test", "Alert", "Body"),
            new MutationContext("operator", correlation, correlation.ToString("D")));
        var repository = new DeliveryRepository(dataSource!);
        var submission = Assert.IsType<NOTIFICATION::NexaConnect.Services.Notification.Application.Delivery.NotificationDeliveryWork>(
            await repository.ClaimDueAsync(TimeSpan.FromMinutes(1), default));
        var accepted = new ProviderResult(ProviderOutcome.Accepted, "test-provider", "receipt-rollback");
        await ExecuteStatementAsync("ALTER TABLE outbox_messages RENAME TO unavailable_outbox_messages");
        try
        {
            await Assert.ThrowsAsync<PostgresException>(() => repository.RecordAsync(submission, accepted,
                DeliveryPolicy.Decide(submission, accepted, 4, DateTimeOffset.UtcNow), default));
        }
        finally
        {
            await ExecuteStatementAsync("ALTER TABLE unavailable_outbox_messages RENAME TO outbox_messages");
        }

        Assert.Equal("submitting", await ScalarAsync<string>("SELECT status FROM notifications WHERE id=$1", queued.Id));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM notification_delivery_attempts WHERE notification_id=$1", queued.Id));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM notification_audit_records WHERE notification_id=$1 AND action='notification.delivery.accepted'", queued.Id));
    }

    [NotificationRabbitFact]
    public async Task Broker_recovery_publishes_confirmed_delivery_lifecycle_messages()
    {
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            var unavailable = new ConnectionFactory
            {
                Uri = new Uri("amqp://guest:guest@127.0.0.1:1"),
                RequestedConnectionTimeout = TimeSpan.FromSeconds(1)
            };
            await using IConnection ignored = await unavailable.CreateConnectionAsync();
        });
        var sender = new NotificationSender(dataSource!);
        Guid correlation = Guid.NewGuid();
        var queued = sender.Send(new SendNotification(Guid.NewGuid(), "email", "private@example.test", "Alert", "Body"),
            new MutationContext("operator", correlation, correlation.ToString("D")));
        var repository = new DeliveryRepository(dataSource!);
        var submission = Assert.IsType<NOTIFICATION::NexaConnect.Services.Notification.Application.Delivery.NotificationDeliveryWork>(
            await repository.ClaimDueAsync(TimeSpan.FromMinutes(1), default));
        var accepted = new ProviderResult(ProviderOutcome.Accepted, "test-provider", "receipt-broker");
        await repository.RecordAsync(submission, accepted,
            DeliveryPolicy.Decide(submission, accepted, 4, DateTimeOffset.UtcNow), default);

        string exchange = $"nexaconnect.notification.phase10.{Guid.NewGuid():N}";
        string queue = $"nexaconnect.notification.phase10.{Guid.NewGuid():N}";
        await using IConnection connection = await new ConnectionFactory
        {
            Uri = new Uri(Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_INTEGRATION_URI")!)
        }.CreateConnectionAsync();
        await using IChannel channel = await connection.CreateChannelAsync();
        try
        {
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false);
            await channel.QueueDeclareAsync(queue, durable: false, exclusive: true, autoDelete: true);
            await channel.QueueBindAsync(queue, exchange, "notification.#");
            var transport = new RabbitMqOutboxTransport(connection, Options.Create(new OutboxOptions { Exchange = exchange }));
            var store = new PostgresOutboxStore(dataSource!);
            OutboxMessage[] pending = (await store.ClaimBatchAsync(20, default))
                .Where(message => message.AggregateId == queued.Id).ToArray();
            Assert.Equal(4, pending.Length);
            foreach (OutboxMessage message in pending)
            {
                await transport.PublishAsync(message, default);
                await store.MarkPublishedAsync(message.Id, default);
            }

            var deliveries = new List<BasicGetResult>();
            for (int attempt = 0; attempt < 30 && deliveries.Count < 4; attempt++)
            {
                BasicGetResult? delivery = await channel.BasicGetAsync(queue, autoAck: true);
                if (delivery is null) await Task.Delay(100);
                else deliveries.Add(delivery);
            }
            Assert.Equal(["notification.audit.v1", "notification.audit.v1", "notification.delivery-status-changed.v1", "notification.queued.v1"],
                deliveries.Select(delivery => delivery.RoutingKey).Order().ToArray());
            Assert.All(deliveries, delivery => Assert.True(delivery.BasicProperties.Persistent));
        }
        finally
        {
            await channel.ExchangeDeleteAsync(exchange);
        }
    }

    public async Task InitializeAsync()
    {
        string? environment = Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (string.IsNullOrWhiteSpace(connectionString) || environment is not ("Development" or "Test" or "Testing")) return;
        schema = $"notification_delivery_it_{Guid.NewGuid():N}";
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema };
        dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        await new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection).ExecuteNonQueryAsync();
        string root = Path.Combine(FindRepositoryRoot(), "src", "Tools", "NexaConnect.DataMigration", "Scripts", "Notification");
        foreach (string migration in new[] { "0001_initial_schema", "0002_product_integration", "0003_provider_delivery" })
            await new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(root, migration, "up.sql")), connection).ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (dataSource is null || schema is null) return;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        await new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", connection).ExecuteNonQueryAsync();
        await dataSource.DisposeAsync();
    }

    private async Task ExecuteAsync(string sql, Guid id)
    {
        await using var command = dataSource!.CreateCommand(sql);
        command.Parameters.AddWithValue(id);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql, Guid id)
    {
        await using var command = dataSource!.CreateCommand(sql);
        command.Parameters.AddWithValue(id);
        return (T)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Expected a scalar value."));
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var command = dataSource!.CreateCommand(sql);
        return (T)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Expected a scalar value."));
    }

    private async Task ExecuteScriptAsync(string path)
    {
        await using var command = dataSource!.CreateCommand(await File.ReadAllTextAsync(path));
        await command.ExecuteNonQueryAsync();
    }

    private async Task ExecuteStatementAsync(string sql)
    {
        await using var command = dataSource!.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NexaConnect.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}

public class NotificationDatabaseFactAttribute : FactAttribute
{
    public NotificationDatabaseFactAttribute()
    {
        string? environment = Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NEXACONNECT_NOTIFICATION_INTEGRATION_DB"))
            || environment is not ("Development" or "Test" or "Testing"))
            Skip = "NEXACONNECT_NOTIFICATION_INTEGRATION_DB and a safe environment are required.";
    }
}

public sealed class NotificationRabbitFactAttribute : NotificationDatabaseFactAttribute
{
    public NotificationRabbitFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_ACCEPTANCE") != "1"
            || !Uri.TryCreate(Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_INTEGRATION_URI"),
                UriKind.Absolute, out _))
            Skip = "Notification RabbitMQ acceptance requires its opt-in and URI.";
    }
}
