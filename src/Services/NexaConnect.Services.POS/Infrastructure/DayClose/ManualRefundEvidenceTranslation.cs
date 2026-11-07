using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.POS.Domain.DayClose;

namespace NexaConnect.Services.POS.Infrastructure.DayClose;

internal static class ManualRefundEvidenceTranslation
{
    internal static DayManualRefundEvidence? Read(SourceCutoff<PosDaySummary> manifest, ManualRefundDeliveryProof? proof)
    {
        if(manifest.EvidenceProtocolVersion < 4) return null;
        var s=manifest.Summary.ManualRefunds;
        if(s is null || proof is null || proof.PosManifestId!=manifest.ManifestId || proof.PosRevision!=manifest.SourceRevision
            || proof.CashAmount!=s.CashAmount || proof.PromptPayAmount!=s.PromptPayAmount
            || proof.CompletedCount!=s.CompletedCount || proof.EvidenceVersion!=s.EvidenceVersion)
            throw new InvalidOperationException("Manual refund proof does not match the retained POS inventory.");
        var result=new DayManualRefundEvidence(proof.PosManifestId,proof.PosRevision.Epoch,proof.PosRevision.Revision,
            proof.CashAmount,proof.PromptPayAmount,proof.CompletedCount,proof.EvidenceVersion,proof.Matched);
        if(!result.IsValid)throw new InvalidOperationException("Manual refund proof invalid.");
        return result;
    }
}
