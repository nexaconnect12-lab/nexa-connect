using System.Text.Json;
using Microsoft.Extensions.Options;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Notification.Application.Messages;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NexaConnect.Services.Notification.Infrastructure;

public sealed class NotificationConsumerOptions
{
    public bool Enabled { get; set; }
    public string Exchange { get; set; } = "nexaconnect.events";
    public string Queue { get; set; } = "nexaconnect.notification.requested.v1";
    public ushort PrefetchCount { get; set; } = 16;
}

public sealed class NotificationRequestedConsumer(IConnection connection, IServiceScopeFactory scopes, IOptions<NotificationConsumerOptions> options, ILogger<NotificationRequestedConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await channel.ExchangeDeclareAsync(options.Value.Exchange, ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);
        var arguments = new Dictionary<string, object?> { ["x-dead-letter-exchange"] = options.Value.Exchange, ["x-dead-letter-routing-key"] = "notification.requested.v1.dead" };
        await channel.QueueDeclareAsync(options.Value.Queue, durable: true, exclusive: false, autoDelete: false, arguments: arguments, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(options.Value.Queue + ".dead", durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(options.Value.Queue, options.Value.Exchange, "notification.requested.v1", cancellationToken: stoppingToken);
        await channel.QueueBindAsync(options.Value.Queue + ".dead", options.Value.Exchange, "notification.requested.v1.dead", cancellationToken: stoppingToken);
        await channel.BasicQosAsync(0, options.Value.PrefetchCount, false, stoppingToken);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            try
            {
                NotificationRequestedV1 message = JsonSerializer.Deserialize<NotificationRequestedV1>(delivery.Body.Span) ?? throw new JsonException("Notification request is empty.");
                using IServiceScope scope = scopes.CreateScope();
                NotificationHandlingResult result = await scope.ServiceProvider.GetRequiredService<NotificationIntegrationHandler>().HandleAsync(message, stoppingToken);
                if (result == NotificationHandlingResult.Busy)
                {
                    await channel.BasicNackAsync(delivery.DeliveryTag, false, true, stoppingToken);
                    logger.LogInformation("Notification request {EventId} is already leased for organization {OrganizationId}; delivery was requeued", message.EventId, message.OrganizationId);
                    return;
                }
                await channel.BasicAckAsync(delivery.DeliveryTag, false, stoppingToken);
                logger.LogInformation("Notification request {EventId} processed for organization {OrganizationId}", message.EventId, message.OrganizationId);
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException)
            {
                logger.LogError(exception, "Permanent notification request rejection for routing key {RoutingKey}", delivery.RoutingKey);
                await channel.BasicNackAsync(delivery.DeliveryTag, false, false, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Transient notification request failure for routing key {RoutingKey}", delivery.RoutingKey);
                await channel.BasicNackAsync(delivery.DeliveryTag, false, true, stoppingToken);
            }
        };
        await channel.BasicConsumeAsync(options.Value.Queue, false, consumer, stoppingToken);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
