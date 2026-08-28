using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using RabbitMQ.Client;

namespace NexaConnect.Infrastructure.Messaging;

public sealed record OutboxMessage(
    Guid Id,
    string EventType,
    int ContractVersion,
    string AggregateType,
    Guid AggregateId,
    string Payload,
    string? CorrelationId,
    DateTimeOffset OccurredAtUtc);

public interface IOutboxStore
{
    Task EnqueueAsync(OutboxMessage message, CancellationToken cancellationToken);
    Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(int batchSize, CancellationToken cancellationToken);
    Task MarkPublishedAsync(Guid messageId, CancellationToken cancellationToken);
    Task MarkFailedAsync(Guid messageId, string category, CancellationToken cancellationToken);
}

public sealed class PostgresOutboxStore(NpgsqlDataSource dataSource) : IOutboxStore
{
    public async Task EnqueueAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO outbox_messages
                (id, event_type, contract_version, aggregate_type, aggregate_id, payload, correlation_id, occurred_at_utc)
            VALUES ($1, $2, $3, $4, $5, $6::jsonb, $7, $8)
            ON CONFLICT (id) DO NOTHING;
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(message.Id);
        command.Parameters.AddWithValue(message.EventType);
        command.Parameters.AddWithValue(message.ContractVersion);
        command.Parameters.AddWithValue(message.AggregateType);
        command.Parameters.AddWithValue(message.AggregateId);
        command.Parameters.AddWithValue(message.Payload);
        command.Parameters.AddWithValue((object?)message.CorrelationId ?? DBNull.Value);
        command.Parameters.AddWithValue(message.OccurredAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        const string sql = """
            WITH claimed AS
            (
                SELECT id
                FROM outbox_messages
                WHERE published_at_utc IS NULL
                  AND (next_attempt_at_utc IS NULL OR next_attempt_at_utc <= now())
                ORDER BY occurred_at_utc, id
                FOR UPDATE SKIP LOCKED
                LIMIT $1
            )
            UPDATE outbox_messages message
            SET retry_count = message.retry_count + 1,
                next_attempt_at_utc = now() + interval '30 seconds'
            FROM claimed
            WHERE message.id = claimed.id
            RETURNING message.id, message.event_type, message.contract_version, message.aggregate_type,
                      message.aggregate_id, message.payload::text, message.correlation_id, message.occurred_at_utc;
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(batchSize);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var messages = new List<OutboxMessage>();
        while (await reader.ReadAsync(cancellationToken))
        {
            messages.Add(new OutboxMessage(reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3),
                reader.GetGuid(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetFieldValue<DateTimeOffset>(7)));
        }
        return messages;
    }

    public Task MarkPublishedAsync(Guid messageId, CancellationToken cancellationToken) => ExecuteAsync(
        "UPDATE outbox_messages SET published_at_utc = now(), next_attempt_at_utc = NULL, last_error_category = NULL WHERE id = $1;",
        messageId, null, cancellationToken);

    public Task MarkFailedAsync(Guid messageId, string category, CancellationToken cancellationToken) => ExecuteAsync(
        "UPDATE outbox_messages SET last_error_category = $2 WHERE id = $1;", messageId, category, cancellationToken);

    private async Task ExecuteAsync(string sql, Guid messageId, string? category, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(messageId);
        if (category is not null) command.Parameters.AddWithValue(category);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public interface IOutboxTransport
{
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken);
}

public sealed class RabbitMqOutboxTransport : IOutboxTransport, IAsyncDisposable
{
    private readonly IOptions<OutboxOptions> options;
    private readonly Func<CancellationToken, Task<IConnection>> connectionFactory;
    private readonly bool ownsConnection;
    private readonly SemaphoreSlim connectionGate = new(1, 1);
    private IConnection? connection;

    public RabbitMqOutboxTransport(IOptions<OutboxOptions> options)
        : this(options, cancellationToken => new ConnectionFactory
        {
            Uri = new Uri(options.Value.ConnectionString),
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true
        }.CreateConnectionAsync(cancellationToken), ownsConnection: true)
    {
    }

    public RabbitMqOutboxTransport(IConnection connection, IOptions<OutboxOptions> options)
        : this(options, _ => Task.FromResult(connection), ownsConnection: false)
    {
        this.connection = connection;
    }

    public RabbitMqOutboxTransport(
        IOptions<OutboxOptions> options,
        Func<CancellationToken, Task<IConnection>> connectionFactory)
        : this(options, connectionFactory, ownsConnection: true)
    {
    }

    private RabbitMqOutboxTransport(
        IOptions<OutboxOptions> options,
        Func<CancellationToken, Task<IConnection>> connectionFactory,
        bool ownsConnection)
    {
        this.options = options;
        this.connectionFactory = connectionFactory;
        this.ownsConnection = ownsConnection;
    }

    public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        IConnection activeConnection = await GetConnectionAsync(cancellationToken);
        try
        {
            await using IChannel channel = await activeConnection.CreateChannelAsync(
                new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
                cancellationToken);
            await channel.ExchangeDeclareAsync(options.Value.Exchange, ExchangeType.Topic, durable: true,
                cancellationToken: cancellationToken);
            var properties = new BasicProperties
                { Persistent = true, ContentType = "application/json", Type = message.EventType };
            byte[] body = Encoding.UTF8.GetBytes(message.Payload);
            await channel.BasicPublishAsync(options.Value.Exchange, message.EventType, true, properties, body,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await InvalidateAsync(activeConnection);
            throw;
        }
    }

    private async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        IConnection? current = Volatile.Read(ref connection);
        if (current?.IsOpen == true) return current;

        await connectionGate.WaitAsync(cancellationToken);
        try
        {
            current = connection;
            if (current?.IsOpen == true) return current;
            if (current is not null && ownsConnection) await current.DisposeAsync();
            connection = await connectionFactory(cancellationToken);
            return connection;
        }
        finally
        {
            connectionGate.Release();
        }
    }

    private async Task InvalidateAsync(IConnection failedConnection)
    {
        await connectionGate.WaitAsync();
        try
        {
            if (!ReferenceEquals(connection, failedConnection)) return;
            connection = null;
            if (ownsConnection) await failedConnection.DisposeAsync();
        }
        finally
        {
            connectionGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await connectionGate.WaitAsync();
        try
        {
            if (connection is not null && ownsConnection) await connection.DisposeAsync();
            connection = null;
        }
        finally
        {
            connectionGate.Release();
            connectionGate.Dispose();
        }
    }
}

public sealed class OutboxDispatcher(
    IOutboxStore store,
    IOutboxTransport transport,
    IOptions<OutboxOptions> options,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            IReadOnlyList<OutboxMessage> messages;
            try{messages=await store.ClaimBatchAsync(options.Value.BatchSize,stoppingToken);}catch(Exception exception)when(exception is not OperationCanceledException){logger.LogError(exception,"Failed to claim outbox messages.");await Task.Delay(TimeSpan.FromSeconds(5),stoppingToken);continue;}
            foreach (OutboxMessage message in messages)
            {
                try
                {
                    await transport.PublishAsync(message, stoppingToken);
                    await store.MarkPublishedAsync(message.Id, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(exception, "Failed to publish outbox message {MessageId} of type {EventType}.", message.Id, message.EventType);
                    await store.MarkFailedAsync(message.Id, exception.GetType().Name, stoppingToken);
                }
            }
            await Task.Delay(options.Value.PollInterval, stoppingToken);
        }
    }
}

public sealed class OutboxOptions
{
    public string Exchange { get; set; } = "nexaconnect.events";
    public int BatchSize { get; set; } = 50;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);
    public string ConnectionString { get; set; } = string.Empty;
}

public static class OutboxServiceCollectionExtensions
{
    public static IServiceCollection AddPostgresOutbox(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName)
    {
        string databaseConnection = configuration.GetConnectionString(connectionStringName)
            ?? throw new InvalidOperationException($"ConnectionStrings:{connectionStringName} is required for PostgreSQL outbox persistence.");
        services.TryAddSingleton(_ => NpgsqlDataSource.Create(databaseConnection));
        services.AddSingleton<IOutboxStore, PostgresOutboxStore>();
        services.Configure<OutboxOptions>(configuration.GetSection("Outbox"));
        string configuredConnection = configuration["Outbox:ConnectionString"]
            ?? throw new InvalidOperationException("Outbox:ConnectionString is required when PostgreSQL outbox persistence is enabled.");
        _ = new Uri(configuredConnection);
        services.AddSingleton<IOutboxTransport, RabbitMqOutboxTransport>();
        services.AddHostedService<OutboxDispatcher>();
        return services;
    }
}
