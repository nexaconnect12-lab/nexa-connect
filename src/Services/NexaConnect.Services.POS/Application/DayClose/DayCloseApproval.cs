using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Domain.DayClose;
namespace NexaConnect.Services.POS.Application.DayClose;

public interface IApprovalSealEvidenceReader
{Task<DayEvidence> ReadRetainedAsync(DayIdentity day,DayEvidence reviewed,string token,CancellationToken ct);}
public sealed record ApprovalView(DayIdentity Identity,long Version,string Status,DayApprovalDecision? Decision,DayApprovalDecision[] History,
    bool HistoryTruncated,DateTimeOffset? ValidatedAtUtc,bool CanApprove,long? SealVersion,DayEvidence? SealSnapshot,string Reason,DayApprovalDecision? OperationDecision=null);
public interface IDayApprovalStore
{
    Task<DayApprovalDecision?> ReplayAsync(DayIdentity day,ApprovalCommand command,string subject,CancellationToken ct);
    Task<ApprovalView> ObserveAsync(DayIdentity day,long? observedSealVersion,DayEvidence? proof,PreparationActor actor,DateTimeOffset now,CancellationToken ct);
    Task<ApprovalView> ApproveAsync(DayIdentity day,ApprovalCommand command,DayEvidence proof,PreparationActor actor,DateTimeOffset now,CancellationToken ct);
}
public sealed class DayCloseApproval(IDayApprovalStore approvals,IDaySealStore seals,IApprovalSealEvidenceReader evidence,
    IRestaurantScopeReader scopes,IAuthorizationDecisionClient authorization,TimeProvider clock,ILogger<DayCloseApproval> logger)
{
    public const string ApprovePermission="pos.day-close.approve";
    public async Task<ApprovalView> ReadAsync(Guid organization,Guid branch,DateOnly date,PosUserContext user,CancellationToken ct)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(25));ct=deadline.Token;
        var(day,actor,scope)=await Authorize(organization,branch,date,user,DayClosePreparation.ReadPermission,ct);
        var permission=await authorization.DecideAsync(user,scope,ApprovePermission,ct);
        return await Fresh(day,user,actor,permission.Granted&&permission.DecisionId!=Guid.Empty,ct);
    }
    public async Task<ApprovalView> ApproveAsync(Guid organization,ApprovalCommand command,PosUserContext user,CancellationToken ct)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(25));ct=deadline.Token;
        var(day,actor,scope)=await Authorize(organization,command.BranchId,command.BusinessDate,user,ApprovePermission,ct);
        BranchDayApproval.Validate(command,day,user.Subject);
        await Authorize(organization,command.BranchId,command.BusinessDate,user,DayClosePreparation.ReadPermission,ct);
        var replay=await approvals.ReplayAsync(day,command,user.Subject,ct);
        if(replay is not null)return (await Fresh(day,user,actor,true,ct)) with{OperationDecision=replay};
        var current=await seals.ReadAsync(day,ct);
        if(current?.Version!=command.ReviewedSealVersion || current.Status!="ready_for_review" || current.Snapshot?.Seals is null)
            throw new DayCloseConflictException("reviewed_seal_not_ready");
        var proof=await evidence.ReadRetainedAsync(day,current.Snapshot,user.AccessToken,ct);
        // Recheck live hierarchy and approval/read decisions immediately before the local commit.
        var confirmed=await Authorize(organization,command.BranchId,command.BusinessDate,user,ApprovePermission,ct);
        if(confirmed.Item1!=day)throw new UnauthorizedAccessException();
        await Authorize(organization,command.BranchId,command.BusinessDate,user,DayClosePreparation.ReadPermission,ct);
        var result=await approvals.ApproveAsync(day,command,proof,confirmed.Item2,clock.GetUtcNow(),ct);
        logger.LogInformation("Day-close approval recorded with status {Status}",result.Status);
        // Later source commits can follow the preflight; approval binds the observed immutable set.
        return result with{CanApprove=true};
    }
    private async Task<ApprovalView> Fresh(DayIdentity day,PosUserContext user,PreparationActor actor,bool canApprove,CancellationToken ct)
    {
        var current=await seals.ReadAsync(day,ct);DayEvidence? proof=null;
        if(current?.Status=="ready_for_review" && current.Snapshot?.Seals is not null)
        {
            try{proof=await evidence.ReadRetainedAsync(day,current.Snapshot,user.AccessToken,ct);}
            catch(UnauthorizedAccessException){throw;}
            catch(Exception e)when(e is HttpRequestException or InvalidOperationException or System.Text.Json.JsonException || e is OperationCanceledException && !ct.IsCancellationRequested)
            {logger.LogWarning("Day-close approval validation unavailable; category {Category}",e.GetType().Name);}
        }
        var view=await approvals.ObserveAsync(day,current?.Version,proof,actor,clock.GetUtcNow(),ct);
        return view with{CanApprove=canApprove};
    }
    private async Task<(DayIdentity,PreparationActor,RestaurantAuthorizationScope)> Authorize(Guid organization,Guid branch,DateOnly date,PosUserContext user,string permission,CancellationToken ct)
    {
        if(organization==Guid.Empty || branch==Guid.Empty || date==default || date==DateOnly.MaxValue)throw new ArgumentException();
        if(string.IsNullOrWhiteSpace(user.Subject) || string.IsNullOrWhiteSpace(user.AccessToken))throw new UnauthorizedAccessException();
        var scope=await scopes.GetAsync(branch,ct);
        if(scope.OrganizationId!=organization || scope.BranchId!=branch || scope.RestaurantId==Guid.Empty)throw new UnauthorizedAccessException();
        var decision=await authorization.DecideAsync(user,scope,permission,ct);
        if(!decision.Granted || decision.DecisionId==Guid.Empty)throw new UnauthorizedAccessException();
        return (new(organization,scope.RestaurantId,branch,date),new(user.Subject,decision.DecisionId),scope);
    }
}
