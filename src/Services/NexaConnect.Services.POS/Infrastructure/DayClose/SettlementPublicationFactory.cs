using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.POS.Domain.DayClose;
namespace NexaConnect.Services.POS.Infrastructure.DayClose;

internal static class SettlementPublicationFactory
{
    public static BranchDaySettledV1 Legacy(SettlementReceipt receipt)
    {
        var s=receipt.Snapshot;var day=receipt.Identity;var seals=s.Seals??throw new InvalidOperationException("Settlement source seals absent.");
        return new(receipt.EventId,receipt.CorrelationId,receipt.SettledAtUtc,receipt.SettlementId,day.OrganizationId,day.RestaurantId,day.BranchId,day.BusinessDate,
            receipt.ApprovalId,receipt.SealVersion,s.Currency,s.GrossSales,s.CompletedRefunds,s.NetSales,s.CashVariance,
            s.Tenders.Select(t=>new SettlementTenderV1(t.Method,t.Currency,t.Amount)).ToArray(),
            new[]{("Order",seals.Order),("Payment",seals.Payment),("POS",seals.Pos)}.Select(p=>new SettlementSourceV1(p.Item1,p.Item2.SealId,p.Item2.RevisionEpoch,p.Item2.SourceRevision)).ToArray());
    }
    public static BranchDaySettledV2 Current(SettlementReceipt receipt)
    {
        var s=receipt.Snapshot;var c=s.Corrections??throw new InvalidOperationException("Settlement correction proof absent.");
        if(!s.IsValid(receipt.SettledAtUtc)||s.Blockers().Length!=0||!c.DeliveryComplete)throw new InvalidOperationException("Settlement proof invalid.");
        return new(receipt.EventId,receipt.CorrelationId,receipt.SettledAtUtc,Legacy(receipt),receipt.DecisionId,s.FromUtc,s.ToUtc,
            new(c.ManifestId,c.RevisionEpoch,c.SourceRevision,c.ManifestHash,c.Expected,c.Matched,c.Missing,c.Conflicting,c.Unexpected,c.Adjustment,
                c.Items.Select(i=>new SettlementCorrectionItemV2(i.EventId,i.CorrectionId,i.OriginalSettlementId,i.WorkId,i.OrderId,i.TenderId,i.DrawerId,i.PostingDate,i.PostedAtUtc,i.Adjustment)).ToArray()));
    }
}
