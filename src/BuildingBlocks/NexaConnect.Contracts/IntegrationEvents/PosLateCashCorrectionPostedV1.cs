namespace NexaConnect.Contracts.IntegrationEvents;
public sealed record PosLateCashCorrectionPostedV1(Guid EventId,Guid CorrelationId,DateTimeOffset OccurredAtUtc,Guid OrganizationId,Guid RestaurantId,Guid BranchId,
 Guid CorrectionId,Guid WorkId,Guid OriginalSettlementId,Guid OrderId,Guid TenderId,Guid DrawerId,long ReviewedVersion,DateOnly PostingDate,DateTimeOffset PostingFromUtc,
 DateTimeOffset PostingToUtc,string Currency,decimal CashVarianceAdjustment):IIntegrationEvent;
