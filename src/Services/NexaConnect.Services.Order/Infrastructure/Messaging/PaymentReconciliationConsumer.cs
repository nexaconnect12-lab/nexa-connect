using System.Text.Json;
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
    PaymentReconciliationApplicationService handler,
    ILogger<PaymentReconciliationConsumer> logger) : BackgroundService
{
    private const string Consumer = "order.payment-reconciled.v1";

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
        await channel.QueueBindAsync(options.Value.Queue + ".dead", options.Value.Exchange, "order.payment-reconciled.dead", cancellationToken: stoppingToken);
        await channel.BasicQosAsync(0, options.Value.PrefetchCount, false, stoppingToken);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) => await HandleAsync(channel, delivery, stoppingToken);
        await channel.BasicConsumeAsync(options.Value.Queue, autoAck: false, consumer, stoppingToken);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken cancellationToken)
    {
        Guid? eventId = null;
        try
        {
            PaymentAuthorizationReconciledV1 message = JsonSerializer.Deserialize<PaymentAuthorizationReconciledV1>(delivery.Body.Span)
                ?? throw new JsonException("Payment reconciliation event is empty.");
            eventId = message.EventId;
            InboxClaimResult claim = await inbox.ClaimAsync(message.EventId, Consumer, TimeSpan.FromMinutes(2), cancellationToken);
            if (claim == InboxClaimResult.Busy)
            {
                await channel.BasicNackAsync(delivery.DeliveryTag, false, true, cancellationToken);
                return;
            }
            if (claim == InboxClaimResult.Completed)
            {
                await channel.BasicAckAsync(delivery.DeliveryTag, false, cancellationToken);
                return;
            }
            try
            {
                await handler.ApplyAsync(message, cancellationToken);
                await inbox.MarkCompletedAsync(message.EventId, Consumer, cancellationToken);
                await channel.BasicAckAsync(delivery.DeliveryTag, false, cancellationToken);
                logger.LogInformation("Payment reconciliation event {EventId} was applied for organization {OrganizationId}",
                    message.EventId, message.OrganizationId);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await inbox.ReleaseAsync(message.EventId, Consumer, exception.GetType().Name, cancellationToken);
                throw;
            }
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            logger.LogWarning(exception, "Rejected payment reconciliation event {EventId}", eventId);
            await channel.BasicNackAsync(delivery.DeliveryTag, false, false, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Payment reconciliation event {EventId} processing failed", eventId);
            await channel.BasicNackAsync(delivery.DeliveryTag, false, true, cancellationToken);
        }
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
