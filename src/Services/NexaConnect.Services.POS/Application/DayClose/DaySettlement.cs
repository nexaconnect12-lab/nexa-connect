using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Domain.DayClose;

namespace NexaConnect.Services.POS.Application.DayClose;

public sealed record SettlementSource(string Source, SourceBarrierProof Proof);
public sealed record SettlementState(Guid Id, DayIdentity Identity, SettlementCommand Command, string Subject,
    Guid AuthorizationDecisionId, Guid CorrelationId, FinalizationState Preparation, string Status,
    Guid? DecisionId, SettlementReceipt? Receipt, SettlementSource[] Sources, string? TraceCorrelationId = null);
public sealed record SettlementProgress(Guid Id,DayIdentity Identity,SettlementCommand Command,string Status,
    Guid? DecisionId,SettlementReceipt? Receipt,SettlementSource[] Sources);
public sealed record SettlementView([property:System.Text.Json.Serialization.JsonIgnore] SettlementState? Settlement,
    bool CanFinalize,bool SourceProofCurrent=false)
{
    [System.Text.Json.Serialization.JsonPropertyName("settlement")]
    public SettlementProgress? Progress=>Settlement is null?null:new(Settlement.Id,Settlement.Identity,Settlement.Command,
        Settlement.Status,Settlement.DecisionId,Settlement.Receipt,Settlement.Sources);
}

public interface IDaySettlementStore
{
    Task<SettlementState?> ReadAsync(DayIdentity day, CancellationToken ct);
    Task<SettlementState?> ReplayAsync(DayIdentity day,SettlementCommand command,string subject,CancellationToken ct);
    Task<SettlementState> BeginAsync(DayIdentity day, SettlementCommand command, FinalizationState preparation,
        ApprovalView approval, PreparationActor actor, Guid correlationId, DateTimeOffset now, CancellationToken ct);
    Task<SettlementState> DecideAsync(SettlementState state, bool commit, SettlementSource[] sources, DateTimeOffset now, CancellationToken ct);
    Task<SettlementState> AcknowledgeAsync(SettlementState state, SettlementSource[] sources, CancellationToken ct);
    Task<SettlementState[]> PendingAsync(CancellationToken ct);
}
public interface ISettlementSources
{
    Task<SettlementSource[]> ExecuteAsync(SettlementState state, string phase, CancellationToken ct);
    Task<SettlementSource[]> ReadAsync(SettlementState state, CancellationToken ct)=>Task.FromResult<SettlementSource[]>([]);
}

/// <summary>Recovery acts only on durable, previously authorized intents. It never admits a new customer command.</summary>
public sealed class DaySettlementRecovery(IDaySettlementStore store, ISettlementSources sources, TimeProvider clock,
    ILogger<DaySettlementRecovery> logger)
{
    public async Task<SettlementState> RecoverAsync(SettlementState state, CancellationToken ct)
    {
        if (state.Status is "finalized" or "aborted") return state;
        if (state.DecisionId is null)
        {
            try
            {
                var armed = await sources.ExecuteAsync(state, "armed", ct);
                state = await store.DecideAsync(state, true, armed, clock.GetUtcNow(), ct);
            }
            catch (DayCloseConflictException e) when (e.Message == "source_barrier_conflict")
            {
                state = await store.DecideAsync(state, false, [], clock.GetUtcNow(), ct);
            }
        }
        var proof = await sources.ExecuteAsync(state, state.Receipt is null ? "aborted" : "committed", ct);
        state = await store.AcknowledgeAsync(state, proof, ct);
        logger.LogInformation("Settlement recovery completed with status {Status}", state.Status);
        return state;
    }
}

