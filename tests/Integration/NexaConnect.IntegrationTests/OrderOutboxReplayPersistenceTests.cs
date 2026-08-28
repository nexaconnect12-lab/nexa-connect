extern alias ORDER;

using NexaConnect.Infrastructure.Messaging;
using NexaConnect.Contracts.IntegrationEvents;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using RabbitMQ.Client;
using OrderAggregate = ORDER::NexaConnect.Services.Order.Domain.OrderAggregate;
using OrderLine = ORDER::NexaConnect.Services.Order.Domain.OrderLine;
using PostgresOrderRepository = ORDER::NexaConnect.Services.Order.Infrastructure.Persistence.PostgresOrderRepository;

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
    public async Task Repository_payment_review_events_survive_hosted_dispatcher_restart_and_publish_valid_contracts()
    {
        var store=new PostgresOutboxStore(_dataSource!);
        var repository=new PostgresOrderRepository(_dataSource!);
        Guid organizationId=Guid.NewGuid(),paymentIntentId=Guid.NewGuid(),authorizationDecisionId=Guid.NewGuid(),correlationId=Guid.NewGuid();
        var order=OrderAggregate.Create(Guid.NewGuid(),organizationId,Guid.NewGuid(),[new OrderLine(Guid.NewGuid(),"Broker item",10m,1,"kitchen")],"USD",Guid.NewGuid());
        order.Submit();order.MarkInventoryReserved();order.MarkKitchenAccepted();order.MarkPaymentPending(paymentIntentId);order.MarkPaymentReview();
        var required=new OrderPaymentReviewRequiredV1(Guid.NewGuid(),correlationId,DateTimeOffset.UtcNow,organizationId,order.Id,paymentIntentId,"provider_void_failed");
        await repository.SaveWithEventAsync(order,required,default);
        var review=Assert.IsType<ORDER::NexaConnect.Services.Order.Application.PaymentReviews.PaymentReviewCase>(await repository.GetReviewAsync(organizationId,order.Id,default));
        Guid claimId=Assert.IsType<Guid>(await repository.ClaimResolutionAsync(review,"resume_payment","broker-operator",DateTimeOffset.UtcNow,default));
        order.ResumePaymentPending();
        var resolved=new OrderPaymentReviewResolvedV1(Guid.NewGuid(),correlationId,DateTimeOffset.UtcNow,organizationId,order.Id,paymentIntentId,"resume_payment",review.ConcurrencyVersion+1,authorizationDecisionId);
        var audit=new PlatformAuditEventV1(Guid.NewGuid(),correlationId,resolved.OccurredAtUtc,"broker-operator",organizationId,"order.payment-review.resolved","order",order.Id.ToString("D"),"succeeded");
        Assert.True(await repository.ResolveAsync(order,review,"resume_payment","provider verified", "broker-operator",claimId,resolved,audit,default));
        string[] eventTypes=["order.payment-review-required.v1","order.payment-review-resolved.v1","order.audit.v1"];
        using(var failedDispatcher=new OutboxDispatcher(store,new FailingTransport(),Options.Create(new OutboxOptions{BatchSize=10,PollInterval=TimeSpan.FromMilliseconds(25)}),NullLogger<OutboxDispatcher>.Instance))
        {
            await failedDispatcher.StartAsync(default);
            await WaitUntilAsync(async()=>await CountAsync("SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1 AND last_error_category='IOException'",order.Id)==3,TimeSpan.FromSeconds(5));
            await failedDispatcher.StopAsync(default);
        }
        await MakeImmediatelyRetryableAsync(required.EventId);await MakeImmediatelyRetryableAsync(resolved.EventId);await MakeImmediatelyRetryableAsync(audit.EventId);

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
            using(var recoveredDispatcher=new OutboxDispatcher(store,transport,Options.Create(new OutboxOptions{Exchange=exchange,BatchSize=10,PollInterval=TimeSpan.FromMilliseconds(25)}),NullLogger<OutboxDispatcher>.Instance))
            {
                await recoveredDispatcher.StartAsync(default);
                await WaitUntilAsync(async()=>await CountAsync("SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1 AND published_at_utc IS NOT NULL",order.Id)==3,TimeSpan.FromSeconds(5));
                await recoveredDispatcher.StopAsync(default);
            }
            var deliveries=new List<BasicGetResult>();
            for(int attempt=0;attempt<30&&deliveries.Count<3;attempt++){BasicGetResult? delivery=await channel.BasicGetAsync(queue,autoAck:true);if(delivery is null)await Task.Delay(100);else deliveries.Add(delivery);}
            Assert.Equal(eventTypes.Order(),deliveries.Select(value=>value.RoutingKey).Order());
            Assert.All(deliveries,value=>Assert.True(value.BasicProperties.Persistent));
            OrderPaymentReviewRequiredV1 requiredCopy=Deserialize<OrderPaymentReviewRequiredV1>(deliveries,"order.payment-review-required.v1");
            OrderPaymentReviewResolvedV1 resolvedCopy=Deserialize<OrderPaymentReviewResolvedV1>(deliveries,"order.payment-review-resolved.v1");
            PlatformAuditEventV1 auditCopy=Deserialize<PlatformAuditEventV1>(deliveries,"order.audit.v1");
            Assert.Equal(required,requiredCopy);Assert.Equal(resolved,resolvedCopy);Assert.Equal(audit,auditCopy);
            Assert.Equal(authorizationDecisionId,resolvedCopy.AuthorizationDecisionId);Assert.Equal(correlationId,auditCopy.CorrelationId);
            Assert.DoesNotContain(await store.ClaimBatchAsync(10,default),message=>message.AggregateId==order.Id);
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

        string migrations=Path.Combine(FindRepositoryRoot(),"src","Tools","NexaConnect.DataMigration","Scripts","Order");
        foreach(string version in new[]{"0001_initial_schema","0002_payment_capture_reconciliation","0003_payment_void_reconciliation","0004_payment_review_resolution"})
        {await using var command=new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(migrations,version,"up.sql")),connection);await command.ExecuteNonQueryAsync();}
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

    private async Task<long> CountAsync(string sql,Guid id){await using var command=_dataSource!.CreateCommand(sql);command.Parameters.AddWithValue(id);return Convert.ToInt64(await command.ExecuteScalarAsync());}
    private static async Task WaitUntilAsync(Func<Task<bool>> condition,TimeSpan timeout){DateTimeOffset end=DateTimeOffset.UtcNow+timeout;while(DateTimeOffset.UtcNow<end){if(await condition())return;await Task.Delay(50);}throw new TimeoutException("Hosted dispatcher did not reach the expected state.");}
    private static T Deserialize<T>(IEnumerable<BasicGetResult> deliveries,string routingKey)=>System.Text.Json.JsonSerializer.Deserialize<T>(deliveries.Single(value=>value.RoutingKey==routingKey).Body.Span)??throw new InvalidOperationException("Published contract was empty.");
    private static string FindRepositoryRoot(){DirectoryInfo? directory=new(AppContext.BaseDirectory);while(directory is not null&&!File.Exists(Path.Combine(directory.FullName,"NexaConnect.sln")))directory=directory.Parent;return directory?.FullName??throw new DirectoryNotFoundException();}

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

    private sealed class FailingTransport:IOutboxTransport{public Task PublishAsync(OutboxMessage message,CancellationToken cancellationToken)=>Task.FromException(new IOException("acceptance broker outage"));}
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
