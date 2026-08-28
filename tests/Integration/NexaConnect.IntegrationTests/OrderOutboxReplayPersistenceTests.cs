using NexaConnect.Infrastructure.Messaging;
using Microsoft.Extensions.Options;
using Npgsql;
using RabbitMQ.Client;

namespace NexaConnect.IntegrationTests;

public sealed class OrderOutboxReplayPersistenceTests : IAsyncLifetime
{
    private readonly string? _configuredConnectionString =
        Environment.GetEnvironmentVariable("NEXACONNECT_ORDER_INTEGRATION_DB");
    private NpgsqlDataSource? _dataSource;
    private string? _schema;

    [Fact]
    public async Task Failed_event_is_claimed_again_and_marked_published()
    {
        if (!DatabaseConfigured()) return;

        Guid messageId = Guid.NewGuid();
        var message = new OutboxMessage(
            messageId, "OrderSubmittedV1", 1, "order", Guid.NewGuid(),
            "{\"orderId\":\"00000000-0000-0000-0000-000000000001\"}",
            Guid.NewGuid().ToString(), DateTimeOffset.UtcNow.AddSeconds(-1));
        var store = new PostgresOutboxStore(_dataSource!);

        await store.EnqueueAsync(message, CancellationToken.None);
        IReadOnlyList<OutboxMessage> firstClaim = await store.ClaimBatchAsync(10, CancellationToken.None);
        Assert.Single(firstClaim);
        Assert.Equal(messageId, firstClaim[0].Id);

        await store.MarkFailedAsync(messageId, "test-transport-failure", CancellationToken.None);
        await MakeImmediatelyRetryableAsync(messageId);

        IReadOnlyList<OutboxMessage> replayClaim = await store.ClaimBatchAsync(10, CancellationToken.None);
        Assert.Single(replayClaim);
        Assert.Equal(messageId, replayClaim[0].Id);
        await store.MarkPublishedAsync(messageId, CancellationToken.None);

        Assert.Empty(await store.ClaimBatchAsync(10, CancellationToken.None));
    }

