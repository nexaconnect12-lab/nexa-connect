using NexaConnect.Services.POS.Domain.DayClose;
namespace NexaConnect.UnitTests;
public sealed class FinalizationPreparationTests
{
    private static readonly DateTimeOffset Now=new(2026,9,3,0,0,0,TimeSpan.Zero);
    private static FinalizationState State()
    {
        var day=new DayIdentity(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new(2026,9,1));var snapshot=DayApprovalTests.Evidence();
        var decision=new DayApprovalDecision(Guid.NewGuid(),Guid.NewGuid(),day,1,2,"manager","review_complete",Now,Now,Guid.NewGuid(),snapshot);
        return new(day,1,"preparing",new(day.BranchId,day.BusinessDate,Guid.NewGuid(),0,decision.ApprovalId,1),decision,Now.AddMinutes(4),"manager",Guid.NewGuid(),Now.AddSeconds(30),[],[],null);
    }
    private static FinalizationFence[] Fences(FinalizationState state)=>new[]{("Order",state.Approval.Snapshot.Seals!.Order),("Payment",state.Approval.Snapshot.Seals.Payment),("POS",state.Approval.Snapshot.Seals.Pos)}
        .Select(x=>new FinalizationFence(x.Item1,x.Item2.SealId,x.Item2.RevisionEpoch,x.Item2.SourceRevision,state.ExpiresAtUtc,true,false)).ToArray();
    [Fact]public void Exact_approval_and_three_active_matching_fences_prepare_without_finalization()
    {
        var state=State();var result=FinalizationPreparation.Complete(state,state.ClaimId!.Value,state.Approval,1,"approved",Fences(state),Now,false);
        Assert.Equal("prepared",result.Status);Assert.Null(result.ClaimId);Assert.Equal(Now,result.ValidatedAtUtc);Assert.Equal(state.Approval,result.Approval);
    }
    [Theory][InlineData("missing")][InlineData("duplicate")][InlineData("seal")][InlineData("epoch")][InlineData("cancelled")][InlineData("expired")][InlineData("version")][InlineData("approval")][InlineData("status")]
    public void Incomplete_stale_or_mismatched_proof_never_prepares(string kind)
    {
        var state=State();var fences=Fences(state);var decision=state.Approval;long version=1;string status="approved";
        switch(kind){case "missing":fences=fences.Take(2).ToArray();break;case "duplicate":fences[1]=fences[0];break;case "seal":fences[0]=fences[0] with{SealId=Guid.NewGuid()};break;case "epoch":fences[0]=fences[0] with{Epoch=Guid.NewGuid()};break;case "cancelled":fences[0]=fences[0] with{Cancelled=true};break;case "expired":state=state with{ExpiresAtUtc=Now.AddSeconds(14)};break;case "version":version=2;break;case "approval":decision=decision with{ApprovalId=Guid.NewGuid()};break;case "status":status="unverified";break;}
        Assert.False(FinalizationPreparation.HasLiveProof(state,decision,version,status,fences,Now));
    }
    [Fact]public void Cancel_requires_every_source_tombstone_and_stale_claim_cannot_finish()
    {
        var state=State();var fences=Fences(state).Select(x=>x with{Cancelled=true,Active=false}).ToArray();
        Assert.Equal("cancelled",FinalizationPreparation.Complete(state,state.ClaimId!.Value,null,0,"unverified",fences,Now,true).Status);
        Assert.Equal("cancelling",FinalizationPreparation.Complete(state,state.ClaimId!.Value,null,0,"unverified",fences.Take(2).ToArray(),Now,true).Status);
        Assert.Throws<DayCloseConflictException>(()=>FinalizationPreparation.Complete(state,Guid.NewGuid(),state.Approval,1,"approved",fences,Now,false));
        Assert.Throws<DayCloseConflictException>(()=>FinalizationPreparation.Complete(state,state.ClaimId!.Value,state.Approval,1,"approved",fences,Now.AddSeconds(31),false));
    }
    [Theory][InlineData("tenant-admin",true)][InlineData("store-manager",true)][InlineData("accountant",false)][InlineData("cashier",false)]
    public void Only_default_managers_receive_finalization_preparation(string role,bool allowed)=>Assert.Equal(allowed,NexaConnect.Services.Authorization.Domain.ProductRoleDefaults.PermissionsFor(role).Contains(FinalizationPreparation.PreparePermission));
}
