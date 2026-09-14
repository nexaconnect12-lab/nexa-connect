using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NexaConnect.Services.POS.Application.CashSessions;
using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Domain.CashReviews;

namespace NexaConnect.Services.POS.Application.CashReviews;

public static class CashReviewPermissions
{
    public const string Read = "pos.cash-review.read";
    public const string Resolve = "pos.cash-review.resolve";
}

public sealed record CashReviewScope(Guid OrganizationId, Guid RestaurantId, Guid BranchId, Guid StoreId);
public sealed record CashReviewAccess(bool CanRead, bool CanResolve);

public sealed record CashReviewListItem(
    Guid CashSessionId,
    Guid ShiftId,
    Guid StoreId,
    Guid TerminalId,
    string ShiftNumber,
    string CashierSubjectId,
    string Currency,
    decimal ExpectedClosingAmount,
    decimal ActualClosingAmount,
    decimal VarianceAmount,
    DateTimeOffset ClosedAtUtc,
    long SessionVersion,
    string ReviewStatus,
    long ReviewVersion,
    DateTimeOffset? ReviewedAtUtc);

public sealed record CashReviewHistoryEntry(
    Guid Id,
    long SessionVersion,
    string Decision,
    string Reason,
    string ReviewerSubjectId,
    Guid AuthorizationDecisionId,
    long ReviewVersion,
    DateTimeOffset OccurredAtUtc);

public sealed record CashReviewDetail(
    CashReviewListItem Session,
    IReadOnlyList<CashMovementSummary> Movements,
    IReadOnlyList<CashReviewHistoryEntry> History);

public sealed record CashReviewPage(IReadOnlyList<CashReviewListItem> Items, string? NextCursor);

public sealed record ResolveCashReviewCommand(
    Guid OrganizationId,
    Guid BranchId,
    Guid StoreId,
    Guid CashSessionId,
    string Decision,
    string Reason,
    long ExpectedSessionVersion,
    long ExpectedReviewVersion,
    Guid IdempotencyKey);

public interface ICashReviewStore
{
    Task<bool> StoreMatchesScopeAsync(Guid restaurantId, Guid branchId, Guid storeId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<CashReviewListItem>> ListAsync(CashReviewScope scope, DateTimeOffset fromUtc,
        DateTimeOffset toUtc, DateTimeOffset? beforeClosedAtUtc, Guid? beforeId, int take,
        CancellationToken cancellationToken);
    Task<CashReviewDetail?> GetAsync(CashReviewScope scope, Guid cashSessionId,
        CancellationToken cancellationToken);
    Task<CashReviewDetail?> ResolveAsync(CashReviewScope scope, Guid cashSessionId,
        CashReviewDecision decision, string actorSubjectId, Guid authorizationDecisionId,
        long expectedSessionVersion, long expectedReviewVersion, Guid idempotencyKey,
        string payloadHash, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken);
}

public sealed class CashReviewApplicationService(
    ICashReviewStore store,
    IRestaurantScopeReader scopeReader,
    IAuthorizationDecisionClient authorization,
    TimeProvider timeProvider)
{
    public async Task<CashReviewAccess> AccessAsync(Guid organizationId, Guid branchId, Guid storeId,
        PosUserContext user, CancellationToken cancellationToken)
    {
        CashReviewScope scope = await ResolveScopeAsync(organizationId, branchId, storeId, cancellationToken);
        bool canRead = await IsGrantedAsync(user, scope, CashReviewPermissions.Read, cancellationToken);
        bool canResolve = canRead && await IsGrantedAsync(user, scope, CashReviewPermissions.Resolve, cancellationToken);
        return new CashReviewAccess(canRead, canResolve);
    }

    public async Task<CashReviewPage> ListAsync(Guid organizationId, Guid branchId, Guid storeId,
        DateTimeOffset fromUtc, DateTimeOffset toUtc, string? cursor, int limit, PosUserContext user,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100 || fromUtc >= toUtc || toUtc - fromUtc > TimeSpan.FromDays(31))
            throw new CashReviewValidationException("Use a limit from 1 to 100 and a valid UTC range of at most 31 days.");
        CashReviewScope scope = await AuthorizeAsync(organizationId, branchId, storeId, user,
            CashReviewPermissions.Read, cancellationToken);
        (DateTimeOffset? beforeAt, Guid? beforeId) = DecodeCursor(cursor);
        IReadOnlyList<CashReviewListItem> rows;
        try
        {
            rows = await store.ListAsync(scope, fromUtc.ToUniversalTime(), toUtc.ToUniversalTime(),
                beforeAt, beforeId, limit + 1, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new CashReviewDependencyException("POS database", exception);
        }
        IReadOnlyList<CashReviewListItem> items = rows.Take(limit).ToArray();
        string? next = rows.Count > limit && items.Count > 0
            ? EncodeCursor(items[^1].ClosedAtUtc, items[^1].CashSessionId)
            : null;
        return new CashReviewPage(items, next);
    }

    public async Task<CashReviewDetail> GetAsync(Guid organizationId, Guid branchId, Guid storeId,
        Guid cashSessionId, PosUserContext user, CancellationToken cancellationToken)
    {
        if (cashSessionId == Guid.Empty) throw new CashReviewValidationException("Cash session is required.");
        CashReviewScope scope = await AuthorizeAsync(organizationId, branchId, storeId, user,
            CashReviewPermissions.Read, cancellationToken);
        try
        {
            return await store.GetAsync(scope, cashSessionId, cancellationToken)
                ?? throw new CashReviewNotFoundException();
        }
        catch (CashReviewNotFoundException) { throw; }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new CashReviewDependencyException("POS database", exception);
        }
    }

