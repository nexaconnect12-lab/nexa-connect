using NexaConnect.Services.POS.Domain.DayClose;
namespace NexaConnect.UnitTests;

public sealed class DayCorrectionEvidenceTests
{
    private static readonly DateTimeOffset Now=new(2026,9,3,0,0,0,TimeSpan.Zero);
    private static DayEvidence Evidence()
    {
        var e=DayApprovalTests.Evidence();var p=e.Cutoff!.Pos;
        var item=new DayCorrectionItem(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new(2026,9,1),e.FromUtc.AddHours(1),-10.0001m);
        return e with{Corrections=new(p.ManifestId,p.RevisionEpoch!.Value,p.SourceRevision!.Value,new('a',64),item.Adjustment,1,1,0,0,0,[item])};
    }
    [Theory][InlineData("missing")][InlineData("conflicting")][InlineData("unexpected")]
    public void Even_claimed_complete_aggregate_flags_cannot_bypass_correction_delivery_gaps(string kind)
    {
        var e=Evidence();var c=e.Corrections!;
        c=kind switch{"missing"=>c with{Matched=0,Missing=1},"conflicting"=>c with{Matched=0,Conflicting=1},_=>c with{Unexpected=1}};
        Assert.True((e with{Corrections=c}).IsValid(Now));Assert.Contains("correction_delivery_unproven",(e with{Corrections=c}).Blockers());
        var identity=new DayIdentity(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new(2026,9,1));var aggregate=BranchDayCutoff.New(identity,Now);var claim=Guid.NewGuid();
        aggregate.Begin(new(identity.BranchId,identity.BusinessDate,Guid.NewGuid(),0,"routine_close"),"manager",claim,Now,false);
        aggregate.Complete(claim,e with{Corrections=c},Now);Assert.Equal("blocked",aggregate.Export().Status);Assert.NotNull(aggregate.Export().Snapshot);
    }
    [Fact]
    public void Legacy_empty_inventory_is_readable_but_cannot_admit_new_seal_or_settlement()
    {
        var e=DayApprovalTests.Evidence();var legacy=e with{Corrections=null,Cutoff=e.Cutoff! with{EvidenceProtocolVersion=2}};
        Assert.True(legacy.IsValid(Now));Assert.Contains("correction_evidence_unavailable",legacy.Blockers());
        var day=new DayIdentity(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new(2026,9,1));
        Assert.Throws<DayCloseConflictException>(()=>BranchDaySeal.New(day,Now).Begin(new(day.BranchId,day.BusinessDate,Guid.NewGuid(),0,"routine_close",2),"manager",Guid.NewGuid(),Now,false,legacy));
    }
    [Theory][InlineData("manifest")][InlineData("epoch")][InlineData("amount")][InlineData("window")][InlineData("duplicate")]
    public void Invalid_provenance_amount_window_and_duplicate_identity_fail_validation(string kind)
    {
        var e=Evidence();var c=e.Corrections!;
        c=kind switch{"manifest"=>c with{ManifestId=Guid.NewGuid()},"epoch"=>c with{RevisionEpoch=Guid.NewGuid()},"amount"=>c with{Adjustment=-11},
            "window"=>c with{Items=[c.Items[0] with{PostedAtUtc=e.ToUtc}]},_=>c with{Expected=2,Matched=2,Adjustment=2*c.Adjustment,Items=[c.Items[0],c.Items[0]]}};
        Assert.False((e with{Corrections=c}).IsValid(Now));
    }
    [Fact]
    public void Equal_totals_cannot_hide_changed_original_identity_and_export_copies_correction_items()
    {
        var e=Evidence();Assert.Empty(e.Blockers());Assert.Equal("-10.0001",e.Corrections!.AdjustmentText);
        var changed=e with{Corrections=e.Corrections with{Items=[e.Corrections.Items[0] with{EventId=Guid.NewGuid()}]}};
        Assert.False(e.SameEvidence(changed));
        var day=new DayIdentity(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new(2026,9,1));var aggregate=BranchDayCutoff.New(day,Now);var claim=Guid.NewGuid();
        aggregate.Begin(new(day.BranchId,day.BusinessDate,Guid.NewGuid(),0,"routine_close"),"manager",claim,Now,false);aggregate.Complete(claim,e,Now);
        e.Corrections.Items[0]=e.Corrections.Items[0] with{Adjustment=-999};Assert.Equal(-10.0001m,aggregate.Export().Snapshot!.Corrections!.Items[0].Adjustment);
        var exported=aggregate.Export();exported.Snapshot!.Corrections!.Items[0]=exported.Snapshot.Corrections.Items[0] with{Adjustment=-100};
        Assert.Equal(-10.0001m,aggregate.Export().Snapshot!.Corrections!.Items[0].Adjustment);
    }
}
