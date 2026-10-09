namespace NexaConnect.Services.POS.Domain.DayClose;

public sealed record ApprovalCommand(Guid BranchId,DateOnly BusinessDate,Guid OperationId,long ExpectedApprovalVersion,long ReviewedSealVersion,string ReasonCode);
public sealed record DayApprovalDecision(Guid ApprovalId,Guid OperationId,DayIdentity Identity,long ApprovalVersion,long SealVersion,
    string ApproverSubject,string ReasonCode,DateTimeOffset ApprovedAtUtc,DateTimeOffset SourceValidatedAtUtc,Guid ValidationCheckId,DayEvidence Snapshot);
public sealed record DayApprovalState(long Version,string Status,DayApprovalDecision? Decision,string Reason,DateTimeOffset UpdatedAtUtc);

/// <summary>Approval records a manager decision on evidence; source commits remain independent.</summary>
public static class BranchDayApproval
{
    public static void Validate(ApprovalCommand command,DayIdentity day,string subject)
    {
        if(command.BranchId!=day.BranchId || command.BusinessDate!=day.BusinessDate || command.OperationId==Guid.Empty
            || command.ExpectedApprovalVersion<0 || command.ReviewedSealVersion<=0 || command.ReasonCode is not ("review_complete" or "review_after_changes")
            || string.IsNullOrWhiteSpace(subject) || subject.Length>128 || subject.Any(char.IsControl))throw new ArgumentException();
    }
    public static DayApprovalState Approve(DayApprovalState state,PreparationState sealedDay,ApprovalCommand command,string subject,DayEvidence proof,DateTimeOffset now)
    {
        Validate(command,sealedDay.Identity,subject);
        if(state.Version!=command.ExpectedApprovalVersion)throw new DayCloseConflictException("approval_version_changed");
        if(state.Decision?.SealVersion==command.ReviewedSealVersion)throw new DayCloseConflictException("seal_already_reviewed");
        if(sealedDay.Version!=command.ReviewedSealVersion || sealedDay.Status!="ready_for_review" || sealedDay.Snapshot?.Seals is null
            || sealedDay.Snapshot.Currency!="THB" || !sealedDay.Snapshot.IsValid(now) || sealedDay.Snapshot.Blockers().Length!=0 || !proof.IsValid(now) || proof.Seals is null
            || now-proof.ObservedAtUtc>TimeSpan.FromMinutes(1)
            || proof.Blockers().Length!=0 || !sealedDay.Snapshot.SameEvidence(proof) || proof.Cutoff?.CheckId is null || proof.Cutoff.CheckId==Guid.Empty)
            throw new DayCloseConflictException("reviewed_seal_not_ready");
        var version=checked(state.Version+1);
        var decision=new DayApprovalDecision(Guid.NewGuid(),command.OperationId,sealedDay.Identity,version,sealedDay.Version,subject,command.ReasonCode,
            now,proof.ObservedAtUtc,proof.Cutoff.CheckId,Copy(sealedDay.Snapshot));
        return new(version,"approved",decision,"review_complete",now);
    }
    public static DayApprovalState Observe(DayApprovalState state,PreparationState? current,DayEvidence? proof,DateTimeOffset now)
    {
        if(state.Decision is null || state.Status=="superseded")return state;
        string status="unverified",reason="source_unavailable";
        var decision=state.Decision;
        if(current is null || current.Identity!=decision.Identity || current.Snapshot?.Seals is null
            || current.Snapshot.Seals.Order!=decision.Snapshot.Seals!.Order || current.Snapshot.Seals.Payment!=decision.Snapshot.Seals.Payment
            || current.Snapshot.Seals.Pos!=decision.Snapshot.Seals.Pos || current.Status=="preparing")
        {status="superseded";reason="reviewed_seal_replaced";}
        else if(current.PendingSealChanges is >0 && current.PendingSealChanges>(current.LatestSealComparison?.UnknownChanges??0)
            || proof?.Seals is {PendingChanges:>0} && proof.Seals.PendingChanges>(proof.SealComparison?.UnknownChanges??0))
        {status="superseded";reason="relevant_source_change";}
        else if(current.Version!=decision.SealVersion && current.Status=="ready_for_review")
        {status="superseded";reason="reviewed_version_changed";}
        else if(current.Version==decision.SealVersion && current.Status=="ready_for_review" && proof is not null
            && proof.IsValid(now) && now-proof.ObservedAtUtc<=TimeSpan.FromMinutes(1) && proof.Blockers().Length==0 && decision.Snapshot.SameEvidence(proof))
        {status="approved";reason="review_complete";}
        if(status==state.Status && reason==state.Reason)return state;
        return state with{Version=checked(state.Version+1),Status=status,Reason=reason,UpdatedAtUtc=now};
    }
    private static DayEvidence Copy(DayEvidence value)=>value with{Tenders=[..value.Tenders],Issues=[..value.Issues],
        SealComparison=value.SealComparison is null?null:value.SealComparison with{Tenders=[..value.SealComparison.Tenders],Changes=value.SealComparison.Changes?.ToArray()}};
}
