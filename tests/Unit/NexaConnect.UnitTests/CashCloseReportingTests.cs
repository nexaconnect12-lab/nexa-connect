using Microsoft.Extensions.Configuration;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Reporting.Application;
using NexaConnect.Services.Reporting.Domain;
using NexaConnect.Services.POS.Application.CashReviews;
using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Domain.CashReviews;

namespace NexaConnect.UnitTests;

public sealed class CashCloseReportingTests
{
    private static PosCashCloseSnapshotV1 Event() => new(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(-1),
        "THB", 100, 95, -5, 1, 2, 0, "review_required");

    [Fact]
    public void Translation_and_versions_protect_replay_tenant_and_financial_consistency()
    {
        var value = Event(); var first = CashCloseReporting.Translate(value);
        var approved = CashCloseReporting.Translate(value with { SnapshotVersion = 2, ReviewVersion = 1, ReviewStatus = "approved" });
        var late = CashCloseReporting.Translate(value with { SnapshotVersion = 3, FinancialVersion = 3, ReviewVersion = 1, ExpectedAmount = 110, VarianceAmount = -15 });
        Assert.True(approved.ShouldReplace(first)); Assert.True(late.ShouldReplace(approved));
        Assert.False(first.ShouldReplace(late)); Assert.False(late.ShouldReplace(late));
        Assert.Throws<ArgumentException>(() => (late with { OrganizationId = Guid.NewGuid() }).ShouldReplace(first));
        Assert.Throws<ArgumentException>(() => (late with { StoreId = Guid.NewGuid() }).ShouldReplace(first));
        Assert.Throws<ArgumentException>(() => (late with { ReviewStatus = "approved" }).ShouldReplace(late));
        Assert.Throws<ArgumentException>(() => (late with { FinancialVersion = 1 }).ShouldReplace(approved));
        Assert.DoesNotContain("95", value.ToString()); Assert.Contains("redacted", first.ToString());
    }

    [Fact]
    public void Invalid_financial_events_are_rejected_before_persistence()
    {
        var e = Event();
        foreach (var bad in new[] { e with { EventId = Guid.Empty }, e with { CorrelationId = Guid.Empty },
            e with { VarianceAmount = 4 }, e with { ReviewStatus = "balanced" }, e with { ReviewStatus = "approved" },
            e with { FinancialVersion = 0 }, e with { Currency = "thb" }, e with { CountedAmount = -1 },
            e with { ClosedAtUtc = e.OccurredAtUtc.AddHours(1) }, e with { ExpectedAmount = decimal.MaxValue } })
            Assert.Throws<ArgumentException>(() => CashCloseReporting.Translate(bad));
    }

    [Theory]
    [InlineData(0, 3, 2, "approved", "balanced")]
    [InlineData(-5, 3, 2, "approved", "review_required")]
    [InlineData(-5, 3, 3, "approved", "approved")]
    [InlineData(-5, 3, 3, "investigating", "investigating")]
    public void Late_financial_versions_supersede_review(decimal variance, long financial, long reviewed, string status, string expected) =>
        Assert.Equal(expected, CashClosePublication.ReviewStatus(variance, financial, reviewed, status));

