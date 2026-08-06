using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Domain.Shifts;

namespace NexaConnect.UnitTests;

public sealed class PosShiftApplicationTests
{
    private static readonly Guid BranchId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid RestaurantId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid OrganizationId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid StoreId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid TerminalId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid DecisionId = Guid.Parse("60000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Open_requires_a_matching_active_store_and_terminal()
    {
        var store = new FakeShiftStore { TerminalMatches = false };
        var authorization = new FakeAuthorizationClient();
        var service = CreateService(store, authorization);

        ShiftAuthorizationException exception = await Assert.ThrowsAsync<ShiftAuthorizationException>(() => service.OpenAsync(
            new OpenShiftCommand(BranchId, StoreId, TerminalId, "SHIFT-001"),
            User(),
            CancellationToken.None));

        Assert.Equal("store-terminal-scope", exception.Stage);
        Assert.False(authorization.WasCalled);
        Assert.Null(store.Created);
    }

    [Fact]
    public async Task Open_persists_an_authorized_shift_aggregate()
    {
        var store = new FakeShiftStore { TerminalMatches = true };
        var authorization = new FakeAuthorizationClient
        {
            Decision = new AuthorizationDecision(DecisionId, true, null)
        };
        var service = CreateService(store, authorization);

        OpenShiftResult result = await service.OpenAsync(
            new OpenShiftCommand(BranchId, StoreId, TerminalId, "SHIFT-001"),
            User(),
            CancellationToken.None);

        Assert.Equal(DecisionId, result.AuthorizationDecisionId);
        Assert.NotNull(store.Created);
        Assert.Equal(ShiftStatus.Open, store.Created!.Status);
        Assert.Equal("employee-1", store.Created.EmployeeSubject);
        Assert.Equal(DecisionId, store.Created.AuthorizationDecisionId);
    }

    [Fact]
    public async Task Close_rejects_a_concurrent_change()
    {
        var opened = Shift.Open(
            Guid.Parse("70000000-0000-0000-0000-000000000001"),
            StoreId,
            TerminalId,
            "employee-1",
            "SHIFT-001",
            DecisionId,
            DateTimeOffset.UtcNow.AddHours(-1));
        var store = new FakeShiftStore
        {
            OpenShift = new ShiftSnapshot(
                opened.Id,
                opened.StoreId,
                opened.TerminalId,
                RestaurantId,
                BranchId,
                opened.EmployeeSubject,
                opened.ShiftNumber,
                opened.Status,
                opened.OpenedAtUtc,
                opened.ClosedAtUtc,
                opened.OpenedBy,
                opened.ClosedBy,
                opened.AuthorizationDecisionId,
                opened.CloseAuthorizationDecisionId,
                opened.ConcurrencyVersion),
            CloseResult = false
        };
        var authorization = new FakeAuthorizationClient
        {
            Decision = new AuthorizationDecision(DecisionId, true, null)
        };
        var service = CreateService(store, authorization);

        await Assert.ThrowsAsync<ShiftConflictException>(() => service.CloseAsync(
            opened.Id,
            User(),
            CancellationToken.None));
    }

    private static ShiftApplicationService CreateService(
        FakeShiftStore store,
        FakeAuthorizationClient authorization) => new(
        store,
        new FakeScopeReader(),
        authorization,
        TimeProvider.System);

    private static PosUserContext User() => new("employee-1", "access-token");

    private sealed class FakeScopeReader : IRestaurantScopeReader
    {
        public Task<RestaurantAuthorizationScope> GetAsync(Guid branchId, CancellationToken cancellationToken) =>
            Task.FromResult(new RestaurantAuthorizationScope(OrganizationId, RestaurantId, branchId));
    }

    private sealed class FakeAuthorizationClient : IAuthorizationDecisionClient
    {
        public AuthorizationDecision Decision { get; set; } = new(DecisionId, false, null);
        public bool WasCalled { get; private set; }

        public Task<AuthorizationDecision> DecideAsync(
            PosUserContext user,
            RestaurantAuthorizationScope scope,
            string permission,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(Decision);
        }
    }

    private sealed class FakeShiftStore : IShiftStore
    {
        public bool TerminalMatches { get; init; }
        public Shift? Created { get; private set; }
        public ShiftSnapshot? OpenShift { get; init; }
        public bool CloseResult { get; init; } = true;

        public Task<bool> TerminalMatchesAsync(
            Guid branchId,
            Guid storeId,
            Guid terminalId,
            Guid restaurantId,
            CancellationToken cancellationToken) => Task.FromResult(TerminalMatches);

        public Task CreateAsync(Shift shift, CancellationToken cancellationToken)
        {
            Created = shift;
            return Task.CompletedTask;
        }

        public Task<ShiftSnapshot?> FindOpenAsync(Guid shiftId, CancellationToken cancellationToken) =>
            Task.FromResult(OpenShift);

        public Task<bool> TryCloseAsync(Shift shift, CancellationToken cancellationToken) =>
            Task.FromResult(CloseResult);
    }
}
