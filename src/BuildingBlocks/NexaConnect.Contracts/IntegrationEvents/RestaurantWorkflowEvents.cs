namespace NexaConnect.Contracts.IntegrationEvents;

public interface IIntegrationEvent
{
    Guid EventId { get; }
    Guid CorrelationId { get; }
    DateTimeOffset OccurredAtUtc { get; }
}

public sealed record OrderLineSnapshot(
    Guid ProductId,
    string Name,
    decimal UnitPrice,
    int Quantity,
    string PreparationStation);

public sealed record OrderSubmittedV1(
    Guid EventId,
    Guid CorrelationId,
    DateTimeOffset OccurredAtUtc,
    Guid OrderId,
    Guid OrganizationId,
    Guid BranchId,
    IReadOnlyList<OrderLineSnapshot> Lines,
    decimal TotalAmount,
    string Currency) : IIntegrationEvent;

public sealed record InventoryReservedV1(
    Guid EventId,
    Guid CorrelationId,
    DateTimeOffset OccurredAtUtc,
    Guid OrderId,
    Guid ReservationId) : IIntegrationEvent;

public sealed record InventoryReservationRejectedV1(
    Guid EventId,
    Guid CorrelationId,
    DateTimeOffset OccurredAtUtc,
    Guid OrderId,
    string Reason) : IIntegrationEvent;

public sealed record KitchenTicketCreatedV1(
    Guid EventId,
    Guid CorrelationId,
    DateTimeOffset OccurredAtUtc,
    Guid OrderId,
    Guid TicketId,
    IReadOnlyList<OrderLineSnapshot> Lines) : IIntegrationEvent;

public sealed record PaymentCompletedV1(
    Guid EventId,
    Guid CorrelationId,
    DateTimeOffset OccurredAtUtc,
    Guid OrderId,
    Guid PaymentId,
    decimal Amount,
    string Currency,
    string Method) : IIntegrationEvent;

public sealed record PaymentFailedV1(
    Guid EventId,
    Guid CorrelationId,
    DateTimeOffset OccurredAtUtc,
    Guid OrderId,
    string Reason) : IIntegrationEvent;
