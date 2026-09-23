namespace NexaConnect.Contracts.IntegrationEvents;

// Restricted financial integration data. Never include this payload in operational logs.
public sealed record PosCashCloseSnapshotV1(
    Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc,
    Guid OrganizationId, Guid RestaurantId, Guid BranchId, Guid StoreId,
    Guid CashSessionId, Guid ShiftId, DateTimeOffset ClosedAtUtc,
    string Currency, decimal ExpectedAmount, decimal CountedAmount, decimal VarianceAmount,
    long SnapshotVersion, long FinancialVersion, long ReviewVersion, string ReviewStatus) : IIntegrationEvent
{
    public override string ToString() => "PosCashCloseSnapshotV1 [redacted]";
}
