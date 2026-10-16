using NexaConnect.Services.POS.Domain.DayClose;
namespace NexaConnect.UnitTests;

public sealed class BranchDaySettlementTests
{
    [Theory][InlineData("tenant-admin",true)][InlineData("store-manager",true)][InlineData("accountant",false)][InlineData("cashier",false)]
    public void New_role_assignments_match_finalize_backfill_and_preserve_read_only_roles(string role,bool granted)=>
        Assert.Equal(granted,NexaConnect.Services.Authorization.Domain.ProductRoleDefaults.PermissionsFor(role).Contains(BranchDaySettlement.FinalizePermission));
    private static readonly DateTimeOffset Now=new(2026,9,3,0,0,0,TimeSpan.Zero);
    private static FinalizationState Prepared()
    {
        var day=new DayIdentity(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new(2026,9,1));var snapshot=DayApprovalTests.Evidence();
        var approval=new DayApprovalDecision(Guid.NewGuid(),Guid.NewGuid(),day,1,2,"manager","review_complete",Now,Now,Guid.NewGuid(),snapshot);
        var expires=Now.AddMinutes(4);
        var sources=new[]{("Order",snapshot.Seals!.Order),("Payment",snapshot.Seals.Payment),("POS",snapshot.Seals.Pos)}
            .Select(x=>new FinalizationFence(x.Item1,x.Item2.SealId,x.Item2.RevisionEpoch,x.Item2.SourceRevision,expires,true,false)).ToArray();
        return new(day,2,"prepared",new(day.BranchId,day.BusinessDate,Guid.NewGuid(),0,approval.ApprovalId,1),approval,expires,"manager",null,null,sources,[],Now);
    }
    private static SettlementCommand Command(FinalizationState state)=>new(state.Identity.BranchId,state.Identity.BusinessDate,Guid.NewGuid(),state.Version,state.Command.OperationId,state.Approval.ApprovalId,1);
    [Fact]public void Three_exact_source_acknowledgements_are_required_for_commit_and_final_delivery()
    {
        var state=Prepared();var armed=state.Sources.Select(s=>new SettlementAcknowledgement(s.Source,s.SealId,"armed",null)).ToArray();
        BranchDaySettlement.ValidateAcknowledgements(state,armed,"armed",null);
        foreach(var proof in new[]{armed[..2],[armed[0],armed[0],armed[2]],armed.Select(x=>x with{SealId=Guid.NewGuid()}).ToArray()})
            Assert.Throws<DayCloseConflictException>(()=>BranchDaySettlement.ValidateAcknowledgements(state,proof,"armed",null));
        var decision=Guid.NewGuid();var committed=armed.Select(x=>x with{Phase="committed",DecisionId=decision}).ToArray();
        BranchDaySettlement.ValidateAcknowledgements(state,committed,"committed",decision);
        Assert.Throws<DayCloseConflictException>(()=>BranchDaySettlement.ValidateAcknowledgements(state,committed,"committed",Guid.NewGuid()));
        Assert.Throws<DayCloseConflictException>(()=>BranchDaySettlement.ValidateAcknowledgements(state,committed,"armed",null));
    }
    [Fact] public void Exact_live_review_admits_and_commit_copies_original_financial_snapshot()
    {
        var state=Prepared();var command=Command(state);
        BranchDaySettlement.ValidateAdmission(command,state,state.Approval,1,"approved",Now);
        var receipt=BranchDaySettlement.Commit(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),state,Now);
        Assert.Equal(state.Identity,receipt.Identity);Assert.Equal(state.Approval.Snapshot.GrossSales,receipt.Snapshot.GrossSales);
        Assert.Equal(state.Approval.ApprovalId,receipt.ApprovalId);Assert.Equal(state.Approval.SealVersion,receipt.SealVersion);
        Assert.NotSame(state.Approval.Snapshot.Tenders,receipt.Snapshot.Tenders);Assert.NotSame(state.Sources,receipt.Sources);
    }
    [Fact] public void Missing_expired_reversed_or_different_source_proof_cannot_admit_a_new_intent()
    {
        var state=Prepared();var command=Command(state);
        foreach(var changed in new[]{state with{Status="blocked"},state with{Sources=state.Sources[..2]},state with{ExpiresAtUtc=Now.AddSeconds(15)},
            state with{Sources=state.Sources.Select(x=>x with{Cancelled=true,Active=false}).ToArray()},
            state with{Sources=state.Sources.Select(x=>x with{Epoch=Guid.NewGuid()}).ToArray()}})
            Assert.Throws<DayCloseConflictException>(()=>BranchDaySettlement.ValidateAdmission(command,changed,state.Approval,1,"approved",Now));
        Assert.Throws<DayCloseConflictException>(()=>BranchDaySettlement.ValidateAdmission(command,state,state.Approval,2,"approved",Now));
        Assert.Throws<DayCloseConflictException>(()=>BranchDaySettlement.ValidateAdmission(command,state,state.Approval,1,"superseded",Now));
        Assert.Throws<DayCloseConflictException>(()=>BranchDaySettlement.ValidateAdmission(command,state,state.Approval with{Snapshot=state.Approval.Snapshot with{NetSales=999}},1,"approved",Now));
    }
    [Fact] public void Every_reviewed_binding_must_match_the_exact_preparation()
    {
        var state=Prepared();var command=Command(state);
        foreach(var changed in new[]{command with{ExpectedPreparationVersion=3},command with{PreparationOperationId=Guid.NewGuid()},
            command with{ApprovalId=Guid.NewGuid()},command with{ReviewedApprovalVersion=2},command with{BranchId=Guid.NewGuid()},command with{BusinessDate=state.Identity.BusinessDate.AddDays(1)}})
            Assert.Throws<DayCloseConflictException>(()=>BranchDaySettlement.ValidateAdmission(changed,state,state.Approval,1,"approved",Now));
    }
}
