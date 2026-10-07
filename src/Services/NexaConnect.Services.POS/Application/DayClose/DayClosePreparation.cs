using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Domain.DayClose;
namespace NexaConnect.Services.POS.Application.DayClose;

public sealed record PreparationActor(string Subject, Guid DecisionId);
public sealed record PreparationLease(PreparationState State, Guid? ClaimId);
public sealed record PreparationView(DayIdentity Identity, long Version, string Status, DayEvidence? Snapshot,
    string[] Blockers, DateTimeOffset? ValidatedAtUtc, bool CanPrepare, PreparationCommand? PendingCommand,long? PendingSealChanges=null);
public interface IDayCloseStore
{
    Task<PreparationState?> ReadAsync(DayIdentity day, CancellationToken ct);
    Task<PreparationLease> BeginAsync(DayIdentity day, PreparationCommand command, PreparationActor actor, DateTimeOffset now, CancellationToken ct);
    Task<PreparationState> CompleteAsync(DayIdentity day, Guid claim, DayEvidence? evidence, PreparationActor actor, DateTimeOffset now, CancellationToken ct);
    Task<PreparationState> ValidateAsync(DayIdentity day, long expectedVersion, DayEvidence? evidence, PreparationActor actor, DateTimeOffset now, CancellationToken ct);
}
public interface IDayCloseEvidenceReader { Task<DayEvidence> ReadAsync(DayIdentity day, string token, CancellationToken ct); }
public sealed class DayClosePreparation(IDayCloseStore store, IDayCloseEvidenceReader evidence,
    IRestaurantScopeReader scopes, IAuthorizationDecisionClient authorization, TimeProvider clock,
    ILogger<DayClosePreparation> logger,bool sealing=false)
{
    public const string ReadPermission = "pos.day-close.read", PreparePermission = "pos.day-close.prepare";
    public async Task<PreparationView> ReadAsync(Guid organization, Guid branch, DateOnly date, PosUserContext user, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(25));
        var (day, actor, scope) = await Authorize(organization, branch, date, user, ReadPermission, deadline.Token);
        bool canPrepare = (await authorization.DecideAsync(user, scope, PreparePermission, deadline.Token)).Granted;
        return await Fresh(day, user, actor, canPrepare, deadline.Token);
    }
    public async Task<PreparationView> PrepareAsync(Guid organization, PreparationCommand command, PosUserContext user, CancellationToken ct)
    {
        if (command.OperationId == Guid.Empty || command.ExpectedVersion < 0 || command.ReasonCode is not ("routine_close" or "recheck")
            || !sealing && command.ReviewedCutoffVersion is not null) throw new ArgumentException();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(25));
        var (day, actor, _) = await Authorize(organization, command.BranchId, command.BusinessDate, user, PreparePermission, deadline.Token);
        var lease = await store.BeginAsync(day, command, actor, clock.GetUtcNow(), deadline.Token);
        if (lease.ClaimId is null) return await Fresh(day, user, actor, true, deadline.Token);
        DayEvidence? latest = null;
        try { latest = await evidence.ReadAsync(day, user.AccessToken, deadline.Token); }
        catch (Exception e) when (e is HttpRequestException or InvalidOperationException or System.Text.Json.JsonException || e is OperationCanceledException && !deadline.IsCancellationRequested)
        { logger.LogWarning("Day-close preparation source unavailable; category {Category}", e.GetType().Name); }
        // Cancellation leaves the durable lease for explicit resume; dependency failure becomes Blocked.
        var state = await store.CompleteAsync(day, lease.ClaimId.Value, latest, actor, clock.GetUtcNow(), deadline.Token);
        logger.LogInformation("Day-close preparation completed with status {Status}", state.Status);
        return View(state, true, latest is null ? null : clock.GetUtcNow(), user.Subject);
    }
    private async Task<PreparationView> Fresh(DayIdentity day, PosUserContext user, PreparationActor actor, bool canPrepare, CancellationToken ct)
    {
        var state = await store.ReadAsync(day, ct);
        if (state is null) return new(day, 0, "not_prepared", null, ["not_prepared"], null, canPrepare, null);
        DateTimeOffset? validated = null;
        if (state.Status == "ready_for_review")
        {
            DayEvidence? latest = null;
            try { latest = await evidence.ReadAsync(day, user.AccessToken, ct); validated = clock.GetUtcNow(); }
            catch (Exception e) when (e is HttpRequestException or InvalidOperationException or System.Text.Json.JsonException || e is OperationCanceledException && !ct.IsCancellationRequested)
            { logger.LogWarning("Day-close validation source unavailable; category {Category}", e.GetType().Name); }
            state = await store.ValidateAsync(day, state.Version, latest, actor, clock.GetUtcNow(), ct);
        }
        return View(state, canPrepare, validated, user.Subject);
    }
    private async Task<(DayIdentity, PreparationActor, RestaurantAuthorizationScope)> Authorize(Guid organization, Guid branch, DateOnly date,
        PosUserContext user, string permission, CancellationToken ct)
    {
        if (organization == Guid.Empty || branch == Guid.Empty || date == default || date == DateOnly.MaxValue) throw new ArgumentException();
        if (string.IsNullOrWhiteSpace(user.Subject) || string.IsNullOrWhiteSpace(user.AccessToken)) throw new UnauthorizedAccessException();
        var scope = await scopes.GetAsync(branch, ct);
        if (scope.OrganizationId != organization || scope.BranchId != branch || scope.RestaurantId == Guid.Empty) throw new UnauthorizedAccessException();
        var decision = await authorization.DecideAsync(user, scope, permission, ct);
        if (!decision.Granted || decision.DecisionId == Guid.Empty) throw new UnauthorizedAccessException();
        return (new(organization, scope.RestaurantId, branch, date), new(user.Subject, decision.DecisionId), scope);
    }
    private static PreparationView View(PreparationState state, bool canPrepare, DateTimeOffset? validated, string subject) =>
        new(state.Identity, state.Version, state.Status, state.Snapshot, state.Blockers, validated, canPrepare,
            state.PreparingSubject == subject && canPrepare ? state.PendingCommand : null,state.PendingSealChanges);
}
