using System.Diagnostics;
using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.Reporting.Application;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NexaConnect.Services.Reporting.Infrastructure.Messaging;

public sealed class SettlementFinancialConsumer(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<SettlementFinancialConsumer> logger) : BackgroundService
{
    public const string RoutingKey = "pos.branch-day-settled.v2";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly ActivitySource Activities = new("nexaconnect-reporting");
    private static readonly System.Diagnostics.Metrics.Meter Meter = new("nexaconnect-reporting");
    private static readonly System.Diagnostics.Metrics.Counter<long> Outcomes =
        Meter.CreateCounter<long>("reporting.day_settlement.outcomes");
    private readonly TaskCompletionSource readiness = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WaitUntilReadyAsync(CancellationToken cancellationToken) => readiness.Task.WaitAsync(cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string uri = configuration["SettlementConsumer:ConnectionString"]
            ?? throw new InvalidOperationException("SettlementConsumer:ConnectionString is required.");
        string exchange = configuration["SettlementConsumer:Exchange"] ?? "nexaconnect.events";
        string queue = configuration["SettlementConsumer:Queue"] ?? "nexaconnect.reporting.day-settlements.v1";
        ushort prefetch = Math.Clamp(configuration.GetValue<ushort?>("SettlementConsumer:PrefetchCount") ?? 16, (ushort)1, (ushort)100);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using IConnection connection = await new ConnectionFactory { Uri = new Uri(uri) }
                    .CreateConnectionAsync(stoppingToken);
                await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
                await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);
                var arguments = new Dictionary<string, object?>
                {
                    ["x-dead-letter-exchange"] = exchange,
                    ["x-dead-letter-routing-key"] = queue + ".dead"
                };
                await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false,
                    arguments: arguments, cancellationToken: stoppingToken);
                await channel.QueueDeclareAsync(queue + ".dead", durable: true, exclusive: false, autoDelete: false,
                    cancellationToken: stoppingToken);
                await channel.QueueBindAsync(queue, exchange, RoutingKey, cancellationToken: stoppingToken);
                await channel.QueueBindAsync(queue, exchange, "pos.branch-day-settled.v1", cancellationToken: stoppingToken);
                await channel.QueueBindAsync(queue + ".dead", exchange, queue + ".dead", cancellationToken: stoppingToken);
                await channel.BasicQosAsync(0, prefetch, false, stoppingToken);
                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                channel.ChannelShutdownAsync += (_, _) => { closed.TrySetResult(); return Task.CompletedTask; };
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (_, delivery) => await HandleAsync(channel, delivery, stoppingToken);
                await channel.BasicConsumeAsync(queue, autoAck: false, consumer, stoppingToken);
                logger.LogInformation("Settlement financial consumer ready");
                readiness.TrySetResult();
                await closed.Task.WaitAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                Outcomes.Add(1, new KeyValuePair<string, object?>("status", "connection_retry"));
                logger.LogWarning("Settlement financial consumer connection unavailable");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken cancellationToken)
    {
        using Activity? activity = Activities.StartActivity("day-settlement.project", ActivityKind.Consumer);
        Guid? eventId = null;
        IDisposable? correlationScope = null;
        IDisposable? logScope = null;
        try
        {
            if ((delivery.RoutingKey != RoutingKey && delivery.RoutingKey != "pos.branch-day-settled.v1") || delivery.Body.Length > 2 * 1024 * 1024)
                throw new ArgumentException("Invalid settlement envelope.");
            var value = delivery.RoutingKey == RoutingKey
                ? new SettlementPublication(null, JsonSerializer.Deserialize<BranchDaySettledV2>(delivery.Body.Span, Json) ?? throw new JsonException())
                : new SettlementPublication(JsonSerializer.Deserialize<BranchDaySettledV1>(delivery.Body.Span, Json) ?? throw new JsonException(), null);
            var fact = SettlementReporting.Translate(value);
            eventId = fact.EventId;
            correlationScope = NexaConnect.Observability.CorrelationContext.Push(fact.CorrelationId.ToString("D"));
            logScope = logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = fact.CorrelationId });
            using IServiceScope scope = scopes.CreateScope();
            bool changed = await scope.ServiceProvider.GetRequiredService<SettlementReporting>()
                .ProjectAsync(value, cancellationToken);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
            Outcomes.Add(1, new KeyValuePair<string, object?>("status", changed ? "applied" : "replayed"));
            logger.LogInformation("Settlement financial event processed; changed {Changed}", changed);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or OverflowException)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            Outcomes.Add(1, new KeyValuePair<string, object?>("status", "rejected"));
            logger.LogWarning("Settlement financial event {EventId} rejected with category {Category}",
                eventId, exception.GetType().Name);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            Outcomes.Add(1, new KeyValuePair<string, object?>("status", "retry"));
            logger.LogWarning("Settlement financial projection unavailable for event {EventId}; delivery will retry", eventId);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, cancellationToken);
        }
        finally
        {
            logScope?.Dispose();
            correlationScope?.Dispose();
        }
    }
}
