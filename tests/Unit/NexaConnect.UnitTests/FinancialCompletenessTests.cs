using NexaConnect.Services.Reporting.Application;
using NexaConnect.Services.Reporting.Domain;

namespace NexaConnect.UnitTests;

public sealed class FinancialCompletenessTests
{
    [Fact]
    public void Missing_conflicting_unexpected_and_cross_scope_evidence_are_explicit_gaps()
    {
        var (source,snapshot)=Fixture();
        Assert.Equal("observed_complete",FinancialCompletenessEvaluator.Evaluate(source,snapshot).Status);
        var missing=FinancialCompletenessEvaluator.Evaluate(source,snapshot with { Payments=[] });
        Assert.Equal(1,missing.Payments.Missing); Assert.Equal("gaps_detected",missing.Status);
        var conflict=FinancialCompletenessEvaluator.Evaluate(source,snapshot with { Payments=[snapshot.Payments[0] with { Paid=1 }] });
        Assert.Equal(1,conflict.Payments.Conflicting);
        var foreign=FinancialCompletenessEvaluator.Evaluate(source,snapshot with { Sales=[snapshot.Sales[0] with { OrganizationId=Guid.NewGuid() }] });
        Assert.Equal(1,foreign.Sales.Conflicting);
        var extra=FinancialCompletenessEvaluator.Evaluate(source,snapshot with { Sales=[..snapshot.Sales,snapshot.Sales[0] with { OrderId=Guid.NewGuid() }] });
        Assert.Equal(1,extra.Sales.Unexpected);
        Assert.Equal("gaps_detected",FinancialCompletenessEvaluator.Evaluate(source with { SaleCandidates=2,SaleEvidenceGaps=1 },snapshot).Status);
        Assert.Equal("gaps_detected",FinancialCompletenessEvaluator.Evaluate(source with { UnretainedRefunds=1 },snapshot).Status);
    }
    [Fact]
    public void Period_scope_validation_and_manifest_stability_do_not_use_freshness_as_completeness()
    {
        var (source,snapshot)=Fixture();
        Assert.Throws<ArgumentException>(()=>FinancialCompletenessEvaluator.Evaluate(source with { Range=source.Range with { BranchId=Guid.NewGuid() } },snapshot));
        Assert.Throws<ArgumentException>(()=>FinancialCompleteness.ValidateRange(source.Range with { ToUtc=DateTimeOffset.UtcNow.AddHours(1) }));
        Assert.Throws<ArgumentException>(()=>FinancialCompletenessEvaluator.Evaluate(source with { Sales=[..source.Sales,source.Sales[0]],SaleCandidates=2 },snapshot));
        var first=FinancialCompletenessEvaluator.Evaluate(source,snapshot);
        var next=FinancialCompletenessEvaluator.Evaluate(source with { OrderObservedAtUtc=DateTimeOffset.UtcNow },snapshot);
        Assert.Equal(first.ManifestHash,next.ManifestHash); Assert.NotEqual(first.CheckId,next.CheckId);
        Assert.Equal("gaps_detected",FinancialCompletenessEvaluator.Evaluate(source,snapshot with { RefundHashes=new Dictionary<Guid,string>() }).Status);
    }
    private static (FinancialCompletenessSource,FinancialProjectionSnapshot) Fixture()
    {
        var now=DateTimeOffset.UtcNow.AddHours(-1); Guid org=Guid.NewGuid(),restaurant=Guid.NewGuid(),branch=Guid.NewGuid(),order=Guid.NewGuid(),payment=Guid.NewGuid(),evt=Guid.NewGuid();
        var sale=new SaleFinancialFact(evt,org,restaurant,branch,order,payment,"payment_intent","card","THB","pos","takeaway",now.AddMinutes(-1),now,"R-TEST",100,0,0,100).Canonicalize();
        var refund=new RefundFinancialFact(Guid.NewGuid(),org,restaurant,branch,order,payment,Guid.NewGuid(),25,"THB","customer_request",now.AddMinutes(1),25,100,"RF-TEST");
        var source=new FinancialCompletenessSource(new(org,branch,now.AddHours(-1),now.AddHours(.5)),DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,1,1,0,0,0,0,[sale],[refund]);
        var snapshot=new FinancialProjectionSnapshot([new(order,evt,org,restaurant,branch,"pos","takeaway","THB",100,0,0,0,100,"completed",sale.OrderedAtUtc,sale.PaidAtUtc,1)],
            [new(payment,evt,org,restaurant,branch,order,"card","payment_intent",null,"THB",100,0,"paid",sale.PaidAtUtc,1)],[refund],
            new Dictionary<Guid,string>{{evt,FinancialCompleteness.Hash(sale)}},new Dictionary<Guid,string>{{refund.SourceEventId,FinancialCompleteness.Hash(refund)}});
        return (source,snapshot);
    }
}
