using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Domain.DayClose;
namespace NexaConnect.Services.POS.Application.DayClose;

public sealed record FinalizationView(DayIdentity Identity,long Version,string Status,FinalizationCommand? PendingCommand,Guid? ApprovalId,
    long? ReviewedApprovalVersion,long? SealVersion,DateTimeOffset? ExpiresAtUtc,DateTimeOffset? ValidatedAtUtc,
    FinalizationFence[] Sources,string[] Blockers,bool CanPrepare);
public interface IFinalizationStore
{
    Task<string?> SettlementStatusAsync(DayIdentity day,CancellationToken ct)=>Task.FromResult<string?>(null);
    Task<FinalizationState?> ReadAsync(DayIdentity day,CancellationToken ct);
    Task<FinalizationState> BeginAsync(DayIdentity day,FinalizationCommand command,ApprovalView approval,PreparationActor actor,DateTimeOffset now,CancellationToken ct);
    Task<FinalizationState> CancelAsync(DayIdentity day,Guid operation,PreparationActor actor,DateTimeOffset now,CancellationToken ct);
    Task<FinalizationState> CompleteAsync(FinalizationState claimed,ApprovalView? approval,FinalizationFence[] sources,PreparationActor actor,DateTimeOffset now,bool cancel,CancellationToken ct);
    Task<FinalizationState> ObserveAsync(FinalizationState observed,ApprovalView? approval,FinalizationFence[] sources,PreparationActor actor,DateTimeOffset now,CancellationToken ct);
}
public interface IFinalizationSources
{Task<FinalizationFence[]> ExecuteAsync(FinalizationState state,PosUserContext user,bool? cancel,CancellationToken ct);}

