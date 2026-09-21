using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexaConnect.Infrastructure.Messaging;
using NexaConnect.Services.Order.Application.Workflow;
using NexaConnect.Services.Order.Infrastructure.Messaging;
using RabbitMQ.Client;

namespace NexaConnect.UnitTests;

public sealed class OrderReconciliationLifetimeTests
{
    [Fact]
    public void Hosted_consumer_accepts_scoped_application_service_with_development_lifetime_validation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        // Build-time graph validation must not open infrastructure connections.
        services.AddSingleton<IConnection>(_ => throw new InvalidOperationException("No broker connection expected."));
        services.AddSingleton<IDurableInboxStore>(_ => throw new InvalidOperationException("No inbox connection expected."));
        services.AddSingleton<IOrderRepository>(_ => throw new InvalidOperationException("No persistence expected."));
        services.AddScoped<IInventoryReservationPort>(_ => throw new InvalidOperationException("No inventory call expected."));
        services.AddScoped<IKitchenPort>(_ => throw new InvalidOperationException("No kitchen call expected."));
        services.AddScoped<IPaymentPort>(_ => throw new InvalidOperationException("No provider call expected."));
        services.AddScoped<IIntegrationEventPublisher>(_ => throw new InvalidOperationException("No publication expected."));
        services.AddScoped<PaymentReconciliationApplicationService>();
        services.AddHostedService<PaymentReconciliationConsumer>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        { ValidateOnBuild = true, ValidateScopes = true });
        Assert.NotNull(provider.GetRequiredService<IServiceScopeFactory>());
    }
}
