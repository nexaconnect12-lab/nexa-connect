using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Order.Application.Orders;
using NexaConnect.Services.Order.Application.Tenant;
using NexaConnect.Services.Order.Domain;

namespace NexaConnect.UnitTests;

public sealed class BranchOrderListTests
{
    private static OrderListScope Scope() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    private static OrderListRow Row(OrderListScope scope, string status = "KitchenAccepted", bool claimed = false) =>
        new(Guid.NewGuid(), scope, "ORDER", DateTimeOffset.UtcNow, "qr", status, "THB", 80, "A1", true, true, false, claimed);

    [Fact]
    public async Task Read_authorization_is_live_and_denial_never_queries_storage()
    {
        var repository = new Repository(); var auth = new Authorization { Read = false };
        var service = new OrderListService(repository, auth, true);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListAsync(Scope(), "all", "all", null, 25, "Bearer staff", default));
        Assert.Equal(0, repository.Calls);
    }

    [Theory]
    [InlineData("all", "all", 0)] [InlineData("all", "all", 101)]
    [InlineData("qr' OR 1=1--", "all", 25)] [InlineData("all", "unknown", 25)]
    public async Task Invalid_filters_and_limits_never_query_storage(string channel, string state, int limit)
    {
        var repository = new Repository();
        await Assert.ThrowsAsync<ArgumentException>(() => new OrderListService(repository, new Authorization(), true)
            .ListAsync(Scope(), channel, state, null, limit, "Bearer staff", default));
        Assert.Equal(0, repository.Calls);
    }

    [Theory]
    [InlineData("private?token")] [InlineData("")] [InlineData("AAAA")]
    public void Cursor_rejects_unbounded_or_noncanonical_input(string value) =>
        Assert.Throws<ArgumentException>(() => OrderListCursor.Parse(value));

    [Fact]
    public async Task List_maps_original_orders_and_never_offers_paid_claimed_or_unready_orders_for_collection()
    {
        var scope = Scope();
        var ready = Row(scope);
        var repository = new Repository { Rows = [ready, Row(scope, "Paid", true), Row(scope, claimed: true),
            Row(scope, "Submitted"), Row(scope) with { Channel = "pos", AcceptedQr = false, TableCode = null },
            Row(scope) with { HasProviderIntent = true }, Row(scope) with { HasPricing = false }, Row(scope) with { AcceptedQr = false },
            Row(scope, "Cancelled", true)] };
        var page = await new OrderListService(repository, new Authorization(), true).ListAsync(scope, "all", "all", null, 25, "Bearer staff", default);
        Assert.Equal(ready.OrderId, Assert.Single(page.Items, i => i.CanCollectQr).OrderId);
        Assert.Equal("QR-" + ready.OrderId.ToString("N"), page.Items[0].CollectionReference);
        Assert.Equal("paid", page.Items[1].CollectionStatus);
        Assert.Equal("collecting", page.Items[2].CollectionStatus);
        Assert.Equal("preparing", page.Items[3].CollectionStatus);
        Assert.Null(page.Items[4].CollectionReference);
        Assert.Null(page.Items[4].CollectionStatus);
        Assert.Equal("unavailable", page.Items[8].CollectionStatus);
        Assert.False(page.Items[8].CanCollectQr);
        Assert.Equal(1, repository.Calls);
    }

    [Theory]
    [InlineData(false, true)] [InlineData(true, false)]
    public async Task Readonly_roles_or_disabled_collection_still_see_history_without_collect_authority(bool enabled, bool pay)
    {
        var scope = Scope(); var repository = new Repository { Rows = [Row(scope)] };
        var page = await new OrderListService(repository, new Authorization { Pay = pay }, enabled)
            .ListAsync(scope, "qr", "open", null, 25, "Bearer staff", default);
        Assert.False(Assert.Single(page.Items).CanCollectQr);
    }

    [Fact]
    public async Task Stable_cursor_is_from_last_returned_row_and_original_scope_filters_are_preserved()
    {
        var scope = Scope();
        var rows = new[] { Row(scope), Row(scope), Row(scope) };
        var repository = new Repository { Rows = rows };
        var page = await new OrderListService(repository, new Authorization(), true).ListAsync(scope, "qr", "open", null, 2, "Bearer staff", default);
        Assert.Equal(2, page.Items.Count);
        var cursor = OrderListCursor.Parse(page.NextCursor)!;
        Assert.Equal(rows[1].OrderId, cursor.OrderId);
        Assert.Equal(rows[1].CreatedAtUtc, cursor.CreatedAtUtc);
        Assert.Equal(scope, repository.Scope);
        Assert.Equal("qr", repository.Channel); Assert.Equal("open", repository.State); Assert.Equal(3, repository.Limit);
    }

    [Fact]
    public async Task Unexpected_repository_scope_cannot_be_disclosed()
    {
        var repository = new Repository { Rows = [Row(Scope())] };
        await Assert.ThrowsAsync<InvalidDataException>(() => new OrderListService(repository, new Authorization(), true)
            .ListAsync(Scope(), "all", "all", null, 25, "Bearer staff", default));
    }

    private sealed class Repository : IOrderListRepository
    {
        public IReadOnlyList<OrderListRow> Rows = [];
        public int Calls, Limit; public OrderListScope? Scope; public string? Channel, State;
        public Task<IReadOnlyList<OrderListRow>> ReadAsync(OrderListScope scope, string channel, string state,
            OrderListCursor? cursor, int limit, CancellationToken ct)
        { Calls++; Scope = scope; Channel = channel; State = state; Limit = limit; return Task.FromResult(Rows); }
    }

    private sealed class Authorization : IOrderTenantAuthorizer
    {
        public bool Read = true, Pay = true;
        public Task<bool> HasBranchAccessAsync(Guid org, Guid branch, string permission, string bearer, CancellationToken ct) => Task.FromResult(Read);
        public Task<Guid?> GetBranchDecisionAsync(Guid org, Guid branch, string permission, string bearer, CancellationToken ct) =>
            Task.FromResult<Guid?>(permission == ProductPermissions.OrderRead ? Read ? Guid.NewGuid() : null : Pay ? Guid.NewGuid() : null);
    }
}
