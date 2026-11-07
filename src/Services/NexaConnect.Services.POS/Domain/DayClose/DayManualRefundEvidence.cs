namespace NexaConnect.Services.POS.Domain.DayClose;

public sealed record DayManualRefundEvidence(Guid ManifestId, Guid RevisionEpoch, long SourceRevision,
    decimal CashAmount, decimal PromptPayAmount, int CompletedCount, string? EvidenceVersion, bool Matched)
{
    public bool IsValid => ManifestId != Guid.Empty && RevisionEpoch != Guid.Empty && SourceRevision >= 0
        && CashAmount >= 0 && PromptPayAmount >= 0 && CashAmount < 1_000_000_000_000_000m && PromptPayAmount < 1_000_000_000_000_000m
        && decimal.Round(CashAmount,4)==CashAmount && decimal.Round(PromptPayAmount,4)==PromptPayAmount
        && CompletedCount is >= 0 and <= 10000
        && (CompletedCount == 0 ? CashAmount == 0 && PromptPayAmount == 0 && EvidenceVersion is null
            : CashAmount + PromptPayAmount > 0 && EvidenceVersion is {Length:64} && EvidenceVersion.All(char.IsAsciiHexDigit));
}
