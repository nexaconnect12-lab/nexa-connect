using System.Text.Json;
using Microsoft.Extensions.Options;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Infrastructure.Messaging;
using NexaConnect.Services.Reporting.Application;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NexaConnect.Services.Reporting.Infrastructure.Messaging;

public sealed class ActivityConsumerOptions
{
    public bool Enabled { get; set; }
    public string ConnectionString { get; set; } = "";
    public string Exchange { get; set; } = "nexaconnect.events";
    public string Queue { get; set; } = "nexaconnect.reporting.activity.v1";
    public ushort PrefetchCount { get; set; } = 20;
}

public sealed class ActivityProjectionConsumer(
    IConnection connection,
    IOptions<ActivityConsumerOptions> options,
    IDurableInboxStore inbox,
    IServiceScopeFactory scopes,
    ILogger<ActivityProjectionConsumer> logger) : BackgroundService
{
    private const string Consumer = "reporting.activity.v1";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await channel.ExchangeDeclareAsync(options.Value.Exchange, ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);
        var arguments = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = options.Value.Exchange,
            ["x-dead-letter-routing-key"] = "reporting.activity.dead"
        };
        await channel.QueueDeclareAsync(options.Value.Queue, durable: true, exclusive: false, autoDelete: false,
            arguments: arguments, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(options.Value.Queue + ".dead", durable: true, exclusive: false, autoDelete: false,
            cancellationToken: stoppingToken);
        await channel.QueueBindAsync(options.Value.Queue, options.Value.Exchange, "*.audit.v1", cancellationToken: stoppingToken);
        await channel.QueueBindAsync(options.Value.Queue + ".dead", options.Value.Exchange, "reporting.activity.dead", cancellationToken: stoppingToken);
        await channel.BasicQosAsync(0, options.Value.PrefetchCount, false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, args) => await HandleAsync(channel, args, stoppingToken);
        await channel.BasicConsumeAsync(options.Value.Queue, autoAck: false, consumer, stoppingToken);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs args, CancellationToken cancellationToken)
    {
        Guid? eventId = null;
        try
        {
            PlatformAuditEventV1 audit = JsonSerializer.Deserialize<PlatformAuditEventV1>(args.Body.Span)
                ?? throw new JsonException("Audit event is empty.");
            eventId = audit.EventId;
            string source = ResolveSource(args.RoutingKey);
            InboxClaimResult claim = await inbox.ClaimAsync(audit.EventId, Consumer, TimeSpan.FromMinutes(2), cancellationToken);
            if (claim == InboxClaimResult.Busy)
            {
                await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: true, cancellationToken);
                return;
            }
            if (claim == InboxClaimResult.Completed)
            {
                await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken);
                return;
            }

            try
            {
                using IServiceScope scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ActivityService>()
                    .ProjectAsync(new ProjectAuditActivityCommand(audit, "nexa_connect", source), cancellationToken);
                await inbox.MarkCompletedAsync(audit.EventId, Consumer, cancellationToken);
                await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await inbox.ReleaseAsync(audit.EventId, Consumer, exception.GetType().Name, cancellationToken);
                throw;
            }
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            logger.LogWarning(exception, "Rejected permanent activity event {EventId} from {RoutingKey}.", eventId, args.RoutingKey);
            await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Activity event {EventId} processing failed for {RoutingKey}.", eventId, args.RoutingKey);
            await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: true, cancellationToken);
        }
    }

    private static string ResolveSource(string routingKey) => routingKey switch
    {
        "platform-directory.audit.v1" => "platform-directory",
        "restaurant.audit.v1" => "restaurant",
        "catalog.audit.v1" => "catalog",
        "media.audit.v1" => "media",
        "notification.audit.v1" => "notification",
        "payment.audit.v1" => "payment",
        "kitchen.audit.v1" => "kitchen",
        "customer.audit.v1" => "customer",
        _ => throw new InvalidOperationException("Audit routing key is not allowed.")
    };
}

public static class ActivityConsumerRegistration
{
    public static IServiceCollection AddActivityConsumer(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ActivityConsumerOptions>(configuration.GetSection("ActivityConsumer"));
        if (!configuration.GetValue<bool>("ActivityConsumer:Enabled")) return services;
        string value = configuration["ActivityConsumer:ConnectionString"]
            ?? throw new InvalidOperationException("ActivityConsumer:ConnectionString is required when enabled.");
        services.AddSingleton<IConnection>(_ => new ConnectionFactory { Uri = new Uri(value) }
            .CreateConnectionAsync().GetAwaiter().GetResult());
        services.AddSingleton<IDurableInboxStore, PostgresInboxStore>();
        services.AddHostedService<ActivityProjectionConsumer>();
        return services;
    }
}
