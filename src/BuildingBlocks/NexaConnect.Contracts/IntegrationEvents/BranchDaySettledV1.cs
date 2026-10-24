namespace NexaConnect.Contracts.IntegrationEvents;

public sealed record SettlementTenderV1(string Method,string Currency,decimal Amount);
public sealed record SettlementSourceV1(string Source,Guid SealId,Guid Epoch,long Revision);
public sealed record BranchDaySettledV1(Guid EventId,Guid CorrelationId,DateTimeOffset OccurredAtUtc,Guid SettlementId,
    Guid OrganizationId,Guid RestaurantId,Guid BranchId,DateOnly BusinessDate,Guid ApprovalId,long SealVersion,
    string Currency,decimal GrossSales,decimal CompletedRefunds,decimal NetSales,decimal CashVariance,
    IReadOnlyList<SettlementTenderV1> Tenders,IReadOnlyList<SettlementSourceV1> Sources):IIntegrationEvent;
