using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Infrastructure.Messaging;
using NexaConnect.Services.Order.Application.Workflow;

namespace NexaConnect.Services.Order.Infrastructure.Messaging;

public sealed class PostgresIntegrationEventPublisher(IOutboxStore outbox) : IIntegrationEventPublisher
{
    public Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        Guid aggregateId = integrationEvent switch
        {
            OrderSubmittedV1 value => value.OrderId,
            InventoryReservedV1 value => value.OrderId,
            InventoryReservationRejectedV1 value => value.OrderId,
            KitchenTicketCreatedV1 value => value.OrderId,
            PaymentCompletedV1 value => value.OrderId,
            PaymentFailedV1 value => value.OrderId,
            PaymentAuthorizationUncertainV1 value => value.OrderId,
            PaymentAuthorizationReconciledV1 value => value.OrderId,
            PaymentCaptureReconciledV1 value => value.OrderId,
            _ => throw new InvalidOperationException($"Unsupported integration event type {integrationEvent.GetType().Name}.")
        };
        var message = new OutboxMessage(
            integrationEvent.EventId,
            integrationEvent.GetType().Name,
            1,
            "order",
            aggregateId,
            JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType()),
            integrationEvent.CorrelationId.ToString(),
            integrationEvent.OccurredAtUtc);
        return outbox.EnqueueAsync(message, cancellationToken);
    }
}

public sealed class InMemoryIntegrationEventPublisher : IIntegrationEventPublisher
{
    private readonly List<IIntegrationEvent> events = [];
    public IReadOnlyList<IIntegrationEvent> Events => events;

    public Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        events.Add(integrationEvent);
        return Task.CompletedTask;
    }
}