    [Fact]
    public async Task Denied_report_never_queries_projection_and_authorized_paging_is_scoped()
    {
        var repo = new Repo(); var access = new Access(); var service = new CashCloseReporting(repo, access);
        var snapshot = CashCloseReporting.Translate(Event()); var from = DateTimeOffset.UtcNow.AddDays(-1); var to = DateTimeOffset.UtcNow;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ReadAsync(snapshot.OrganizationId, snapshot.BranchId, snapshot.StoreId, from, to, 1, null, "Bearer test", default));
        Assert.Null(repo.Query); access.Allowed = true;
        repo.Rows = [new(snapshot, to), new(snapshot with { SessionId = Guid.NewGuid() }, to)];
        var page = await service.ReadAsync(snapshot.OrganizationId, snapshot.BranchId, snapshot.StoreId, from, to, 1, null, "Bearer test", default);
        Assert.Single(page.Items); Assert.NotNull(page.NextCursor); Assert.Equal(snapshot.StoreId, repo.Query!.StoreId);
        await service.ReadAsync(snapshot.OrganizationId, snapshot.BranchId, snapshot.StoreId, from, to, 1, page.NextCursor, "Bearer test", default);
        Assert.Equal(snapshot.SessionId, repo.Query!.BeforeId);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ReadAsync(snapshot.OrganizationId, snapshot.BranchId, snapshot.StoreId, from.AddDays(-31), to, 1, null, "Bearer test", default));
    }

    [Fact]
    public async Task Publisher_rejects_mismatched_authoritative_hierarchy()
    {
        var store = new PublicationStore(); var scope = new Scope(); var service = new CashClosePublisher(store, scope);
        var candidate = new CashCloseCandidate(Guid.NewGuid(), scope.Value.RestaurantId, scope.Value.BranchId);
        Assert.True(await service.PublishAsync(candidate, Guid.NewGuid(), default)); Assert.Equal(1, store.Writes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PublishAsync(candidate with { BranchId = Guid.NewGuid() }, Guid.NewGuid(), default));
        Assert.Equal(1, store.Writes);
    }
    [Fact]
    public async Task Publication_batch_can_reuse_hierarchy_client_for_multiple_branches()
    {
        var handler = new HierarchyHandler();
        using var client = new HttpClient(handler);
        using var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        Microsoft.Extensions.Caching.Memory.CacheExtensions.Set(cache, "pos-workload-token", "test-token");
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Services:Restaurant"] = "https://restaurant.test/" }).Build();
        var tokens = new NexaConnect.Services.POS.Infrastructure.Identity.PosWorkloadTokenProvider(client, config, cache);
        var hierarchy = new NexaConnect.Services.POS.Infrastructure.Restaurant.RestaurantHierarchyClient(client, tokens, config);
        Guid first = Guid.NewGuid(), second = Guid.NewGuid();
        Assert.Equal(first, (await hierarchy.GetAsync(first, default)).BranchId);
        Assert.Equal(second, (await hierarchy.GetAsync(second, default)).BranchId);
        Assert.Equal(2, handler.Calls);
    }

    private sealed class HierarchyHandler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Assert.Equal("Bearer test-token", request.Headers.Authorization?.ToString());
            var branch = Guid.Parse(request.RequestUri!.Segments[^2].TrimEnd('/'));
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = System.Net.Http.Json.JsonContent.Create(new RestaurantAuthorizationScope(Guid.NewGuid(), Guid.NewGuid(), branch)) });
        }
    }
    private sealed class Repo : ICashCloseRepository
    {
        public CashCloseQuery? Query; public IReadOnlyList<CashCloseRow> Rows = [];
        public Task<bool> ProjectAsync(Guid id, CashCloseSnapshot value, CancellationToken ct) => Task.FromResult(true);
        public Task<IReadOnlyList<CashCloseRow>> ReadAsync(CashCloseQuery query, CancellationToken ct) { Query = query; return Task.FromResult(Rows); }
    }
    private sealed class Access : ICashCloseAccess
    { public bool Allowed; public Task<bool> CanReadAsync(Guid o, Guid b, Guid s, string a, CancellationToken ct) => Task.FromResult(Allowed); }
    private sealed class PublicationStore : ICashClosePublicationStore
    {
        public int Writes;
        public Task<IReadOnlyList<CashCloseCandidate>> FindAsync(Guid? after, CancellationToken ct) => Task.FromResult<IReadOnlyList<CashCloseCandidate>>([]);
        public Task<bool> PublishAsync(CashCloseCandidate c, Guid o, Guid correlation, CancellationToken ct) { Writes++; return Task.FromResult(true); }
    }
    private sealed class Scope : IRestaurantScopeReader
    { public RestaurantAuthorizationScope Value = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()); public Task<RestaurantAuthorizationScope> GetAsync(Guid b, CancellationToken ct) => Task.FromResult(Value); }
}
