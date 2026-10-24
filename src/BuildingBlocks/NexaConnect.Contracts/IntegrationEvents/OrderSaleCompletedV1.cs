namespace NexaConnect.Contracts.IntegrationEvents;

/// <summary>Immutable receipt evidence for one fully paid sale; contains no customer or payment credentials.</summary>
public sealed record OrderSaleCompletedV1(
    Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc,
    Guid OrganizationId, Guid RestaurantId, Guid BranchId, Guid OrderId,
    Guid PaymentId, string PaymentOrigin, string Method, string Currency,
    string Channel, string ServiceType, DateTimeOffset OrderedAtUtc, DateTimeOffset PaidAtUtc,
    string ReceiptNumber, decimal SubtotalAmount, decimal ServiceChargeAmount, decimal TaxAmount,
    decimal TotalAmount) : IIntegrationEvent;
