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

public sealed record KitchenTicketQueuedV1(Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc, Guid OrganizationId, Guid RestaurantId, Guid BranchId, Guid OrderId, Guid TicketId, Guid PreparationStationId, int ServiceSequence, string Status, string? RequestCorrelationId = null) : IIntegrationEvent;
public sealed record KitchenTicketStatusChangedV1(Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc, Guid OrganizationId, Guid RestaurantId, Guid BranchId, Guid OrderId, Guid TicketId, string FromStatus, string ToStatus, long ConcurrencyVersion, string? ReasonCode, string? RequestCorrelationId = null) : IIntegrationEvent;

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

public sealed record PaymentIntentCreatedV1(
    Guid EventId,
    Guid CorrelationId,
    DateTimeOffset OccurredAtUtc,
    Guid OrganizationId,
    Guid RestaurantId,
    Guid BranchId,
    Guid OrderId,
    Guid PaymentIntentId,
    decimal Amount,
    string Currency,
    string PaymentMethod,
    string Status) : IIntegrationEvent;

public sealed record PaymentAuthorizationStartedV1(Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc,
    Guid OrganizationId, Guid RestaurantId, Guid BranchId, Guid OrderId, Guid PaymentIntentId, decimal Amount,
    string Currency) : IIntegrationEvent;

public sealed record PaymentAuthorizedV1(Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc,
    Guid OrganizationId, Guid RestaurantId, Guid BranchId, Guid OrderId, Guid PaymentIntentId, decimal Amount,
    string Currency, string PaymentMethod) : IIntegrationEvent;

public sealed record PaymentAuthorizationFailedV1(Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc,
    Guid OrganizationId, Guid RestaurantId, Guid BranchId, Guid OrderId, Guid PaymentIntentId,
    string FailureCode) : IIntegrationEvent;

public sealed record PaymentAuthorizationUncertainV1(Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc,
    Guid OrderId, Guid? PaymentIntentId, string Reason) : IIntegrationEvent;

public sealed record PaymentAuthorizationReconciledV1(Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc,
    Guid OrganizationId, Guid OrderId, Guid PaymentIntentId, string Outcome, string? FailureCode) : IIntegrationEvent;

public sealed record NotificationQueuedV1(
    Guid EventId,
    Guid CorrelationId,
    DateTimeOffset OccurredAtUtc,
    Guid NotificationId,
    Guid OrganizationId,
    string Channel) : IIntegrationEvent;

public sealed record NotificationDeliveryStatusChangedV1(Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc,
    Guid NotificationId, Guid OrganizationId, string Channel, string Status, string ProviderCode, string RequestCorrelationId) : IIntegrationEvent;

public sealed record NotificationRequestedV1(
    Guid EventId,
    Guid CorrelationId,
    DateTimeOffset OccurredAtUtc,
    Guid OrganizationId,
    string Channel,
    string Recipient,
    string Subject,
    string Body,
    string SourceService) : IIntegrationEvent;

public sealed record CatalogMenuItemChangedV1(
    Guid EventId,
    Guid CorrelationId,
    DateTimeOffset OccurredAtUtc,
    Guid OrganizationId,
    Guid BranchId,
    Guid ProductId,
    string Name,
    decimal UnitPrice,
    string Currency,
    string PreparationStation,
    bool Available) : IIntegrationEvent;

public sealed record InventoryStockSetV1(Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc, Guid OrganizationId, Guid BranchId, Guid ProductId, decimal AvailableQuantity) : IIntegrationEvent;
public sealed record InventoryReservationCreatedV1(Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc, Guid OrganizationId, Guid BranchId, Guid OrderId, Guid ReservationId, IReadOnlyCollection<InventoryReservationLineV1> Lines) : IIntegrationEvent;
public sealed record InventoryReservationReleasedV1(Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc, Guid OrganizationId, Guid OrderId) : IIntegrationEvent;
public sealed record InventoryReservationLineV1(Guid ProductId, decimal Quantity);
