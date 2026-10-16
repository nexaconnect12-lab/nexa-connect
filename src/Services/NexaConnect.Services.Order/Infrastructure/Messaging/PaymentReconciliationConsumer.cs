using System.Text.Json;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using NexaConnect.Observability;
using Microsoft.Extensions.Options;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Infrastructure.Messaging;
using NexaConnect.Services.Order.Application.Workflow;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NexaConnect.Services.Order.Infrastructure.Messaging;

public sealed class PaymentReconciliationConsumerOptions
{
    public bool Enabled { get; set; }
    public string ConnectionString { get; set; } = "";
    public string Exchange { get; set; } = "nexaconnect.events";
    public string Queue { get; set; } = "nexaconnect.order.payment-reconciled.v1";
    public ushort PrefetchCount { get; set; } = 16;
}

public sealed class PaymentReconciliationConsumer(
    IConnection connection,
    IOptions<PaymentReconciliationConsumerOptions> options,
    IDurableInboxStore inbox,
    IServiceScopeFactory scopeFactory,
    ILogger<PaymentReconciliationConsumer> logger) : BackgroundService
{
    private const string Consumer = "order.payment-reconciled.v1";
    private static readonly ActivitySource Activities=new("nexaconnect-order");
    private static readonly Meter Meter=new("nexaconnect-order");
    private static readonly Counter<long> Outcomes=Meter.CreateCounter<long>("order.payment_reconciliation.outcomes");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await channel.ExchangeDeclareAsync(options.Value.Exchange, ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);
        var arguments = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = options.Value.Exchange,
            ["x-dead-letter-routing-key"] = "order.payment-reconciled.dead"
        };
        await channel.QueueDeclareAsync(options.Value.Queue, durable: true, exclusive: false, autoDelete: false,
            arguments: arguments, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(options.Value.Queue + ".dead", durable: true, exclusive: false, autoDelete: false,
            cancellationToken: stoppingToken);
        await channel.QueueBindAsync(options.Value.Queue, options.Value.Exchange, "payment.authorization-reconciled.v1", cancellationToken: stoppingToken);
        await channel.QueueBindAsync(options.Value.Queue, options.Value.Exchange, "payment.capture-reconciled.v1", cancellationToken: stoppingToken);
        await channel.QueueBindAsync(options.Value.Queue, options.Value.Exchange, "payment.voided.v1", cancellationToken: stoppingToken);
        await channel.QueueBindAsync(options.Value.Queue, options.Value.Exchange, "payment.void-failed.v1", cancellationToken: stoppingToken);
        await channel.QueueBindAsync(options.Value.Queue, options.Value.Exchange, "payment.void-uncertain.v1", cancellationToken: stoppingToken);
        await channel.QueueBindAsync(options.Value.Queue, options.Value.Exchange, "payment.void-reconciled.v1", cancellationToken: stoppingToken);
        await channel.QueueBindAsync(options.Value.Queue + ".dead", options.Value.Exchange, "order.payment-reconciled.dead", cancellationToken: stoppingToken);
        await channel.BasicQosAsync(0, options.Value.PrefetchCount, false, stoppingToken);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) => await HandleAsync(channel, delivery, stoppingToken);
        await channel.BasicConsumeAsync(options.Value.Queue, autoAck: false, consumer, stoppingToken);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken cancellationToken)
    {
        using var activity=Activities.StartActivity("payment-reconciliation.process",ActivityKind.Consumer);
        IDisposable? correlation=null,logging=null;
        try
        {
            IIntegrationEvent message = delivery.RoutingKey switch
            {
                "payment.authorization-reconciled.v1" => (IIntegrationEvent?)JsonSerializer.Deserialize<PaymentAuthorizationReconciledV1>(delivery.Body.Span),
                "payment.capture-reconciled.v1" => (IIntegrationEvent?)JsonSerializer.Deserialize<PaymentCaptureReconciledV1>(delivery.Body.Span),
                "payment.voided.v1" => (IIntegrationEvent?)JsonSerializer.Deserialize<PaymentVoidedV1>(delivery.Body.Span),
                "payment.void-failed.v1" => (IIntegrationEvent?)JsonSerializer.Deserialize<PaymentVoidFailedV1>(delivery.Body.Span),
                "payment.void-uncertain.v1" => (IIntegrationEvent?)JsonSerializer.Deserialize<PaymentVoidUncertainV1>(delivery.Body.Span),
                "payment.void-reconciled.v1" => (IIntegrationEvent?)JsonSerializer.Deserialize<PaymentVoidReconciledV1>(delivery.Body.Span),
                _ => throw new JsonException("Unsupported payment reconciliation routing key.")
            } ?? throw new JsonException("Payment reconciliation event is empty.");
            if(message.EventId==Guid.Empty)throw new ArgumentException("Event identity required.");
            var correlationId=(message.CorrelationId==Guid.Empty?Guid.NewGuid():message.CorrelationId).ToString("D");
            correlation=CorrelationContext.Push(correlationId);
            logging=logger.BeginScope(new Dictionary<string,object>{["CorrelationId"]=correlationId,["TraceId"]=activity?.TraceId.ToString()??string.Empty});
            InboxClaimResult claim = await inbox.ClaimAsync(message.EventId, Consumer, TimeSpan.FromMinutes(2), cancellationToken);
            if (claim == InboxClaimResult.Busy)
            {
                await channel.BasicNackAsync(delivery.DeliveryTag, false, true, cancellationToken);
                return;
            }
            if (claim == InboxClaimResult.Completed)
            {
                await using var completedScope=scopeFactory.CreateAsyncScope();
                await completedScope.ServiceProvider.GetRequiredService<LatePaymentReconciliation>().DispositionAsync(message,delivery.RoutingKey,cancellationToken);
                await channel.BasicAckAsync(delivery.DeliveryTag, false, cancellationToken);
                Outcomes.Add(1,new KeyValuePair<string,object?>("outcome","replayed"));
                return;
            }
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<PaymentReconciliationApplicationService>();
                var late=scope.ServiceProvider.GetRequiredService<LatePaymentReconciliation>();
                var disposition=await late.DispositionAsync(message,delivery.RoutingKey,cancellationToken);
                bool lateCaptured=disposition=="committed";
                bool applied;
                try { applied = disposition=="committed" || disposition!="armed" && (message switch
                {
                    PaymentAuthorizationReconciledV1 authorization => await handler.ApplyAsync(authorization, cancellationToken),
                    PaymentCaptureReconciledV1 capture => await handler.ApplyAsync(capture, cancellationToken),
                    PaymentVoidedV1 value => await handler.ApplyAsync(value, cancellationToken),
                    PaymentVoidFailedV1 value => await handler.ApplyAsync(value, cancellationToken),
                    PaymentVoidUncertainV1 value => await handler.ApplyAsync(value, cancellationToken),
                    PaymentVoidReconciledV1 value => await handler.ApplyAsync(value, cancellationToken),
                    _ => false
                });
                if(disposition=="armed")await Task.Delay(TimeSpan.FromSeconds(1),cancellationToken);
                }
                catch (Npgsql.PostgresException e) when (e.SqlState == "PDS01")
                {
                    applied = await scope.ServiceProvider.GetRequiredService<LatePaymentReconciliation>().CaptureAsync(message, delivery.RoutingKey, cancellationToken);
                    lateCaptured=applied;
                    if (!applied) await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    logger.LogInformation("Payment reconciliation deferred by settlement; custody {Captured}", applied);
                }
                if (!applied)
                {
                    Outcomes.Add(1,new KeyValuePair<string,object?>("outcome",disposition=="armed"?"held":"retry"));
                    await inbox.ReleaseAsync(message.EventId, Consumer, "order_not_ready", cancellationToken);
                    await channel.BasicNackAsync(delivery.DeliveryTag, false, true, cancellationToken);
                    return;
                }
                await inbox.MarkCompletedAsync(message.EventId, Consumer, cancellationToken);
                await channel.BasicAckAsync(delivery.DeliveryTag, false, cancellationToken);
                var outcome=lateCaptured?"late_captured":"applied";
                Outcomes.Add(1,new KeyValuePair<string,object?>("outcome",outcome));activity?.SetTag("outcome",outcome);
                logger.LogInformation("Payment reconciliation delivery completed; outcome {Outcome}",outcome);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await inbox.ReleaseAsync(message.EventId, Consumer, exception.GetType().Name, cancellationToken);
                throw;
            }
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or NexaConnect.Infrastructure.Persistence.SnapshotOperationConflictException)
        {
            logger.LogWarning("Rejected payment reconciliation event; category {Category}",exception.GetType().Name);
            activity?.SetStatus(ActivityStatusCode.Error);Outcomes.Add(1,new KeyValuePair<string,object?>("outcome","dead_lettered"));
            await channel.BasicNackAsync(delivery.DeliveryTag, false, false, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("Payment reconciliation processing failed; category {Category}",exception.GetType().Name);
            activity?.SetStatus(ActivityStatusCode.Error);Outcomes.Add(1,new KeyValuePair<string,object?>("outcome","retry"));
            await channel.BasicNackAsync(delivery.DeliveryTag, false, true, cancellationToken);
        }
        finally {logging?.Dispose();correlation?.Dispose();}
    }

}

public static class PaymentReconciliationConsumerRegistration
{
    public static IServiceCollection AddPaymentReconciliationConsumer(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PaymentReconciliationConsumerOptions>(configuration.GetSection("PaymentReconciliationConsumer"));
        if (!configuration.GetValue<bool>("PaymentReconciliationConsumer:Enabled")) return services;
        string connectionString = configuration["PaymentReconciliationConsumer:ConnectionString"]
            ?? throw new InvalidOperationException("PaymentReconciliationConsumer:ConnectionString is required when enabled.");
        services.AddSingleton<IConnection>(_ => new ConnectionFactory { Uri = new Uri(connectionString) }
            .CreateConnectionAsync().GetAwaiter().GetResult());
        services.AddSingleton<IDurableInboxStore, PostgresInboxStore>();
        services.AddHostedService<PaymentReconciliationConsumer>();
        return services;
    }
}
