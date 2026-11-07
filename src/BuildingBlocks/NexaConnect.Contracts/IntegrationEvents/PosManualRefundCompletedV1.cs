namespace NexaConnect.Contracts.IntegrationEvents;

public sealed record PosManualRefundCompletedV1(Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc,
    Guid OrganizationId, Guid RestaurantId, Guid BranchId, Guid OrderId, Guid SettlementId,
    Guid OperationId, string Method, decimal Amount, string Currency, Guid? CashMovementId)
{
    public const string EventType = "pos.manual-refund-completed.v1";
}
