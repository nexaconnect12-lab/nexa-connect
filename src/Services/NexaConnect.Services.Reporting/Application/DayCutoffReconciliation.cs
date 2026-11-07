using NexaConnect.Contracts.Reporting;
using NexaConnect.Contracts.Platform;

namespace NexaConnect.Services.Reporting.Application;

public interface ICutoffSources
{
    Task<SourceCutoffRead<OrderDaySummary>> OrderAsync(EndOfDayWindow window, Guid id, string bearer, CancellationToken ct);
    Task<SourceCutoffRead<PaymentDaySummary>> PaymentAsync(EndOfDayWindow window, Guid id, string bearer, CancellationToken ct);
    Task<SourceCutoffRead<PosDaySummary>> PosAsync(EndOfDayWindow window, Guid id, string bearer, CancellationToken ct) => throw new InvalidOperationException("POS evidence capability unavailable.");
}
public sealed class DayCutoffReconciliation(ICutoffSources sources, IReportingCustomerAuthorizer authorization,
    IFinancialCompletenessRepository repository, ICashCorrectionFactRepository corrections, IManualRefundFinancialFacts? manualRefunds=null)
{
    public async Task<CutoffReconciliation> CheckAsync(CutoffReconciliationCommand command,string bearer,CancellationToken ct)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(25));ct=deadline.Token;
        var w=command.Window;
        if(w.OrganizationId==Guid.Empty || w.RestaurantId==Guid.Empty || w.BranchId==Guid.Empty
            || w.FromUtc==default || w.ToUtc<=w.FromUtc || w.ToUtc-w.FromUtc>TimeSpan.FromHours(27) || w.ToUtc>DateTimeOffset.UtcNow
            || command.OrderManifestId==Guid.Empty || command.PaymentManifestId==Guid.Empty || command.PosManifestId==Guid.Empty)throw new ArgumentException();
        if(!await authorization.IsGrantedAsync(w.OrganizationId,w.BranchId,ProductPermissions.ReportingSalesRead,bearer,ct))throw new UnauthorizedAccessException();
        var order=await sources.OrderAsync(w,command.OrderManifestId,bearer,ct);
        var payment=await sources.PaymentAsync(w,command.PaymentManifestId,bearer,ct);
        var pos=await sources.PosAsync(w,command.PosManifestId,bearer,ct);
        ValidatePos(pos,w,command.PosManifestId);
        if(order.Manifest.Window!=w || payment.Manifest.Window!=w || order.Manifest.ManifestId!=command.OrderManifestId
            || payment.Manifest.ManifestId!=command.PaymentManifestId || order.Manifest.Summary.Window!=w || payment.Manifest.Summary.Window!=w
            || order.Manifest.Refunds.Count!=0 || payment.Manifest.Sales.Count!=0)throw new InvalidOperationException("Cutoff source scope invalid.");
        var sales=order.Manifest.Sales.Select(SaleFinancialReporting.Translate).ToArray();
        var refunds=payment.Manifest.Refunds.Select(RefundFinancialReporting.Translate).ToArray();
        if(sales.Any(x=>x.RestaurantId!=w.RestaurantId)||refunds.Any(x=>x.RestaurantId!=w.RestaurantId))throw new InvalidOperationException("Cutoff source ownership invalid.");
        if(order.Manifest.Summary.EvidenceGaps==0)
        {
            var inDay=sales.Where(x=>x.OrderedAtUtc>=w.FromUtc&&x.OrderedAtUtc<w.ToUtc).ToArray();
            var tenders=sales.Where(x=>x.PaidAtUtc>=w.FromUtc&&x.PaidAtUtc<w.ToUtc)
                .GroupBy(x=>(x.Method,x.Currency)).Select(g=>new TenderTotal(g.Key.Method,g.Key.Currency,g.Sum(x=>x.TotalAmount)))
                .OrderBy(x=>x.Method,StringComparer.Ordinal).ThenBy(x=>x.Currency,StringComparer.Ordinal).ToArray();
            if(inDay.Length!=order.Manifest.Summary.CompletedOrders || inDay.Sum(x=>x.TotalAmount)!=order.Manifest.Summary.GrossSales
                || !tenders.SequenceEqual(order.Manifest.Summary.Tenders.OrderBy(x=>x.Method,StringComparer.Ordinal).ThenBy(x=>x.Currency,StringComparer.Ordinal)))
                throw new InvalidOperationException("Cutoff sale manifest disagrees with source summary.");
        }
        if(payment.Manifest.Summary.EvidenceGaps==0 && refunds.Sum(x=>x.Amount)!=payment.Manifest.Summary.CompletedRefunds)
            throw new InvalidOperationException("Cutoff refund manifest disagrees with source summary.");
        var source=new FinancialCompletenessSource(new(w.OrganizationId,w.BranchId,w.FromUtc,w.ToUtc),
            order.Manifest.CapturedAtUtc,payment.Manifest.CapturedAtUtc,sales.Length+order.Manifest.Summary.EvidenceGaps,
            refunds.Length+payment.Manifest.Summary.EvidenceGaps,order.Manifest.Summary.EvidenceGaps,payment.Manifest.Summary.EvidenceGaps,0,0,sales,refunds);
        var check=await repository.CheckAsync(source,ct);
        var correctionCheck=await corrections.CompareAsync(pos.Manifest.CashCorrections!,ct);
        ManualRefundDeliveryProof? manualProof=null;
        if(pos.Manifest.EvidenceProtocolVersion==4)
        {
            var expected=pos.Manifest.Summary.ManualRefunds!;
            var actual=await (manualRefunds??throw new InvalidOperationException("Manual refund projection unavailable.")).ReadDayAsync(w,ct);
            manualProof=new(pos.Manifest.ManifestId,pos.Manifest.SourceRevision!,expected.CashAmount,expected.PromptPayAmount,
                expected.CompletedCount,expected.EvidenceVersion,expected==actual);
        }
        bool manualMatched=manualProof?.Matched??true;
        // Recheck owners after the projection snapshot; the retained source identities never change.
        var orderAfter=await sources.OrderAsync(w,command.OrderManifestId,bearer,ct);
        var paymentAfter=await sources.PaymentAsync(w,command.PaymentManifestId,bearer,ct);
        var posAfter=await sources.PosAsync(w,command.PosManifestId,bearer,ct);ValidatePos(posAfter,w,command.PosManifestId);
        if(orderAfter.Manifest.ManifestId!=command.OrderManifestId || paymentAfter.Manifest.ManifestId!=command.PaymentManifestId
            || orderAfter.Manifest.Window!=w || paymentAfter.Manifest.Window!=w
            || orderAfter.Manifest.EvidenceVersion!=order.Manifest.EvidenceVersion || paymentAfter.Manifest.EvidenceVersion!=payment.Manifest.EvidenceVersion
            || orderAfter.Manifest.SourceRevision!=order.Manifest.SourceRevision || paymentAfter.Manifest.SourceRevision!=payment.Manifest.SourceRevision
            || orderAfter.Manifest.EvidenceProtocolVersion!=order.Manifest.EvidenceProtocolVersion
            || paymentAfter.Manifest.EvidenceProtocolVersion!=payment.Manifest.EvidenceProtocolVersion
            || posAfter.Manifest.SourceRevision!=pos.Manifest.SourceRevision || posAfter.Manifest.EvidenceVersion!=pos.Manifest.EvidenceVersion
            || FinancialCompleteness.Hash(posAfter.Manifest.CashCorrections)!=FinancialCompleteness.Hash(pos.Manifest.CashCorrections)
            || posAfter.Manifest.Summary.ManualRefunds!=pos.Manifest.Summary.ManualRefunds
            || posAfter.Manifest.EvidenceProtocolVersion!=pos.Manifest.EvidenceProtocolVersion)
            throw new InvalidOperationException("Cutoff source identity changed.");
        static bool RevisionBound<T>(SourceCutoff<T> value)=>value.EvidenceProtocolVersion==2
            && value.SourceRevision is {Epoch:var epoch,Revision:>=0} && epoch!=Guid.Empty;
        bool current=RevisionBound(order.Manifest)&&RevisionBound(payment.Manifest)
            && order.Current&&payment.Current&&orderAfter.Current&&paymentAfter.Current&&pos.Current&&posAfter.Current;
        static CutoffFactCounts Counts(FinancialFactCounts c)=>new(c.Expected,c.Matched,c.Missing,c.Conflicting,c.Unexpected);
        return new(check.CheckId,w,command.OrderManifestId,command.PaymentManifestId,current,
            current?(check.Status=="observed_complete"&&correctionCheck.Status=="matched"&&manualMatched?"observed_complete":"gaps_detected"):"sources_changed",
            DateTimeOffset.UtcNow,Counts(check.Sales),Counts(check.Payments),Counts(check.Refunds),
            order.Manifest.SourceRevision,payment.Manifest.SourceRevision,current&&check.Status=="observed_complete"&&correctionCheck.Status=="matched"&&manualMatched,pos.Manifest.EvidenceProtocolVersion,
            command.PosManifestId,pos.Manifest.SourceRevision,
            new(command.PosManifestId,pos.Manifest.SourceRevision!,correctionCheck.ManifestHash,
                new(correctionCheck.Expected,correctionCheck.Matched,correctionCheck.Missing,correctionCheck.Conflicting,correctionCheck.Unexpected),correctionCheck.SourceAdjustment),manualProof);
    }
    public static void ValidatePos(SourceCutoffRead<PosDaySummary> value,EndOfDayWindow w,Guid id)
    {
        var m=value.Manifest;
        if(m.ManifestId!=id || m.Window!=w || m.Summary.Window!=w || m.EvidenceProtocolVersion is not (3 or 4)
            || m.SourceRevision is not {Epoch:var epoch,Revision:>=0} || epoch==Guid.Empty || m.CashCorrections is null
            || m.CashCorrections.RestaurantId!=w.RestaurantId || m.Sales.Count!=0 || m.Refunds.Count!=0
            || m.Summary.LateCashCorrections!=m.CashCorrections.Events.Count
            || m.Summary.LateCashCorrectionAdjustment!=m.CashCorrections.Events.Sum(e=>e.CashVarianceAdjustment))
            throw new InvalidOperationException("Correction-aware POS manifest unavailable or inconsistent.");
        CashCorrectionReporting.ValidateManifest(m.CashCorrections,new(w.OrganizationId,w.BranchId,w.FromUtc,w.ToUtc));
        if(m.EvidenceProtocolVersion==4 ? m.Summary.ManualRefunds is null : m.Summary.ManualRefunds is {CompletedCount:>0} or {UnresolvedPayouts:>0})
            throw new InvalidOperationException("Fresh manual-refund-aware capture required.");
    }
}
