namespace NexaConnect.Services.POS.Domain.DayClose;

/// <summary>Cutoff coordination requires retained source references in addition to preparation invariants.</summary>
public sealed class BranchDayCutoff
{
    private readonly BranchDayClose preparation;
    private BranchDayCutoff(BranchDayClose preparation)=>this.preparation=preparation;
    public static BranchDayCutoff New(DayIdentity identity,DateTimeOffset now)=>new(BranchDayClose.New(identity,now));
    public static BranchDayCutoff Restore(PreparationState state)
    {
        if(state.Status=="ready_for_review" && state.Snapshot?.Cutoff is null)
            throw new ArgumentException("Ready cutoff history requires retained source references.");
        return new(BranchDayClose.Restore(state));
    }
    public PreparationState Export()=>preparation.Export();
    public void Begin(PreparationCommand command,string subject,Guid claim,DateTimeOffset now,bool resume)=>
        preparation.Begin(command,subject,claim,now,resume);
    public void Complete(Guid claim,DayEvidence? evidence,DateTimeOffset now)=>
        preparation.Complete(claim,evidence?.Cutoff is null?null:evidence,now);
    public bool Validate(DayEvidence? evidence,DateTimeOffset now)=>
        preparation.Validate(evidence?.Cutoff is null?null:evidence,now);
}
