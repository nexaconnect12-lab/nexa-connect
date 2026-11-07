namespace NexaConnect.Contracts.Reporting;

// Exact canonical completion-inventory digest, including operation, original sale, tender,
// amount, currency, completion time and drawer movement. No staff or transfer proof data.
public sealed record ManualRefundDeliveryProof(Guid PosManifestId, SourceFinancialRevision PosRevision,
    decimal CashAmount, decimal PromptPayAmount, int CompletedCount, string? EvidenceVersion, bool Matched);