public sealed class DaySettlement(IDaySettlementStore store, IFinalizationStore preparations, DayFinalizationPreparation validation,
    DayCloseApproval approvals, DaySettlementRecovery recovery, IRestaurantScopeReader scopes,
    IAuthorizationDecisionClient authorization, TimeProvider clock, ISettlementSources? sourceProof=null, ILogger<DaySettlement>? logger=null)
{
    public async Task<SettlementView> ReadAsync(Guid organization, Guid branch, DateOnly date, PosUserContext user, CancellationToken ct)
    {
        var (day, _, scope) = await Authorize(organization, branch, date, user, DayClosePreparation.ReadPermission, ct);
        var permission = await authorization.DecideAsync(user, scope, BranchDaySettlement.FinalizePermission, ct);
        var state=await store.ReadAsync(day,ct);
        if(state is not null&&sourceProof is not null)
        {
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                var proof=await sourceProof.ReadAsync(state,deadline.Token);
                if(proof.Length==3)return new(state with{Sources=proof},permission.Granted&&permission.DecisionId!=Guid.Empty,true);
            }
            catch(Exception e)when(!ct.IsCancellationRequested) { logger?.LogWarning("Settlement source observation unavailable; category {Category}",e.GetType().Name); }
        }
        return new(state,permission.Granted&&permission.DecisionId!=Guid.Empty);
    }

    public async Task<SettlementView> FinalizeAsync(Guid organization, SettlementCommand command, PosUserContext user, Guid correlationId, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(25)); ct = deadline.Token;
        var (day, actor, _) = await Authorize(organization, command.BranchId, command.BusinessDate, user, BranchDaySettlement.FinalizePermission, ct);
        await Authorize(organization, command.BranchId, command.BusinessDate, user, DayClosePreparation.ReadPermission, ct);
        if(await store.ReplayAsync(day,command,user.Subject,ct) is {} replay)
            return new(await recovery.RecoverAsync(replay,ct),true);
        var existing = await store.ReadAsync(day, ct);
        if (existing is not null && (existing.Status != "aborted" || existing.Command.OperationId == command.OperationId))
        {
            if (existing.Command != command || existing.Subject != user.Subject) throw new DayCloseConflictException("settlement_operation_conflict");
            return new(await recovery.RecoverAsync(existing, ct), true);
        }
        await validation.ReadAsync(organization, command.BranchId, command.BusinessDate, user, ct);
        var approval = await approvals.ReadAsync(organization, command.BranchId, command.BusinessDate, user, ct);
        var prepared = await preparations.ReadAsync(day, ct) ?? throw new DayCloseConflictException("preparation_missing");
        BranchDaySettlement.ValidateAdmission(command, prepared, approval.Decision, approval.Version, approval.Status, clock.GetUtcNow());
        var confirmed = await Authorize(organization, command.BranchId, command.BusinessDate, user, BranchDaySettlement.FinalizePermission, ct);
        if (confirmed.Item1 != day) throw new UnauthorizedAccessException(); actor = confirmed.Item2;
        await Authorize(organization, command.BranchId, command.BusinessDate, user, DayClosePreparation.ReadPermission, ct);
        var state = await store.BeginAsync(day, command, prepared, approval, actor, correlationId, clock.GetUtcNow(), ct);
        return new(await recovery.RecoverAsync(state, ct), true);
    }

    private async Task<(DayIdentity, PreparationActor, RestaurantAuthorizationScope)> Authorize(Guid organization, Guid branch, DateOnly date,
        PosUserContext user, string permission, CancellationToken ct)
    {
        if (organization == Guid.Empty || branch == Guid.Empty || date == default || date == DateOnly.MaxValue) throw new ArgumentException();
        var scope = await scopes.GetAsync(branch, ct);
        if (scope.OrganizationId != organization || scope.BranchId != branch || scope.RestaurantId==Guid.Empty
            ||string.IsNullOrWhiteSpace(user.Subject)||user.Subject.Length>128||user.Subject.Any(char.IsControl)||string.IsNullOrWhiteSpace(user.AccessToken))
            throw new UnauthorizedAccessException();
        var decision = await authorization.DecideAsync(user, scope, permission, ct);
        if (!decision.Granted || decision.DecisionId == Guid.Empty) throw new UnauthorizedAccessException();
        return (new(organization, scope.RestaurantId, branch, date), new(user.Subject, decision.DecisionId), scope);
    }
}