    [OrderRabbitMqFact]
    public async Task Payment_review_events_survive_transport_failure_and_publish_persistently_over_new_connection()
    {
        var store=new PostgresOutboxStore(_dataSource!);
        Guid orderId=Guid.NewGuid();
        string correlationId=Guid.NewGuid().ToString("D");
        string[] eventTypes=["order.payment-review-required.v1","order.payment-review-resolved.v1","order.audit.v1"];
        foreach(string eventType in eventTypes)
            await store.EnqueueAsync(new OutboxMessage(Guid.NewGuid(),eventType,1,"order",orderId,"{}",correlationId,DateTimeOffset.UtcNow),default);

        OutboxMessage[] failedClaim=(await store.ClaimBatchAsync(10,default)).Where(message=>message.AggregateId==orderId).ToArray();
        Assert.Equal(3,failedClaim.Length);
        await Assert.ThrowsAnyAsync<Exception>(async()=>
        {
            await using IConnection ignored=await new ConnectionFactory{Uri=new Uri("amqp://guest:guest@127.0.0.1:1"),RequestedConnectionTimeout=TimeSpan.FromSeconds(1)}.CreateConnectionAsync();
        });
        foreach(OutboxMessage message in failedClaim)
        {
            await store.MarkFailedAsync(message.Id,"broker-unavailable",default);
            await MakeImmediatelyRetryableAsync(message.Id);
        }

        string exchange=$"nexaconnect.order.payment-review.{Guid.NewGuid():N}";
        string queue=$"nexaconnect.order.payment-review.{Guid.NewGuid():N}";
        await using IConnection connection=await new ConnectionFactory{Uri=new Uri(Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_INTEGRATION_URI")!)}.CreateConnectionAsync();
        await using IChannel channel=await connection.CreateChannelAsync();
        try
        {
            await channel.ExchangeDeclareAsync(exchange,ExchangeType.Topic,durable:true,autoDelete:false);
            await channel.QueueDeclareAsync(queue,durable:false,exclusive:true,autoDelete:true);
            await channel.QueueBindAsync(queue,exchange,"order.#");
            await using var transport=new RabbitMqOutboxTransport(connection,Options.Create(new OutboxOptions{Exchange=exchange}));
            OutboxMessage[] replay=(await store.ClaimBatchAsync(10,default)).Where(message=>message.AggregateId==orderId).ToArray();
            Assert.Equal(3,replay.Length);
            foreach(OutboxMessage message in replay){await transport.PublishAsync(message,default);await store.MarkPublishedAsync(message.Id,default);}
            var routingKeys=new List<string>();
            for(int attempt=0;attempt<30&&routingKeys.Count<3;attempt++)
            {
                BasicGetResult? delivery=await channel.BasicGetAsync(queue,autoAck:true);
                if(delivery is null)await Task.Delay(100);else{Assert.True(delivery.BasicProperties.Persistent);routingKeys.Add(delivery.RoutingKey);}
            }
            Assert.Equal(eventTypes.Order(),routingKeys.Order());
            Assert.DoesNotContain(await store.ClaimBatchAsync(10,default),message=>message.AggregateId==orderId);
        }
        finally{await channel.ExchangeDeleteAsync(exchange);}
    }

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(_configuredConnectionString) || !IsSafeEnvironment()) return;

        _schema = $"order_outbox_it_{Guid.NewGuid():N}";
        var builder = new NpgsqlConnectionStringBuilder(_configuredConnectionString) { SearchPath = _schema };
        _dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();
        await using (var createSchema = new NpgsqlCommand($"CREATE SCHEMA \"{_schema}\";", connection))
        {
            await createSchema.ExecuteNonQueryAsync();
        }

        await using var schema = new NpgsqlCommand(SchemaSql, connection);
        await schema.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is null || _schema is null) return;

        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();
        await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE;", connection);
        await drop.ExecuteNonQueryAsync();
        await _dataSource.DisposeAsync();
    }

    private async Task MakeImmediatelyRetryableAsync(Guid messageId)
    {
        await using NpgsqlConnection connection = await _dataSource!.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE outbox_messages SET next_attempt_at_utc = now() WHERE id = $1;", connection);
        command.Parameters.AddWithValue(messageId);
        await command.ExecuteNonQueryAsync();
    }

    private bool DatabaseConfigured()
    {
        if (_dataSource is not null && IsSafeEnvironment()) return true;
        Console.WriteLine(
            "Order outbox PostgreSQL tests require NEXACONNECT_ORDER_INTEGRATION_DB and a Development/Test/Testing environment.");
        return false;
    }

    private static bool IsSafeEnvironment()
    {
        string? environment = Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        return environment is "Development" or "Test" or "Testing";
    }

    private const string SchemaSql = """
        CREATE TABLE outbox_messages
        (
            id uuid PRIMARY KEY, event_type text NOT NULL, contract_version integer NOT NULL,
            aggregate_type text NOT NULL, aggregate_id uuid NOT NULL, payload jsonb NOT NULL,
            correlation_id text NULL, occurred_at_utc timestamptz NOT NULL,
            published_at_utc timestamptz NULL, retry_count integer NOT NULL DEFAULT 0,
            next_attempt_at_utc timestamptz NULL, last_error_category text NULL
        );
        """;
}

public sealed class OrderRabbitMqFactAttribute : FactAttribute
{
    public OrderRabbitMqFactAttribute()
    {
        string? environment=Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT")??Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")??Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NEXACONNECT_ORDER_INTEGRATION_DB"))||environment is not ("Development" or "Test" or "Testing")||Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_ACCEPTANCE")!="1"||!Uri.TryCreate(Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_INTEGRATION_URI"),UriKind.Absolute,out _))
            Skip="Order payment-review RabbitMQ acceptance requires the Order database, safe environment, opt-in flag, and RabbitMQ URI.";
    }
}
