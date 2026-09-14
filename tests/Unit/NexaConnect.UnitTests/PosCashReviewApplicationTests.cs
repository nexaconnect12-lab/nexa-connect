using NexaConnect.Services.POS.Application.CashReviews;
using NexaConnect.Services.POS.Application.CashSessions;
using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Domain.CashReviews;

namespace NexaConnect.UnitTests;

public sealed class PosCashReviewApplicationTests
{
    private static readonly Guid OrganizationId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid RestaurantId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid BranchId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid StoreId = Guid.Parse("40000000-0000-0000-0000-000000000004");
    private static readonly PosUserContext User = new("manager-1", "token");

    [Fact]
    public async Task List_requires_exact_scope_and_read_permission_and_returns_stable_cursor()
    {
        var store = new FakeReviewStore();
        var authorization = new FakeAuthorization();
        var service = Create(store, authorization);
        store.Items = [Item(3), Item(2), Item(1)];

        CashReviewPage page = await service.ListAsync(OrganizationId, BranchId, StoreId,
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), null, 2, User, default);

        Assert.Equal(2, page.Items.Count);
        Assert.NotNull(page.NextCursor);
        Assert.Contains(CashReviewPermissions.Read, authorization.Permissions);
        Assert.Equal(3, store.LastTake);
    }

    [Fact]
    public async Task List_denies_when_authorization_does_not_grant_read()
    {
        var authorization = new FakeAuthorization { Granted = false };
        var service = Create(new FakeReviewStore(), authorization);

        await Assert.ThrowsAsync<CashReviewAuthorizationException>(() => service.ListAsync(
            OrganizationId, BranchId, StoreId, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow,
            null, 20, User, default));
    }

    [Fact]
    public async Task Resolve_normalizes_decision_and_forwards_reviewed_versions_and_decision_id()
    {
        var store = new FakeReviewStore();
        var authorization = new FakeAuthorization();
        var service = Create(store, authorization);
        Guid operationId = Guid.NewGuid();

        CashReviewDetail result = await service.ResolveAsync(new ResolveCashReviewCommand(
            OrganizationId, BranchId, StoreId, store.SessionId, " APPROVE ", " checked drawer ",
            7, 2, operationId), User, default);

        Assert.Equal("approve", store.Decision!.Code);
        Assert.Equal("checked drawer", store.Decision.Reason);
        Assert.Equal(7, store.ExpectedSessionVersion);
        Assert.Equal(2, store.ExpectedReviewVersion);
        Assert.Equal(operationId, store.OperationId);
        Assert.Equal(authorization.DecisionId, store.AuthorizationDecisionId);
        Assert.Matches("^[0-9a-f]{64}$", store.PayloadHash);
        Assert.Equal(store.SessionId, result.Session.CashSessionId);
    }

    [Theory]
    [InlineData("approve", "")]
    [InlineData("dismiss", "reason")]
    public async Task Resolve_rejects_invalid_decision_before_persistence(string decision, string reason)
    {
        var store = new FakeReviewStore();
        var service = Create(store, new FakeAuthorization());

        await Assert.ThrowsAsync<CashReviewValidationException>(() => service.ResolveAsync(
            new ResolveCashReviewCommand(OrganizationId, BranchId, StoreId, store.SessionId,
                decision, reason, 1, 0, Guid.NewGuid()), User, default));
        Assert.Null(store.Decision);
    }

    private static CashReviewApplicationService Create(FakeReviewStore store, FakeAuthorization authorization) =>
        new(store, new FakeScopeReader(), authorization,
            new FixedTimeProvider(DateTimeOffset.Parse("2026-09-14T10:00:00Z")));

    private static CashReviewListItem Item(int minute) => new(Guid.NewGuid(), Guid.NewGuid(), StoreId,
        Guid.NewGuid(), $"SHIFT-{minute}", "cashier", "THB", 100m, 99m, -1m,
        DateTimeOffset.Parse("2026-09-14T10:00:00Z").AddMinutes(minute), 7,
        "review_required", 0, null);

    private sealed class FakeScopeReader : IRestaurantScopeReader
    {
        public Task<RestaurantAuthorizationScope> GetAsync(Guid branchId, CancellationToken cancellationToken) =>
            Task.FromResult(new RestaurantAuthorizationScope(OrganizationId, RestaurantId, branchId));
    }

    private sealed class FakeAuthorization : IAuthorizationDecisionClient
    {
        public bool Granted { get; set; } = true;
        public Guid DecisionId { get; } = Guid.NewGuid();
        public List<string> Permissions { get; } = [];
        public Task<AuthorizationDecision> DecideAsync(PosUserContext user, RestaurantAuthorizationScope scope,
            string permission, CancellationToken cancellationToken)
        {
            Permissions.Add(permission);
            return Task.FromResult(new AuthorizationDecision(DecisionId, Granted, null));
        }
    }

    private sealed class FakeReviewStore : ICashReviewStore
    {
        public Guid SessionId { get; } = Guid.NewGuid();
        public IReadOnlyList<CashReviewListItem> Items { get; set; } = [];
        public int LastTake { get; private set; }
        public CashReviewDecision? Decision { get; private set; }
        public long ExpectedSessionVersion { get; private set; }
        public long ExpectedReviewVersion { get; private set; }
        public Guid OperationId { get; private set; }
        public Guid AuthorizationDecisionId { get; private set; }
        public string PayloadHash { get; private set; } = "";

        public Task<bool> StoreMatchesScopeAsync(Guid restaurantId, Guid branchId, Guid storeId,
            CancellationToken cancellationToken) => Task.FromResult(storeId == StoreId);

        public Task<IReadOnlyList<CashReviewListItem>> ListAsync(CashReviewScope scope, DateTimeOffset fromUtc,
            DateTimeOffset toUtc, DateTimeOffset? beforeClosedAtUtc, Guid? beforeId, int take,
            CancellationToken cancellationToken)
        {
            LastTake = take;
            return Task.FromResult(Items);
        }

        public Task<CashReviewDetail?> GetAsync(CashReviewScope scope, Guid cashSessionId,
            CancellationToken cancellationToken) => Task.FromResult<CashReviewDetail?>(Detail());

        public Task<CashReviewDetail?> ResolveAsync(CashReviewScope scope, Guid cashSessionId,
            CashReviewDecision decision, string actorSubjectId, Guid authorizationDecisionId,
            long expectedSessionVersion, long expectedReviewVersion, Guid idempotencyKey,
            string payloadHash, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken)
        {
            Decision = decision;
            ExpectedSessionVersion = expectedSessionVersion;
            ExpectedReviewVersion = expectedReviewVersion;
            OperationId = idempotencyKey;
            AuthorizationDecisionId = authorizationDecisionId;
            PayloadHash = payloadHash;
            return Task.FromResult<CashReviewDetail?>(Detail());
        }

        private CashReviewDetail Detail() => new(Item(1) with { CashSessionId = SessionId }, [], []);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
