namespace NexaConnect.Contracts.Reporting;

public sealed record SourceSealCommand(Guid OperationId, EndOfDayWindow Window, Guid ManifestId,
    SourceFinancialRevision ExpectedRevision);
public sealed record SourceDaySeal(Guid SealId, Guid OperationId, EndOfDayWindow Window, Guid ManifestId,
    SourceFinancialRevision SourceRevision, DateTimeOffset SealedAtUtc);
public sealed record SourceSealChange(long Revision, DateTimeOffset RecordedAtUtc);
public sealed record SourceSealRead<T>(SourceDaySeal Seal, SourceCutoff<T> Manifest, long PendingChanges,
    bool JournalComplete, IReadOnlyList<SourceSealChange> Changes, bool ChangesTruncated);
public sealed record SealedReconciliationCommand(EndOfDayWindow Window, Guid OrderSealId, Guid PaymentSealId);
public sealed record SealedReconciliation(Guid CheckId, EndOfDayWindow Window, Guid OrderSealId, Guid PaymentSealId,
    Guid OrderManifestId, Guid PaymentManifestId, bool DeliveryComplete, DateTimeOffset CheckedAtUtc,
    CutoffFactCounts Sales, CutoffFactCounts Payments, CutoffFactCounts Refunds);
