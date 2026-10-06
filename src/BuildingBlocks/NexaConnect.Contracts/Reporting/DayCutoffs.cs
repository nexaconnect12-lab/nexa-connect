using NexaConnect.Contracts.IntegrationEvents;

namespace NexaConnect.Contracts.Reporting;

// Retained source observations. These contracts grant no settlement authority or write lock.
public sealed record SourceCutoffCommand(Guid OperationId, EndOfDayWindow Window);
public sealed record SourceCutoff<T>(Guid ManifestId, Guid OperationId, long Generation, EndOfDayWindow Window,
    DateTimeOffset CapturedAtUtc, string EvidenceVersion, T Summary,
    IReadOnlyList<OrderSaleCompletedV1> Sales, IReadOnlyList<PaymentRefundedV1> Refunds,
    IReadOnlyList<string>? OwnerEvidence = null);
public sealed record SourceCutoffRead<T>(SourceCutoff<T> Manifest, bool Current);
public sealed record CutoffReconciliationCommand(EndOfDayWindow Window, Guid OrderManifestId, Guid PaymentManifestId);
public sealed record CutoffFactCounts(int Expected, int Matched, int Missing, int Conflicting, int Unexpected);
public sealed record CutoffReconciliation(Guid CheckId, EndOfDayWindow Window, Guid OrderManifestId,
    Guid PaymentManifestId, bool SourcesCurrent, string Status, DateTimeOffset CheckedAtUtc,
    CutoffFactCounts Sales, CutoffFactCounts Payments, CutoffFactCounts Refunds);
