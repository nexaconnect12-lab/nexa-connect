namespace NexaConnect.Services.POS.Domain.DayClose;

public sealed record FinalizationCommand(Guid BranchId,DateOnly BusinessDate,Guid OperationId,long ExpectedVersion,
    Guid ApprovalId,long ReviewedApprovalVersion);
public sealed record FinalizationCancel(Guid BranchId,DateOnly BusinessDate,Guid OperationId);
public sealed record FinalizationFence(string Source,Guid SealId,Guid Epoch,long Revision,DateTimeOffset ExpiresAtUtc,bool Active,bool Cancelled);
public sealed record FinalizationState(DayIdentity Identity,long Version,string Status,FinalizationCommand Command,
    DayApprovalDecision Approval,DateTimeOffset ExpiresAtUtc,string Subject,Guid? ClaimId,DateTimeOffset? ClaimUntilUtc,
    FinalizationFence[] Sources,string[] Blockers,DateTimeOffset? ValidatedAtUtc);

public static class FinalizationPreparation
{
    public const string PreparePermission="pos.day-close.finalization.prepare";
    public static void Validate(FinalizationCommand command,DayIdentity day)
    {
        if(command.BranchId!=day.BranchId||command.BusinessDate!=day.BusinessDate||command.OperationId==Guid.Empty
            ||command.ExpectedVersion<0||command.ApprovalId==Guid.Empty||command.ReviewedApprovalVersion<=0)throw new ArgumentException();
    }
    public static bool HasLiveProof(FinalizationState state,DayApprovalDecision? decision,long approvalVersion,string approvalStatus,
        FinalizationFence[] sources,DateTimeOffset now)
    {
        if(state.ExpiresAtUtc<=now.AddSeconds(15)||decision?.Identity!=state.Identity||decision.ApprovalId!=state.Approval.ApprovalId
            ||decision.SealVersion!=state.Approval.SealVersion||approvalVersion!=state.Command.ReviewedApprovalVersion||approvalStatus!="approved"
            ||sources.Length!=3||sources.Select(x=>x.Source).Distinct().Count()!=3)return false;
        var seals=state.Approval.Snapshot.Seals!;
        foreach(var owner in new[]{"Order","Payment","POS"})
        {
            var expected=owner=="Order"?seals.Order:owner=="Payment"?seals.Payment:seals.Pos;
            var source=sources.SingleOrDefault(x=>x.Source==owner);
            if(source is null||!source.Active||source.Cancelled||source.SealId!=expected.SealId||source.Epoch!=expected.RevisionEpoch
                ||source.Revision<expected.SourceRevision||source.ExpiresAtUtc!=state.ExpiresAtUtc)return false;
        }
        return true;
    }
    public static FinalizationState Complete(FinalizationState state,Guid claim,DayApprovalDecision? decision,long approvalVersion,
        string approvalStatus,FinalizationFence[] sources,DateTimeOffset now,bool cancel)
    {
        if(state.ClaimId!=claim||state.ClaimUntilUtc is not {} until||until<=now)throw new DayCloseConflictException("finalization_claim_changed");
        bool ready=cancel?HasCancellationProof(state,sources):HasLiveProof(state,decision,approvalVersion,approvalStatus,sources,now);
        return state with{Version=checked(state.Version+1),Status=cancel?(ready?"cancelled":"cancelling"):(ready?"prepared":"blocked"),
            ClaimId=null,ClaimUntilUtc=null,Sources=[..sources],Blockers=ready?[]:[cancel?"cancellation_incomplete":"finalization_proof_unavailable"],ValidatedAtUtc=ready&&!cancel?now:null};
    }
    private static bool HasCancellationProof(FinalizationState state,FinalizationFence[] sources)
    {
        if(sources.Length!=3||sources.Select(x=>x.Source).Distinct().Count()!=3)return false;
        var seals=state.Approval.Snapshot.Seals!;
        foreach(var owner in new[]{"Order","Payment","POS"})
        {
            var expected=owner=="Order"?seals.Order:owner=="Payment"?seals.Payment:seals.Pos;
            var source=sources.SingleOrDefault(x=>x.Source==owner);
            if(source is null||!source.Cancelled||source.Active||source.SealId!=expected.SealId||source.ExpiresAtUtc!=state.ExpiresAtUtc)return false;
        }
        return true;
    }
}
