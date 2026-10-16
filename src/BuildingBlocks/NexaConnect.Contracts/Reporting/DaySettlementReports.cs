using NexaConnect.Contracts.IntegrationEvents;
namespace NexaConnect.Contracts.Reporting;

public sealed record SettlementReportRange(Guid OrganizationId,Guid BranchId,DateOnly FromDate,DateOnly ToDate);
// Original publications only. Exactly one version per immutable POS receipt; no synthetic upgrade.
public sealed record SettlementPublication(BranchDaySettledV1? V1,BranchDaySettledV2? V2);
public sealed record SettlementManifest(Guid OrganizationId,Guid RestaurantId,Guid BranchId,DateOnly FromDate,DateOnly ToDate,
    DateTimeOffset ObservedAtUtc,IReadOnlyList<SettlementPublication> Events);
public sealed record SettlementReportItem(Guid EventId,Guid SettlementId,DateOnly BusinessDate,DateTimeOffset SettledAtUtc,
    Guid ApprovalId,long SealVersion,string Currency,string GrossSales,string CompletedRefunds,string NetSales,string CashVariance,
    int ContractVersion,string CorrectionEvidence,SettlementCorrectionsV2? Corrections,string Status,
    IReadOnlyList<SettlementSourceV1> Sources,Guid? DecisionId,DateTimeOffset? FromUtc,DateTimeOffset? ToUtc);
public sealed record SettlementReport(Guid OrganizationId,Guid BranchId,DateOnly FromDate,DateOnly ToDate,
    DateTimeOffset SourceObservedAtUtc,DateTimeOffset ComparedAtUtc,string ManifestHash,string Status,
    int Expected,int Matched,int Missing,int Conflicting,int Unexpected,IReadOnlyList<SettlementReportItem> Items);
