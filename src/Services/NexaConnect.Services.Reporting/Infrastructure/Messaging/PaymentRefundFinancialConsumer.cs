using System.Diagnostics;
using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Reporting.Application;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NexaConnect.Services.Reporting.Infrastructure.Messaging;

public class PaymentRefundFinancialConsumer(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<PaymentRefundFinancialConsumer> logger, bool manual = false) : BackgroundService
{
    public const string RoutingKey = "payment.refunded.v1";
    private static readonly ActivitySource Activities = new("nexaconnect-reporting");
    private static readonly System.Diagnostics.Metrics.Meter Meter = new("nexaconnect-reporting");
    private static readonly System.Diagnostics.Metrics.Counter<long> Outcomes =
        Meter.CreateCounter<long>("reporting.payment_refund.outcomes");
    private static readonly System.Diagnostics.Metrics.Counter<long> ManualOutcomes =
        Meter.CreateCounter<long>("reporting.manual_refund.outcomes");
    private string Binding => manual ? PosManualRefundCompletedV1.EventType : RoutingKey;
    private string Source => manual ? "Manual refund financial" : "Payment refund financial";
    private System.Diagnostics.Metrics.Counter<long> Counter => manual ? ManualOutcomes : Outcomes;
    private readonly TaskCompletionSource readiness = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WaitUntilReadyAsync(CancellationToken cancellationToken) => readiness.Task.WaitAsync(cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string section = manual ? "ManualRefundConsumer" : "PaymentRefundConsumer";
        string uri = configuration[$"{section}:ConnectionString"]
            ?? throw new InvalidOperationException($"{section}:ConnectionString is required.");
        string exchange = configuration[$"{section}:Exchange"] ?? "nexaconnect.events";
        string queue = configuration[$"{section}:Queue"] ?? (manual ? "nexaconnect.reporting.manual-refunds.v1" : "nexaconnect.reporting.payment-refunds.v1");
        ushort prefetch = Math.Clamp(configuration.GetValue<ushort?>($"{section}:PrefetchCount") ?? 16, (ushort)1, (ushort)100);
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
                await channel.QueueBindAsync(queue, exchange, Binding, cancellationToken: stoppingToken);
                await channel.QueueBindAsync(queue + ".dead", exchange, queue + ".dead", cancellationToken: stoppingToken);
                await channel.BasicQosAsync(0, prefetch, false, stoppingToken);
                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                channel.ChannelShutdownAsync += (_, _) => { closed.TrySetResult(); return Task.CompletedTask; };
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (_, delivery) => await HandleAsync(channel, delivery, stoppingToken);
                await channel.BasicConsumeAsync(queue, autoAck: false, consumer, stoppingToken);
                logger.LogInformation("{RefundSource} consumer ready", Source);
                readiness.TrySetResult();
                await closed.Task.WaitAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                Counter.Add(1, new KeyValuePair<string, object?>("status", "connection_retry"));
                logger.LogWarning("{RefundSource} consumer connection unavailable", Source);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken cancellationToken)
    {
        using Activity? activity = Activities.StartActivity(manual ? "manual-refund.project" : "payment-refund.project", ActivityKind.Consumer);
        Guid? eventId = null;
        IDisposable? correlationScope = null;
        IDisposable? logScope = null;
        try
        {
            if (delivery.RoutingKey != Binding || delivery.Body.Length > 32768)
                throw new ArgumentException("Invalid refund envelope.");
            using IServiceScope scope = scopes.CreateScope();
            bool changed;
            if (manual)
            {
                var value = JsonSerializer.Deserialize<PosManualRefundCompletedV1>(delivery.Body.Span)
                    ?? throw new JsonException("Manual refund event is empty.");
                eventId = value.EventId;
                ManualRefundFinancialReporting.Validate(value);
                SetCorrelation(value.CorrelationId);
                changed = await scope.ServiceProvider.GetRequiredService<ManualRefundFinancialReporting>().ProjectAsync(value, cancellationToken);
            }
            else
            {
                var value = JsonSerializer.Deserialize<PaymentRefundedV1>(delivery.Body.Span)
                    ?? throw new JsonException("Payment refund event is empty.");
                eventId = value.EventId;
                RefundFinancialReporting.Translate(value);
                SetCorrelation(value.CorrelationId);
                changed = await scope.ServiceProvider.GetRequiredService<RefundFinancialReporting>().ProjectAsync(value, cancellationToken);
            }
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
            Counter.Add(1, new KeyValuePair<string, object?>("status", changed ? "applied" : "replayed"));
            logger.LogInformation("{RefundSource} event processed; changed {Changed}", Source, changed);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or OverflowException)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            Counter.Add(1, new KeyValuePair<string, object?>("status", "rejected"));
            logger.LogWarning("{RefundSource} event {EventId} rejected with category {Category}",
                Source, eventId, exception.GetType().Name);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            Counter.Add(1, new KeyValuePair<string, object?>("status", "retry"));
            logger.LogWarning("{RefundSource} projection unavailable for event {EventId}; delivery will retry", Source, eventId);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, cancellationToken);
        }
        finally
        {
            logScope?.Dispose();
            correlationScope?.Dispose();
        }

        void SetCorrelation(Guid correlation)
        {
            correlationScope = NexaConnect.Observability.CorrelationContext.Push(correlation.ToString("D"));
            logScope = logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlation });
        }
    }
}

public sealed class ManualRefundFinancialConsumer(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<PaymentRefundFinancialConsumer> logger) : PaymentRefundFinancialConsumer(scopes, configuration, logger, manual: true);
