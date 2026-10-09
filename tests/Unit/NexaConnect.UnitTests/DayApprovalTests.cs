using NexaConnect.Services.POS.Domain.DayClose;
namespace NexaConnect.UnitTests;

public sealed class DayApprovalTests
{
    private static readonly DateTimeOffset Now=new(2026,9,3,0,0,0,TimeSpan.Zero);
    private static readonly DayIdentity Day=new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new(2026,9,1));
    private static ApprovalCommand Command(long approvalVersion=0,long sealVersion=2)=>new(Day.BranchId,Day.BusinessDate,Guid.NewGuid(),approvalVersion,sealVersion,"review_complete");
    public static DayEvidence Evidence()
    {
        static CutoffReference Ref()=>new(Guid.NewGuid(),1,new('a',64),Guid.NewGuid(),0);
        static DaySealReference Seal(CutoffReference r)=>new(Guid.NewGuid(),r.ManifestId,r.RevisionEpoch!.Value,r.SourceRevision!.Value);
        var cutoff=new DayCutoffEvidence(Ref(),Ref(),Ref(),Guid.NewGuid(),true,0,true,2);
        return DayClosePreparationTests.Evidence() with{Cutoff=cutoff,Seals=new(Seal(cutoff.Order),Seal(cutoff.Payment),Seal(cutoff.Pos),0,true,true)};
    }
    private static PreparationState Ready(DayEvidence e)=>new(Day,2,"ready_for_review",e,[],Now,PendingSealChanges:0);
    private static DayApprovalState Initial()=>new(0,"not_approved",null,"not_approved",Now);
    [Fact]
    public void Approval_binds_snapshot_versions_actor_and_latest_delivery_check_without_mutating_sources()
    {
        var e=Evidence();var proof=e with{Cutoff=e.Cutoff! with{CheckId=Guid.NewGuid()}};var state=BranchDayApproval.Approve(Initial(),Ready(e),Command(),"manager",proof,Now);
        Assert.Equal("approved",state.Status);Assert.Equal(1,state.Version);Assert.Equal(2,state.Decision!.SealVersion);
        Assert.Equal(proof.Cutoff!.CheckId,state.Decision.ValidationCheckId);Assert.Equal(e.Cutoff!.CheckId,state.Decision.Snapshot.Cutoff!.CheckId);
        e.Tenders[0]=new("cash","THB",999);Assert.Equal(100,state.Decision.Snapshot.Tenders[0].Amount);
        Assert.Equal("manager",state.Decision.ApproverSubject);
    }
    [Theory][InlineData("pending")][InlineData("journal")][InlineData("delivery")][InlineData("stale_proof")][InlineData("wrong_version")][InlineData("wrong_snapshot")]
    public void Incomplete_stale_or_changed_evidence_cannot_be_approved(string kind)
    {
        var e=Evidence();var proof=kind switch{
            "pending"=>e with{Seals=e.Seals! with{PendingChanges=1}},"journal"=>e with{Seals=e.Seals! with{JournalComplete=false}},
            "delivery"=>e with{Seals=e.Seals! with{DeliveryComplete=false}},"stale_proof"=>e with{ObservedAtUtc=Now.AddMinutes(-2)},
            "wrong_snapshot"=>e with{GrossSales=101,NetSales=76},_=>e};
        Assert.Throws<DayCloseConflictException>(()=>BranchDayApproval.Approve(Initial(),Ready(e),Command(sealVersion:kind=="wrong_version"?3:2),"manager",proof,Now));
    }
    [Fact]
    public void Relevant_changes_supersede_once_and_restore_of_values_never_restores_the_decision()
    {
        var e=Evidence();var approved=BranchDayApproval.Approve(Initial(),Ready(e),Command(),"manager",e,Now);
        var changed=e with{Seals=e.Seals! with{PendingChanges=1}};
        var superseded=BranchDayApproval.Observe(approved,Ready(e),changed,Now.AddSeconds(1));Assert.Equal("superseded",superseded.Status);
        Assert.Same(approved.Decision,superseded.Decision);Assert.Equal(superseded,BranchDayApproval.Observe(superseded,Ready(e),e,Now.AddSeconds(2)));
    }
    [Fact]
    public void Dependency_failure_is_unverified_and_the_same_unchanged_binding_can_revalidate()
    {
        var e=Evidence();var approved=BranchDayApproval.Approve(Initial(),Ready(e),Command(),"manager",e,Now);
        var unavailable=BranchDayApproval.Observe(approved,Ready(e),null,Now.AddSeconds(1));Assert.Equal("unverified",unavailable.Status);
        var restored=BranchDayApproval.Observe(unavailable,Ready(e),e with{ObservedAtUtc=Now.AddSeconds(2)},Now.AddSeconds(2));
        Assert.Equal("approved",restored.Status);Assert.Equal(approved.Decision,restored.Decision);Assert.Equal(3,restored.Version);
    }
    [Fact]
    public void Replaced_or_preparing_seals_supersede_and_stale_approval_versions_conflict()
    {
        var e=Evidence();var approved=BranchDayApproval.Approve(Initial(),Ready(e),Command(),"manager",e,Now);
        Assert.Equal("superseded",BranchDayApproval.Observe(approved,Ready(e) with{Status="preparing",Version=3},null,Now).Status);
        Assert.Throws<DayCloseConflictException>(()=>BranchDayApproval.Approve(approved,Ready(e),Command(),"other",e,Now));
        Assert.Throws<ArgumentException>(()=>BranchDayApproval.Validate(Command() with{ReasonCode="arbitrary"},Day,"manager"));
    }
    [Theory][InlineData("tenant-admin",true)][InlineData("store-manager",true)][InlineData("accountant",false)][InlineData("cashier",false)]
    public void Default_approval_authority_is_restricted_to_manager_roles(string role,bool granted)=>
        Assert.Equal(granted,NexaConnect.Services.Authorization.Domain.ProductRoleDefaults.PermissionsFor(role).Contains("pos.day-close.approve"));
}
