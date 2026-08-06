using NexaConnect.Services.POS.Domain.Shifts;

namespace NexaConnect.Services.POS.Application.Shifts;

public sealed record PosUserContext(string Subject, string AccessToken);

public sealed record OpenShiftCommand(Guid BranchId, Guid StoreId, Guid TerminalId, string ShiftNumber);

public sealed record OpenShiftResult(Guid ShiftId, Guid AuthorizationDecisionId);

public sealed record RestaurantAuthorizationScope(Guid OrganizationId, Guid RestaurantId, Guid BranchId);

public sealed record AuthorizationDecision(Guid DecisionId, bool Granted, decimal? EvaluatedLimit);

public sealed record ShiftSnapshot(
    Guid Id,
    Guid StoreId,
    Guid TerminalId,
    Guid RestaurantId,
    Guid BranchId,
    string EmployeeSubject,
    string ShiftNumber,
    ShiftStatus Status,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset? ClosedAtUtc,
    string OpenedBy,
    string? ClosedBy,
    Guid AuthorizationDecisionId,
    Guid? CloseAuthorizationDecisionId,
    long ConcurrencyVersion);

public interface IShiftStore
{
    Task<bool> TerminalMatchesAsync(
        Guid branchId,
        Guid storeId,
        Guid terminalId,
        Guid restaurantId,
        CancellationToken cancellationToken);

    Task CreateAsync(Shift shift, CancellationToken cancellationToken);

    Task<ShiftSnapshot?> FindOpenAsync(Guid shiftId, CancellationToken cancellationToken);

    Task<bool> TryCloseAsync(Shift shift, CancellationToken cancellationToken);
}

public interface IRestaurantScopeReader
{
    Task<RestaurantAuthorizationScope> GetAsync(Guid branchId, CancellationToken cancellationToken);
}

public interface IAuthorizationDecisionClient
{
    Task<AuthorizationDecision> DecideAsync(
        PosUserContext user,
        RestaurantAuthorizationScope scope,
        string permission,
        CancellationToken cancellationToken);
}

public sealed class ShiftApplicationService(
    IShiftStore store,
    IRestaurantScopeReader scopeReader,
    IAuthorizationDecisionClient authorization,
    TimeProvider timeProvider)
{
    public async Task<OpenShiftResult> OpenAsync(
        OpenShiftCommand command,
        PosUserContext user,
        CancellationToken cancellationToken)
    {
        RequireUser(user);
        if (command.BranchId == Guid.Empty || command.StoreId == Guid.Empty || command.TerminalId == Guid.Empty ||
            string.IsNullOrWhiteSpace(command.ShiftNumber))
        {
            throw new ShiftValidationException("Branch, store, terminal, and shift number are required.");
        }

        RestaurantAuthorizationScope scope;
        try
        {
            scope = await scopeReader.GetAsync(command.BranchId, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new ShiftDependencyException("Restaurant", exception);
        }
        EnsureBranch(scope, command.BranchId);
        if (!await store.TerminalMatchesAsync(
                command.BranchId,
                command.StoreId,
                command.TerminalId,
                scope.RestaurantId,
                cancellationToken))
        {
            throw new ShiftAuthorizationException("store-terminal-scope");
        }

        AuthorizationDecision decision;
        try
        {
            decision = await authorization.DecideAsync(
                user,
                scope,
                "pos.shift.open",
                cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new ShiftDependencyException("Authorization", exception);
        }
        if (!decision.Granted)
        {
            throw new ShiftAuthorizationException("authorization-decision");
        }

        Shift shift = Shift.Open(
            Guid.NewGuid(),
            command.StoreId,
            command.TerminalId,
            user.Subject,
            command.ShiftNumber,
            decision.DecisionId,
            timeProvider.GetUtcNow());
        await store.CreateAsync(shift, cancellationToken);
        return new OpenShiftResult(shift.Id, decision.DecisionId);
    }

    public async Task<bool> CloseAsync(
        Guid shiftId,
        PosUserContext user,
        CancellationToken cancellationToken)
    {
        RequireUser(user);
        ShiftSnapshot? snapshot = await store.FindOpenAsync(shiftId, cancellationToken);
        if (snapshot is null)
        {
            return false;
        }

        RestaurantAuthorizationScope scope;
        try
        {
            scope = await scopeReader.GetAsync(snapshot.BranchId, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new ShiftDependencyException("Restaurant", exception);
        }
        EnsureBranch(scope, snapshot.BranchId);
        if (scope.RestaurantId != snapshot.RestaurantId)
        {
            throw new ShiftAuthorizationException("restaurant-scope");
        }

        AuthorizationDecision decision;
        try
        {
            decision = await authorization.DecideAsync(
                user,
                scope,
                "pos.shift.close",
                cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new ShiftDependencyException("Authorization", exception);
        }
        if (!decision.Granted)
        {
            throw new ShiftAuthorizationException("authorization-decision");
        }

        Shift shift = Shift.Rehydrate(
            snapshot.Id,
            snapshot.StoreId,
            snapshot.TerminalId,
            snapshot.EmployeeSubject,
            snapshot.ShiftNumber,
            snapshot.Status,
            snapshot.OpenedAtUtc,
            snapshot.ClosedAtUtc,
            snapshot.OpenedBy,
            snapshot.ClosedBy,
            snapshot.AuthorizationDecisionId,
            snapshot.CloseAuthorizationDecisionId,
            snapshot.ConcurrencyVersion);
        shift.Close(user.Subject, decision.DecisionId, timeProvider.GetUtcNow());
        if (!await store.TryCloseAsync(shift, cancellationToken))
        {
            throw new ShiftConflictException();
        }

        return true;
    }

    private static void RequireUser(PosUserContext user)
    {
        if (string.IsNullOrWhiteSpace(user.Subject) || string.IsNullOrWhiteSpace(user.AccessToken))
        {
            throw new ShiftAuthorizationException("missing-subject");
        }
    }

    private static void EnsureBranch(RestaurantAuthorizationScope scope, Guid branchId)
    {
        if (scope.BranchId != branchId)
        {
            throw new ShiftAuthorizationException("branch-scope");
        }
    }
}

public sealed class ShiftAuthorizationException(string stage) : Exception($"POS shift authorization denied at {stage}.")
{
    public string Stage { get; } = stage;
}

public sealed class ShiftConflictException : Exception;

public sealed class ShiftDependencyException(string dependency, Exception innerException)
    : Exception($"The {dependency} dependency is unavailable.", innerException)
{
    public string Dependency { get; } = dependency;
}
