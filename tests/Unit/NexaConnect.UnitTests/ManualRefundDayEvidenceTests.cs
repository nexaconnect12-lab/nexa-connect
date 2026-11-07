using NexaConnect.Services.POS.Domain;
using NexaConnect.Services.POS.Domain.DayClose;

namespace NexaConnect.UnitTests;

public sealed class ManualRefundDayEvidenceTests
{
    [Fact]
    public void New_cutoff_requires_exact_manual_delivery_bound_to_its_source_revision()
    {
        var s=DayClosePreparationTests.Evidence();
        CutoffReference Ref()=>new(Guid.NewGuid(),1,new('a',64),Guid.NewGuid(),0);
        s=s with{Cutoff=new(Ref(),Ref(),Ref(),Guid.NewGuid(),true,0,true,4)};
        Assert.Contains("manual_refund_evidence_unavailable",s.Blockers());
        var p=s.Cutoff.Pos;
        var proof=new DayManualRefundEvidence(p.ManifestId,p.RevisionEpoch!.Value,p.SourceRevision!.Value,20,30,2,new('b',64),false);
        s=s with{ManualRefunds=proof};Assert.True(s.IsValid(s.ObservedAtUtc));
        Assert.Contains("manual_refund_delivery_unproven",s.Blockers());
        Assert.False((s with{ManualRefunds=proof with{SourceRevision=1}}).IsValid(s.ObservedAtUtc));
        Assert.False(s.SameEvidence(s with{ManualRefunds=proof with{Matched=true}}));
        Assert.False((proof with{CompletedCount=0}).IsValid);
    }
    [Fact]
    public void Refund_completion_affects_its_own_day_and_unresolved_custody_carries_forward()
    {
        var from=DateTimeOffset.UtcNow.Date.AddDays(-2);var end=from.AddDays(1);
        var id=Guid.NewGuid();var later=new DateTimeOffset(end.AddHours(1),TimeSpan.Zero);
        var started=new FinancialRecord(id,"started",1,later);
        var completed=started with{Status="completed",Version=2,FinancialAtUtc=later.AddMinutes(1)};
        Assert.False(FinancialChange.Classify(from,end,started,completed,false,"manual_refunds").AffectsWindow);
        Assert.True(FinancialChange.Classify(end,end.AddDays(1),null,completed,false,"manual_refunds").AffectsWindow);
        Assert.True(FinancialChange.Classify(end.AddDays(1),end.AddDays(2),null,started,false,"manual_refunds").AffectsWindow);
        Assert.Null(FinancialChange.Classify(from,end,null,completed with{FinancialAtUtc=null},false,"manual_refunds").AffectsWindow);
    }
}
