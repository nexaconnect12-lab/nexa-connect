using System.Text.Json;
using Microsoft.Extensions.Options;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.POS.Application.OrderSettlements;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Diagnostics;

namespace NexaConnect.Services.POS.Infrastructure.Messaging;

public sealed class OrderSettlementConsumerOptions
{
    public bool Enabled{get;set;}
    public string ConnectionString{get;set;}="";
    public string Exchange{get;set;}="nexaconnect.events";
    public string Queue{get;set;}="nexaconnect.pos.order-manual-tender.v1";
    public ushort PrefetchCount{get;set;}=10;
}

public sealed class OrderSettlementConsumer(IConnection connection,IOptions<OrderSettlementConsumerOptions> options,
    IServiceScopeFactory scopes,ILogger<OrderSettlementConsumer> logger):BackgroundService
{
    private static readonly ActivitySource Activities=new("nexaconnect-pos");
    private static readonly System.Diagnostics.Metrics.Meter Meter=new("nexaconnect-pos");
    private static readonly System.Diagnostics.Metrics.Counter<long> Outcomes=Meter.CreateCounter<long>("pos.order_settlement.outcomes");
    private readonly TaskCompletionSource readiness=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task WaitUntilReadyAsync(CancellationToken cancellationToken)=>readiness.Task.WaitAsync(cancellationToken);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using IChannel channel=await connection.CreateChannelAsync(cancellationToken:stoppingToken);
        await channel.ExchangeDeclareAsync(options.Value.Exchange,ExchangeType.Topic,durable:true,cancellationToken:stoppingToken);
        var arguments=new Dictionary<string,object?>{{"x-dead-letter-exchange",options.Value.Exchange},{"x-dead-letter-routing-key","pos.order-manual-tender.dead"}};
        await channel.QueueDeclareAsync(options.Value.Queue,durable:true,exclusive:false,autoDelete:false,arguments:arguments,cancellationToken:stoppingToken);
        await channel.QueueDeclareAsync(options.Value.Queue+".dead",durable:true,exclusive:false,autoDelete:false,cancellationToken:stoppingToken);
        await channel.QueueBindAsync(options.Value.Queue,options.Value.Exchange,"order.manual-tender-settled.v1",cancellationToken:stoppingToken);
        await channel.QueueBindAsync(options.Value.Queue+".dead",options.Value.Exchange,"pos.order-manual-tender.dead",cancellationToken:stoppingToken);
        await channel.BasicQosAsync(0,options.Value.PrefetchCount,false,stoppingToken);
        var consumer=new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync+=async(_,args)=>await HandleAsync(channel,args,stoppingToken);
        await channel.BasicConsumeAsync(options.Value.Queue,false,consumer,stoppingToken);
        readiness.TrySetResult();
        await Task.Delay(Timeout.Infinite,stoppingToken);
    }

    private async Task HandleAsync(IChannel channel,BasicDeliverEventArgs args,CancellationToken cancellationToken)
    {
        Guid? eventId=null;
        try
        {
            var value=JsonSerializer.Deserialize<OrderManualTenderSettledV1>(args.Body.Span)??throw new JsonException("Settlement event is empty.");eventId=value.EventId;
            using IDisposable? logScope=logger.BeginScope(new Dictionary<string,object?>{{"CorrelationId",value.CorrelationId},{"EventId",value.EventId},{"OrderId",value.OrderId},{"TerminalId",value.TerminalId}});
            using Activity? activity=Activities.StartActivity("pos.order-settlement.project",ActivityKind.Consumer);
            activity?.SetTag("messaging.message.id",value.EventId);activity?.SetTag("nexaconnect.correlation_id",value.CorrelationId);activity?.SetTag("order.id",value.OrderId);activity?.SetTag("pos.terminal.id",value.TerminalId);
            using IServiceScope scope=scopes.CreateScope();
            OrderSettlementProjectionStatus status=await scope.ServiceProvider.GetRequiredService<OrderSettlementProjectionService>().ProjectAsync(value,cancellationToken);
            logger.LogInformation("POS Order settlement {ProjectionStatus} for event {EventId}, order {OrderId}, terminal {TerminalId}.",status,eventId,value.OrderId,value.TerminalId);
            Outcomes.Add(1,new KeyValuePair<string,object?>("status",status==OrderSettlementProjectionStatus.Applied?"applied":"replayed"));
            await channel.BasicAckAsync(args.DeliveryTag,false,cancellationToken);
        }
        catch(Exception exception) when(exception is JsonException or ArgumentException or OrderSettlementProjectionConflictException)
        {
            logger.LogWarning(exception,"Rejected permanent POS Order settlement event {EventId}.",eventId);
            Outcomes.Add(1,new KeyValuePair<string,object?>("status","dead_lettered"));
            await channel.BasicNackAsync(args.DeliveryTag,false,false,cancellationToken);
        }
        catch(Exception exception) when(exception is not OperationCanceledException)
        {
            logger.LogError(exception,"POS Order settlement event {EventId} will be retried.",eventId);
            Outcomes.Add(1,new KeyValuePair<string,object?>("status","retry"));
            await channel.BasicNackAsync(args.DeliveryTag,false,true,cancellationToken);
        }
    }
}

public static class OrderSettlementConsumerRegistration
{
    public static IServiceCollection AddOrderSettlementConsumer(this IServiceCollection services,IConfiguration configuration)
    {
        services.Configure<OrderSettlementConsumerOptions>(configuration.GetSection("OrderSettlementConsumer"));
        if(!configuration.GetValue<bool>("OrderSettlementConsumer:Enabled"))return services;
        string value=configuration["OrderSettlementConsumer:ConnectionString"]??throw new InvalidOperationException("OrderSettlementConsumer:ConnectionString is required when enabled.");
        services.AddSingleton<IConnection>(_=>new ConnectionFactory{Uri=new Uri(value)}.CreateConnectionAsync().GetAwaiter().GetResult());
        services.AddHostedService<OrderSettlementConsumer>();return services;
    }
}