    public async Task<CashReviewDetail> ResolveAsync(ResolveCashReviewCommand command, PosUserContext user,
        CancellationToken cancellationToken)
    {
        if (command.CashSessionId == Guid.Empty || command.IdempotencyKey == Guid.Empty ||
            command.ExpectedSessionVersion <= 0 || command.ExpectedReviewVersion < 0)
            throw new CashReviewValidationException("Session, idempotency key, and reviewed versions are required.");
        CashReviewDecision decision;
        try { decision = CashReviewDecision.Create(command.Decision, command.Reason); }
        catch (ArgumentException exception) { throw new CashReviewValidationException(exception.Message); }

        CashReviewScope scope = await ResolveScopeAsync(command.OrganizationId, command.BranchId,
            command.StoreId, cancellationToken);
        AuthorizationDecision authorizationDecision = await DecideAsync(user, scope,
            CashReviewPermissions.Resolve, cancellationToken);
        if (!authorizationDecision.Granted) throw new CashReviewAuthorizationException();
        if (authorizationDecision.DecisionId == Guid.Empty)
            throw new CashReviewDependencyException("Authorization",
                new InvalidDataException("Authorization returned an empty decision identifier."));

        try
        {
            return await store.ResolveAsync(scope, command.CashSessionId, decision, user.Subject,
                authorizationDecision.DecisionId, command.ExpectedSessionVersion,
                command.ExpectedReviewVersion, command.IdempotencyKey, ComputePayloadHash(command, decision, user.Subject),
                timeProvider.GetUtcNow(), cancellationToken) ?? throw new CashReviewNotFoundException();
        }
        catch (CashReviewDuplicateOperationException) { throw; }
        catch (InvalidOperationException exception)
        {
            throw new CashReviewConflictException(exception.Message, exception);
        }
        catch (CashReviewNotFoundException) { throw; }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new CashReviewDependencyException("POS database", exception);
        }
    }

    private async Task<CashReviewScope> AuthorizeAsync(Guid organizationId, Guid branchId, Guid storeId,
        PosUserContext user, string permission, CancellationToken cancellationToken)
    {
        CashReviewScope scope = await ResolveScopeAsync(organizationId, branchId, storeId, cancellationToken);
        if (!await IsGrantedAsync(user, scope, permission, cancellationToken))
            throw new CashReviewAuthorizationException();
        return scope;
    }

    private async Task<CashReviewScope> ResolveScopeAsync(Guid organizationId, Guid branchId, Guid storeId,
        CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty || branchId == Guid.Empty || storeId == Guid.Empty)
            throw new CashReviewValidationException("Organization, branch, and store are required.");
        RestaurantAuthorizationScope restaurantScope;
        try { restaurantScope = await scopeReader.GetAsync(branchId, cancellationToken); }
        catch (HttpRequestException exception) { throw new CashReviewDependencyException("Restaurant", exception); }
        bool storeMatches;
        try
        {
            storeMatches = await store.StoreMatchesScopeAsync(
                restaurantScope.RestaurantId, branchId, storeId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new CashReviewDependencyException("POS database", exception);
        }
        if (restaurantScope.OrganizationId != organizationId || restaurantScope.BranchId != branchId || !storeMatches)
            throw new CashReviewAuthorizationException();
        return new CashReviewScope(organizationId, restaurantScope.RestaurantId, branchId, storeId);
    }

    private async Task<bool> IsGrantedAsync(PosUserContext user, CashReviewScope scope, string permission,
        CancellationToken cancellationToken) => (await DecideAsync(user, scope, permission, cancellationToken)).Granted;

    private async Task<AuthorizationDecision> DecideAsync(PosUserContext user, CashReviewScope scope,
        string permission, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(user.Subject) || user.Subject.Length > 200 ||
            string.IsNullOrWhiteSpace(user.AccessToken))
            throw new CashReviewAuthorizationException();
        try
        {
            return await authorization.DecideAsync(user,
                new RestaurantAuthorizationScope(scope.OrganizationId, scope.RestaurantId, scope.BranchId),
                permission, cancellationToken);
        }
        catch (HttpRequestException exception) { throw new CashReviewDependencyException("Authorization", exception); }
    }

    private static string ComputePayloadHash(ResolveCashReviewCommand command, CashReviewDecision decision,
        string actorSubjectId)
    {
        string value = string.Join('\n', command.OrganizationId.ToString("D"), command.BranchId.ToString("D"),
            command.StoreId.ToString("D"), command.CashSessionId.ToString("D"), decision.Code, decision.Reason,
            actorSubjectId.Trim(),
            command.ExpectedSessionVersion.ToString(CultureInfo.InvariantCulture),
            command.ExpectedReviewVersion.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static string EncodeCursor(DateTimeOffset closedAtUtc, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{closedAtUtc.UtcTicks}|{id:D}"));

    private static (DateTimeOffset?, Guid?) DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return (null, null);
        try
        {
            string[] parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
            if (parts.Length != 2 || !long.TryParse(parts[0], CultureInfo.InvariantCulture, out long ticks) ||
                !Guid.TryParse(parts[1], out Guid id) || id == Guid.Empty)
                throw new FormatException();
            return (new DateTimeOffset(ticks, TimeSpan.Zero), id);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentOutOfRangeException)
        {
            throw new CashReviewValidationException("The cash-review cursor is invalid.");
        }
    }
}

public sealed class CashReviewValidationException(string message) : Exception(message);
public sealed class CashReviewAuthorizationException() : Exception("Cash-review access was denied.");
public sealed class CashReviewNotFoundException() : Exception("The cash session was not found in this review scope.");
public sealed class CashReviewDependencyException(string dependency, Exception innerException)
    : Exception($"{dependency} is unavailable.", innerException) { public string Dependency { get; } = dependency; }
public sealed class CashReviewConflictException(string message, Exception innerException) : Exception(message, innerException);
public sealed class CashReviewDuplicateOperationException(string message) : Exception(message);
