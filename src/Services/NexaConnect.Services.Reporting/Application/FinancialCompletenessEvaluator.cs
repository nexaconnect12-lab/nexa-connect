using NexaConnect.Services.Reporting.Domain;

namespace NexaConnect.Services.Reporting.Application;

public sealed record ProjectedSale(Guid OrderId,Guid EventId,Guid OrganizationId,Guid RestaurantId,Guid BranchId,
    string Channel,string ServiceType,string Currency,decimal Subtotal,decimal Discount,decimal ServiceCharge,
    decimal Tax,decimal Total,string Status,DateTimeOffset OrderedAt,DateTimeOffset? CompletedAt,long Version);
public sealed record ProjectedPayment(Guid PaymentId,Guid EventId,Guid OrganizationId,Guid RestaurantId,Guid BranchId,
    Guid OrderId,string Method,string Origin,string? Provider,string Currency,decimal Paid,decimal Refunded,
    string Status,DateTimeOffset? PaidAt,long Version);
public sealed record FinancialProjectionSnapshot(IReadOnlyList<ProjectedSale> Sales,IReadOnlyList<ProjectedPayment> Payments,
    IReadOnlyList<RefundFinancialFact> Refunds,IReadOnlyDictionary<Guid,string> SaleHashes,IReadOnlyDictionary<Guid,string> RefundHashes);

public static class FinancialCompletenessEvaluator
{
    public static FinancialCompletenessObservation Evaluate(FinancialCompletenessSource source,FinancialProjectionSnapshot actual)
    {
        source.Validate();
        var sales=actual.Sales.ToDictionary(s=>s.OrderId); var payments=actual.Payments.ToDictionary(p=>p.PaymentId);
        var refunds=actual.Refunds.ToDictionary(r=>r.RefundId);
        int saleMatched=0,saleMissing=0,saleConflict=0,paymentMatched=0,paymentMissing=0,paymentConflict=0;
        int refundMatched=0,refundMissing=0,refundConflict=0;
        foreach(var value in source.Sales)
        {
            var expected=value.Canonicalize();
            bool hash=actual.SaleHashes.TryGetValue(expected.SourceEventId,out var storedHash) && storedHash==FinancialCompleteness.Hash(expected);
            if(!sales.TryGetValue(expected.OrderId,out var sale)) saleMissing++;
            else if(hash && SaleMatches(sale,expected)) saleMatched++; else saleConflict++;
            if(!payments.TryGetValue(expected.PaymentId,out var payment)) paymentMissing++;
            else if(hash && PaymentMatches(payment,expected)) paymentMatched++; else paymentConflict++;
        }
        foreach(var expected in source.Refunds)
        {
            if(!refunds.TryGetValue(expected.RefundId,out var refund)) refundMissing++;
            else if(actual.RefundHashes.TryGetValue(expected.SourceEventId,out var hash) && hash==FinancialCompleteness.Hash(expected)
                && RefundMatches(refund,expected)) refundMatched++; else refundConflict++;
        }
        var expectedSales=source.Sales.Select(s=>s.OrderId).ToHashSet(); var expectedPayments=source.Sales.Select(s=>s.PaymentId).ToHashSet();
        var expectedRefunds=source.Refunds.Select(r=>r.RefundId).ToHashSet();
        bool Scope(Guid org,Guid branch,DateTimeOffset? time)=>org==source.Range.OrganizationId && branch==source.Range.BranchId
            && time>=source.Range.FromUtc && time<source.Range.ToUtc;
        var saleCounts=new FinancialFactCounts(source.Sales.Count,saleMatched,saleMissing,saleConflict,
            actual.Sales.Count(s=>Scope(s.OrganizationId,s.BranchId,s.OrderedAt) && !expectedSales.Contains(s.OrderId)));
        var paymentCounts=new FinancialFactCounts(source.Sales.Count,paymentMatched,paymentMissing,paymentConflict,
            actual.Payments.Count(p=>Scope(p.OrganizationId,p.BranchId,p.PaidAt) && !expectedPayments.Contains(p.PaymentId)));
        var refundCounts=new FinancialFactCounts(source.Refunds.Count,refundMatched,refundMissing,refundConflict,
            actual.Refunds.Count(r=>Scope(r.OrganizationId,r.BranchId,r.RefundedAtUtc) && !expectedRefunds.Contains(r.RefundId)));
        bool complete=saleCounts.Gaps+paymentCounts.Gaps+refundCounts.Gaps+source.SaleEvidenceGaps+source.RefundEvidenceGaps
            +source.UnretainedSales+source.UnretainedRefunds==0;
        string manifest=FinancialCompleteness.Hash(new { source.Range,source.SaleCandidates,source.RefundCandidates,
            source.SaleEvidenceGaps,source.RefundEvidenceGaps,source.UnretainedSales,source.UnretainedRefunds,
            Sales=source.Sales.Select(s=>s.Canonicalize()).OrderBy(s=>s.OrderId),Refunds=source.Refunds.OrderBy(r=>r.RefundId) });
        return new(Guid.NewGuid(),source.Range,complete?"observed_complete":"gaps_detected",DateTimeOffset.UtcNow,
            source.OrderObservedAtUtc,source.RefundObservedAtUtc,manifest,source.SaleEvidenceGaps,source.RefundEvidenceGaps,
            source.UnretainedSales,source.UnretainedRefunds,saleCounts,paymentCounts,refundCounts);
    }
    private static bool SaleMatches(ProjectedSale s,SaleFinancialFact e)=>s.EventId==e.SourceEventId && s.OrganizationId==e.OrganizationId
        && s.RestaurantId==e.RestaurantId && s.BranchId==e.BranchId && s.Channel==e.Channel && s.ServiceType==e.ServiceType
        && s.Currency==e.Currency && s.Subtotal==e.SubtotalAmount && s.Discount==0 && s.ServiceCharge==e.ServiceChargeAmount
        && s.Tax==e.TaxAmount && s.Total==e.TotalAmount && s.Status=="completed" && s.Version==1
        && FinancialCompleteness.SameInstant(s.OrderedAt,e.OrderedAtUtc) && s.CompletedAt is {} completed
        && FinancialCompleteness.SameInstant(completed,e.PaidAtUtc);
    private static bool PaymentMatches(ProjectedPayment p,SaleFinancialFact e)=>p.EventId==e.SourceEventId && p.OrganizationId==e.OrganizationId
        && p.RestaurantId==e.RestaurantId && p.BranchId==e.BranchId && p.OrderId==e.OrderId && p.Method==e.Method && p.Origin==e.PaymentOrigin
        && p.Provider is null && p.Currency==e.Currency && p.Paid==e.TotalAmount && p.Refunded==0 && p.Status=="paid" && p.Version==1
        && p.PaidAt is {} paid && FinancialCompleteness.SameInstant(paid,e.PaidAtUtc);
    private static bool RefundMatches(RefundFinancialFact a,RefundFinancialFact e)=>a.SourceEventId==e.SourceEventId
        && a.OrganizationId==e.OrganizationId && a.RestaurantId==e.RestaurantId && a.BranchId==e.BranchId
        && a.OrderId==e.OrderId && a.PaymentIntentId==e.PaymentIntentId && a.Amount==e.Amount && a.Currency==e.Currency
        && a.ReasonCode==e.ReasonCode && FinancialCompleteness.SameInstant(a.RefundedAtUtc,e.RefundedAtUtc)
        && a.CumulativeRefundedAmount==e.CumulativeRefundedAmount && a.CapturedAmount==e.CapturedAmount && a.ReceiptNumber==e.ReceiptNumber;
}
