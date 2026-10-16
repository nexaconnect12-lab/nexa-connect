namespace NexaConnect.Services.POS.Domain.DayClose;

public sealed class BranchDaySeal
{
    private BranchDayClose preparation;
    private BranchDaySeal(BranchDayClose value)=>preparation=value;
    public static BranchDaySeal New(DayIdentity day,DateTimeOffset now)=>new(BranchDayClose.New(day,now));
    public static BranchDaySeal Restore(PreparationState state)
    {
        if(state.Status=="ready_for_review" && state.Snapshot?.Seals is null)throw new ArgumentException("Ready seal requires source seals.");
        return new(BranchDayClose.Restore(state));
    }
    public PreparationState Export()=>preparation.Export();
    public void Begin(PreparationCommand command,string actor,Guid claim,DateTimeOffset now,bool resume,DayEvidence? reviewed)
    {
        if(command.ReviewedCutoffVersion is null or <=0)throw new ArgumentException("A reviewed cutoff version is required.");
        var next=preparation;
        if(!resume)
        {
            if(reviewed is null || reviewed.Cutoff is null || reviewed.Seals is not null || !reviewed.IsValid(now) || reviewed.Blockers().Length!=0)
                throw new DayCloseConflictException("cutoff_not_ready");
            next=BranchDayClose.Restore(preparation.Export() with{Snapshot=reviewed});
        }
        next.Begin(command,actor,claim,now,resume);
        preparation=next;
    }
    public void Complete(Guid claim,DayEvidence? evidence,DateTimeOffset now)=>preparation.Complete(claim,evidence?.Seals is null?null:evidence,now);
    public bool Validate(DayEvidence? evidence,DateTimeOffset now)=>preparation.Validate(evidence?.Seals is null?null:evidence,now);
}
