using NexaConnect.Contracts.Reporting;
using NexaConnect.Contracts.Platform;

namespace NexaConnect.Services.Reporting.Application;

public interface ICutoffSources
{
    Task<SourceCutoffRead<OrderDaySummary>> OrderAsync(EndOfDayWindow window, Guid id, string bearer, CancellationToken ct);
    Task<SourceCutoffRead<PaymentDaySummary>> PaymentAsync(EndOfDayWindow window, Guid id, string bearer, CancellationToken ct);
}
public sealed class DayCutoffReconciliation(ICutoffSources sources, IReportingCustomerAuthorizer authorization,
    IFinancialCompletenessRepository repository)
{
    public async Task<CutoffReconciliation> CheckAsync(CutoffReconciliationCommand command,string bearer,CancellationToken ct)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(25));ct=deadline.Token;
        var w=command.Window;
        if(w.OrganizationId==Guid.Empty || w.RestaurantId==Guid.Empty || w.BranchId==Guid.Empty
            || w.FromUtc==default || w.ToUtc<=w.FromUtc || w.ToUtc-w.FromUtc>TimeSpan.FromHours(27) || w.ToUtc>DateTimeOffset.UtcNow
            || command.OrderManifestId==Guid.Empty || command.PaymentManifestId==Guid.Empty)throw new ArgumentException();
        if(!await authorization.IsGrantedAsync(w.OrganizationId,w.BranchId,ProductPermissions.ReportingSalesRead,bearer,ct))throw new UnauthorizedAccessException();
        var order=await sources.OrderAsync(w,command.OrderManifestId,bearer,ct);
        var payment=await sources.PaymentAsync(w,command.PaymentManifestId,bearer,ct);
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
        // Recheck owners after the projection snapshot; the retained source identities never change.
        var orderAfter=await sources.OrderAsync(w,command.OrderManifestId,bearer,ct);
        var paymentAfter=await sources.PaymentAsync(w,command.PaymentManifestId,bearer,ct);
        if(orderAfter.Manifest.ManifestId!=command.OrderManifestId || paymentAfter.Manifest.ManifestId!=command.PaymentManifestId
            || orderAfter.Manifest.Window!=w || paymentAfter.Manifest.Window!=w
            || orderAfter.Manifest.EvidenceVersion!=order.Manifest.EvidenceVersion || paymentAfter.Manifest.EvidenceVersion!=payment.Manifest.EvidenceVersion)
            throw new InvalidOperationException("Cutoff source identity changed.");
        bool current=order.Current&&payment.Current&&orderAfter.Current&&paymentAfter.Current;
        static CutoffFactCounts Counts(FinancialFactCounts c)=>new(c.Expected,c.Matched,c.Missing,c.Conflicting,c.Unexpected);
        return new(check.CheckId,w,command.OrderManifestId,command.PaymentManifestId,current,
            current?check.Status:"sources_changed",check.CheckedAtUtc,Counts(check.Sales),Counts(check.Payments),Counts(check.Refunds));
    }
}