public sealed class DayFinalizationPreparation(IFinalizationStore store,IFinalizationSources sources,DayCloseApproval approvals,
    IRestaurantScopeReader scopes,IAuthorizationDecisionClient authorization,TimeProvider clock,ILogger<DayFinalizationPreparation> logger)
{
    public async Task<FinalizationView> ReadAsync(Guid organization,Guid branch,DateOnly date,PosUserContext user,CancellationToken ct)
    {
        using var deadline=Deadline(ct);ct=deadline.Token;
        var(day,actor,scope)=await Authorize(organization,branch,date,user,DayClosePreparation.ReadPermission,ct);
        var permission=await authorization.DecideAsync(user,scope,FinalizationPreparation.PreparePermission,ct);
        var state=await store.ReadAsync(day,ct);if(state is null)return new(day,0,"not_prepared",null,null,null,null,null,null,[],[],permission.Granted&&permission.DecisionId!=Guid.Empty);
        if(await store.SettlementStatusAsync(day,ct) is {} settlementStatus)
            return View(state with{Status=settlementStatus=="finalized"?"finalized":"finalizing",ValidatedAtUtc=null},false);
        ApprovalView? approval=null;FinalizationFence[] proof=state.Status=="cancelled"?state.Sources:[];
        if(state.Status is not("cancelled" or "expired")&&state.ExpiresAtUtc>clock.GetUtcNow())
        {
            try{proof=await sources.ExecuteAsync(state,user,null,ct);approval=await approvals.ReadAsync(organization,branch,date,user,ct);proof=await sources.ExecuteAsync(state,user,null,ct);}
            catch(UnauthorizedAccessException){throw;}
            catch(Exception e)when(!ct.IsCancellationRequested){logger.LogWarning("Finalization preparation validation unavailable; category {Category}",e.GetType().Name);}
        }
        state=await store.ObserveAsync(state,approval,proof,actor,clock.GetUtcNow(),ct);
        return View(state,permission.Granted&&permission.DecisionId!=Guid.Empty);
    }
    public async Task<FinalizationView> PrepareAsync(Guid organization,FinalizationCommand command,PosUserContext user,CancellationToken ct)
    {
        using var deadline=Deadline(ct);ct=deadline.Token;
        var(day,actor,_)=await Authorize(organization,command.BranchId,command.BusinessDate,user,FinalizationPreparation.PreparePermission,ct);
        FinalizationPreparation.Validate(command,day);
        if(await store.SettlementStatusAsync(day,ct) is not null)throw new DayCloseConflictException("settlement_owns_preparation");
        var approval=await approvals.ReadAsync(organization,command.BranchId,command.BusinessDate,user,ct);
        var confirmed=await Authorize(organization,command.BranchId,command.BusinessDate,user,FinalizationPreparation.PreparePermission,ct);
        if(confirmed.Item1!=day)throw new UnauthorizedAccessException();actor=confirmed.Item2;
        var state=await store.BeginAsync(day,command,approval,actor,clock.GetUtcNow(),ct);
        if(state.ClaimId is null)return await ReadAsync(organization,command.BranchId,command.BusinessDate,user,ct);
        return await Execute(state,user,actor,false,ct);
    }
    public async Task<FinalizationView> CancelAsync(Guid organization,FinalizationCancel command,PosUserContext user,CancellationToken ct)
    {
        using var deadline=Deadline(ct);ct=deadline.Token;
        var(day,actor,_)=await Authorize(organization,command.BranchId,command.BusinessDate,user,FinalizationPreparation.PreparePermission,ct);
        if(command.OperationId==Guid.Empty)throw new ArgumentException();
        if(await store.SettlementStatusAsync(day,ct) is not null)throw new DayCloseConflictException("settlement_owns_preparation");
        await Authorize(organization,command.BranchId,command.BusinessDate,user,DayClosePreparation.ReadPermission,ct);
        var state=await store.CancelAsync(day,command.OperationId,actor,clock.GetUtcNow(),ct);
        if(state.ClaimId is null)return View(state,true);
        return await Execute(state,user,actor,true,ct);
    }
    private async Task<FinalizationView> Execute(FinalizationState state,PosUserContext user,PreparationActor actor,bool cancel,CancellationToken ct)
    {
        FinalizationFence[] proof=[];ApprovalView? approval=null;bool denied=false;
        try
        {
            proof=await sources.ExecuteAsync(state,user,cancel,ct);
            if(!cancel){approval=await approvals.ReadAsync(state.Identity.OrganizationId,state.Identity.BranchId,state.Identity.BusinessDate,user,ct);proof=await sources.ExecuteAsync(state,user,null,ct);}
            var confirmed=await Authorize(state.Identity.OrganizationId,state.Identity.BranchId,state.Identity.BusinessDate,user,FinalizationPreparation.PreparePermission,ct);
            if(confirmed.Item1!=state.Identity)throw new UnauthorizedAccessException();actor=confirmed.Item2;
        }
        catch(UnauthorizedAccessException){denied=true;approval=null;proof=[];}
        catch(Exception e)when(!ct.IsCancellationRequested){logger.LogWarning("Finalization preparation source boundary unavailable; category {Category}",e.GetType().Name);approval=null;proof=[];}
        var result=await store.CompleteAsync(state,approval,proof,actor,clock.GetUtcNow(),cancel,ct);
        if(denied){logger.LogWarning("Finalization preparation authorization denied");throw new UnauthorizedAccessException();}
        logger.LogInformation("Finalization preparation completed with status {Status}",result.Status);return View(result,true);
    }
    private static FinalizationView View(FinalizationState state,bool permission)=>new(state.Identity,state.Version,state.Status,state.Command,state.Approval.ApprovalId,
        state.Command.ReviewedApprovalVersion,state.Approval.SealVersion,state.ExpiresAtUtc,state.ValidatedAtUtc,state.Sources,state.Blockers,permission);
    private static CancellationTokenSource Deadline(CancellationToken ct){var source=CancellationTokenSource.CreateLinkedTokenSource(ct);source.CancelAfter(TimeSpan.FromSeconds(25));return source;}
    private async Task<(DayIdentity,PreparationActor,RestaurantAuthorizationScope)> Authorize(Guid organization,Guid branch,DateOnly date,PosUserContext user,string permission,CancellationToken ct)
    {
        if(organization==Guid.Empty||branch==Guid.Empty||date==default||date==DateOnly.MaxValue)throw new ArgumentException();
        var scope=await scopes.GetAsync(branch,ct);
        if(scope.OrganizationId!=organization||scope.BranchId!=branch||scope.RestaurantId==Guid.Empty||string.IsNullOrWhiteSpace(user.Subject)||string.IsNullOrWhiteSpace(user.AccessToken))throw new UnauthorizedAccessException();
        var decision=await authorization.DecideAsync(user,scope,permission,ct);
        if(!decision.Granted||decision.DecisionId==Guid.Empty)throw new UnauthorizedAccessException();
        return(new(organization,scope.RestaurantId,branch,date),new(user.Subject,decision.DecisionId),scope);
    }
}
