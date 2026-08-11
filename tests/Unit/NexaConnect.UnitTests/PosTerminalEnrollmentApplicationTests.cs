using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Application.Terminals;

namespace NexaConnect.UnitTests;

public sealed class PosTerminalEnrollmentApplicationTests
{
    private static readonly Guid BranchId = Guid.NewGuid();
    private static readonly Guid StoreId = Guid.NewGuid();
    private static readonly Guid TerminalId = Guid.NewGuid();

    [Fact]
    public async Task Enrollment_requires_an_authorization_grant_before_persistence()
    {
        var store = new FakeTerminalStore();
        var service = new TerminalEnrollmentApplicationService(
            store,
            new FakeScopeReader(),
            new FakeAuthorizationClient(false));

        TerminalEnrollmentAuthorizationException exception =
            await Assert.ThrowsAsync<TerminalEnrollmentAuthorizationException>(() => service.EnrollAsync(
                new EnrollTerminalCommand(BranchId, StoreId, TerminalId, " POS-1 ", "pos"),
                new PosUserContext("manager-1", "token"),
                CancellationToken.None));

        Assert.Equal("authorization-decision", exception.Stage);
        Assert.False(store.WasCalled);
    }

    [Fact]
    public async Task Enrollment_trims_code_and_persists_the_resolved_scope()
    {
        var store = new FakeTerminalStore();
        var service = new TerminalEnrollmentApplicationService(
            store,
            new FakeScopeReader(),
            new FakeAuthorizationClient(true));

        bool enrolled = await service.EnrollAsync(
            new EnrollTerminalCommand(BranchId, StoreId, TerminalId, " POS-1 ", "pos"),
            new PosUserContext("manager-1", "token"),
            CancellationToken.None);

        Assert.True(enrolled);
        Assert.Equal("POS-1", store.Code);
    }

    [Fact]
    public async Task Enrollment_maps_an_empty_restaurant_response_to_a_dependency_failure()
    {
        var service = new TerminalEnrollmentApplicationService(
            new FakeTerminalStore(),
            new FailingScopeReader(),
            new FakeAuthorizationClient(true));

        TerminalEnrollmentDependencyException exception =
            await Assert.ThrowsAsync<TerminalEnrollmentDependencyException>(() => service.EnrollAsync(
                new EnrollTerminalCommand(BranchId, StoreId, TerminalId, "POS-1", "pos"),
                new PosUserContext("manager-1", "token"),
                CancellationToken.None));

        Assert.Equal("Restaurant", exception.Dependency);
    }

    private sealed class FakeScopeReader : IRestaurantScopeReader
    {
        public Task<RestaurantAuthorizationScope> GetAsync(Guid branchId, CancellationToken cancellationToken) =>
            Task.FromResult(new RestaurantAuthorizationScope(Guid.NewGuid(), Guid.NewGuid(), branchId));
    }

    private sealed class FailingScopeReader : IRestaurantScopeReader
    {
        public Task<RestaurantAuthorizationScope> GetAsync(Guid branchId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The Restaurant response was empty.");
    }

    private sealed class FakeAuthorizationClient(bool granted) : IAuthorizationDecisionClient
    {
        public Task<AuthorizationDecision> DecideAsync(PosUserContext user, RestaurantAuthorizationScope scope, string permission, CancellationToken cancellationToken) =>
            Task.FromResult(new AuthorizationDecision(Guid.NewGuid(), granted, null));
    }

    private sealed class FakeTerminalStore : ITerminalStore
    {
        public bool WasCalled { get; private set; }
        public string? Code { get; private set; }

        public Task<bool> EnrollAsync(Guid organizationId, Guid restaurantId, Guid branchId, Guid storeId, Guid terminalId, string code, string deviceType, CancellationToken cancellationToken)
        {
            WasCalled = true;
            Code = code;
            return Task.FromResult(true);
        }
    }
}
