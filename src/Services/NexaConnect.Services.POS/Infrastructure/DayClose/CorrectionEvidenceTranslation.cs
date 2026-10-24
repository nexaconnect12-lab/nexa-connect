using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.POS.Domain.DayClose;

namespace NexaConnect.Services.POS.Infrastructure.DayClose;

internal static class CorrectionEvidenceTranslation
{
    public static DayCorrectionEvidence Read(SourceCutoff<PosDaySummary> manifest,CashCorrectionDeliveryProof? proof)
    {
        var events=manifest.CashCorrections?.Events??throw new InvalidOperationException("Correction source evidence absent.");
        if(manifest.EvidenceProtocolVersion!=3||proof is null||proof.Counts is null||proof.PosManifestId!=manifest.ManifestId
            ||proof.ManifestHash!=CashCorrectionEvidenceFingerprint.Calculate(events)
            ||proof.PosRevision!=manifest.SourceRevision||proof.PosRevision is not{Epoch:var epoch,Revision:>=0}||epoch==Guid.Empty)
            throw new InvalidOperationException("Correction evidence identity mismatch.");
        var c=proof.Counts;
        var result=new DayCorrectionEvidence(manifest.ManifestId,proof.PosRevision.Epoch,proof.PosRevision.Revision,proof.ManifestHash,
            proof.Adjustment,c.Expected,c.Matched,c.Missing,c.Conflicting,c.Unexpected,
            events.OrderBy(e=>e.EventId).Select(e=>new DayCorrectionItem(e.EventId,e.CorrectionId,e.OriginalSettlementId,e.WorkId,
                e.OrderId,e.TenderId,e.DrawerId,e.PostingDate,e.OccurredAtUtc,e.CashVarianceAdjustment)).ToArray());
        if(!result.IsValid(manifest.Window.FromUtc,manifest.Window.ToUtc)||result.Expected!=manifest.Summary.LateCashCorrections
            ||result.Adjustment!=manifest.Summary.LateCashCorrectionAdjustment
            ||manifest.CashCorrections!.OrganizationId!=manifest.Window.OrganizationId
            ||manifest.CashCorrections.RestaurantId!=manifest.Window.RestaurantId||manifest.CashCorrections.BranchId!=manifest.Window.BranchId)
            throw new InvalidOperationException("Correction delivery proof disagrees with source evidence.");
        return result;
    }
}
