using System.Diagnostics;
using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Reporting.Application;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NexaConnect.Services.Reporting.Infrastructure.Messaging;

public sealed class CashCloseConsumer(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<CashCloseConsumer> logger) : BackgroundService
{
    private static readonly ActivitySource Activities = new("nexaconnect-reporting");
    public const string RoutingKey = "pos.cash-close.snapshot.v1";
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string uri = configuration["CashCloseConsumer:ConnectionString"] ?? throw new InvalidOperationException("CashCloseConsumer:ConnectionString is required.");
        string exchange = configuration["CashCloseConsumer:Exchange"] ?? "nexaconnect.events";
        string queue = configuration["CashCloseConsumer:Queue"] ?? "nexaconnect.reporting.cash-close.v1";
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await new ConnectionFactory { Uri = new Uri(uri) }.CreateConnectionAsync(stoppingToken);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
                await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, true, cancellationToken: stoppingToken);
                await channel.QueueDeclareAsync(queue, true, false, false,
                    new Dictionary<string, object?> { ["x-dead-letter-exchange"] = exchange, ["x-dead-letter-routing-key"] = queue + ".dead" }, cancellationToken: stoppingToken);
                await channel.QueueDeclareAsync(queue + ".dead", true, false, false, cancellationToken: stoppingToken);
                await channel.QueueBindAsync(queue, exchange, RoutingKey, cancellationToken: stoppingToken);
                await channel.QueueBindAsync(queue + ".dead", exchange, queue + ".dead", cancellationToken: stoppingToken);
                await channel.BasicQosAsync(0, 16, false, stoppingToken);
                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                channel.ChannelShutdownAsync += (_, _) => { closed.TrySetResult(); return Task.CompletedTask; };
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (_, delivery) => await Handle(channel, delivery, stoppingToken);
                await channel.BasicConsumeAsync(queue, false, consumer, stoppingToken);
                logger.LogInformation("Cash-close consumer ready");
                await closed.Task.WaitAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            { logger.LogWarning("Cash-close consumer connection unavailable"); }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task Handle(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        using var activity = Activities.StartActivity("cash-close.project", ActivityKind.Consumer);
        IDisposable? correlationScope = null;
        IDisposable? logScope = null;
        try
        {
            if (delivery.RoutingKey != RoutingKey || delivery.Body.Length > 32768) throw new ArgumentException("Invalid cash-close envelope.");
            var value = JsonSerializer.Deserialize<PosCashCloseSnapshotV1>(delivery.Body.Span) ?? throw new JsonException();
            // Validate before using the correlation identifier in telemetry.
            CashCloseReporting.Translate(value);
            correlationScope = NexaConnect.Observability.CorrelationContext.Push(value.CorrelationId.ToString("D"));
            logScope = logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = value.CorrelationId });
            using var scope = scopes.CreateScope();
            bool changed = await scope.ServiceProvider.GetRequiredService<CashCloseReporting>().ProjectAsync(value, ct);
            logger.LogInformation("Cash-close snapshot processed; changed {Changed}", changed);
            await channel.BasicAckAsync(delivery.DeliveryTag, false, ct);
        }
        catch (Exception e) when (e is JsonException or ArgumentException or OverflowException)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            logger.LogWarning("Cash-close snapshot rejected");
            await channel.BasicNackAsync(delivery.DeliveryTag, false, false, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            logger.LogWarning("Cash-close projection unavailable; delivery will retry");
            await Task.Delay(500, ct);
            await channel.BasicNackAsync(delivery.DeliveryTag, false, true, ct);
        }
        finally
        {
            logScope?.Dispose();
            correlationScope?.Dispose();
        }
    }
}
