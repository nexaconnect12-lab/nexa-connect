using System.Text.Json.Serialization;
using NexaConnect.Contracts.IntegrationEvents;

namespace NexaConnect.Contracts.Reporting;

public sealed record CashCorrectionManifest(Guid OrganizationId, Guid RestaurantId, Guid BranchId,
    DateTimeOffset FromUtc, DateTimeOffset ToUtc, DateTimeOffset ObservedAtUtc,
    IReadOnlyList<PosLateCashCorrectionPostedV1> Events);

public sealed record CashCorrectionReportItem(Guid EventId, Guid CorrectionId, Guid OriginalSettlementId,
    Guid WorkId, Guid OrderId, Guid TenderId, Guid DrawerId, DateOnly PostingDate,
    DateTimeOffset PostedAtUtc, string Currency,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] decimal Adjustment,
    string Status);

public sealed record CashCorrectionReport(Guid OrganizationId, Guid BranchId, DateTimeOffset FromUtc,
    DateTimeOffset ToUtc, DateTimeOffset SourceObservedAtUtc, DateTimeOffset ComparedAtUtc,
    string ManifestHash, string Status, int Expected, int Matched, int Missing, int Conflicting, int Unexpected,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] decimal SourceAdjustment,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] decimal ProjectedAdjustment,
    IReadOnlyList<CashCorrectionReportItem> Items);
