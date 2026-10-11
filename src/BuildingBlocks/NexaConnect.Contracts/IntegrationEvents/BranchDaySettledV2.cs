using System.Text.Json.Serialization;
namespace NexaConnect.Contracts.IntegrationEvents;

public sealed record SettlementCorrectionItemV2(Guid EventId,Guid CorrectionId,Guid OriginalSettlementId,Guid WorkId,
    Guid OrderId,Guid TenderId,Guid DrawerId,DateOnly PostingDate,DateTimeOffset PostedAtUtc,
    [property:JsonNumberHandling(JsonNumberHandling.WriteAsString|JsonNumberHandling.AllowReadingFromString)] decimal Adjustment);
public sealed record SettlementCorrectionsV2(Guid ManifestId,Guid RevisionEpoch,long SourceRevision,string ManifestHash,
    int Expected,int Matched,int Missing,int Conflicting,int Unexpected,
    [property:JsonNumberHandling(JsonNumberHandling.WriteAsString|JsonNumberHandling.AllowReadingFromString)] decimal Adjustment,
    IReadOnlyList<SettlementCorrectionItemV2> Items);
public sealed record BranchDaySettledV2(Guid EventId,Guid CorrelationId,DateTimeOffset OccurredAtUtc,
    BranchDaySettledV1 Settlement,Guid DecisionId,DateTimeOffset FromUtc,DateTimeOffset ToUtc,
    SettlementCorrectionsV2 Corrections):IIntegrationEvent;
