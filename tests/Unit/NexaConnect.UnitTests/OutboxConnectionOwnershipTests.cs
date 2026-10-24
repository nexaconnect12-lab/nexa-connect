using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexaConnect.Infrastructure.Messaging;
using RabbitMQ.Client;

namespace NexaConnect.UnitTests;

public sealed class OutboxConnectionOwnershipTests
{
    [Fact]
    public async Task PublisherResolutionNeverBorrowsConsumerConnectionOrEagerlyConnects()
    {
        int consumerResolutions=0;
        var services=new ServiceCollection();services.AddLogging();services.AddOptions();
        services.AddSingleton<IConnection>(_=>{consumerResolutions++;throw new InvalidOperationException("Consumer connection cannot be borrowed.");});
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["ConnectionStrings:Order"]="Host=127.0.0.1;Database=unit;Username=unit",
            ["Outbox:ConnectionString"]="amqp://guest:guest@127.0.0.1:1/"
        }).Build();
        services.AddPostgresOutbox(configuration,"Order");
        await using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateOnBuild=true,ValidateScopes=true});
        var transport=provider.GetRequiredService<IOutboxTransport>();
        Assert.IsType<RabbitMqOutboxTransport>(transport);Assert.Equal(0,consumerResolutions);
        Assert.Same(transport,provider.GetRequiredService<IOutboxTransport>());
    }
}
