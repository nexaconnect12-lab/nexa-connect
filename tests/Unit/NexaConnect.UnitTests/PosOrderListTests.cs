using System.Net;
using System.Net.Http.Json;
using NexaConnect.POS;

namespace NexaConnect.UnitTests;

public sealed class PosOrderListTests
{
    [Theory]
    [InlineData("shift", "Open a shift")]
    [InlineData("cart", "Finish or clear")]
    [InlineData("pending", "Verify the original")]
    [InlineData("paid", "Already paid")]
    [InlineData("claimed", "Collection has already started")]
    [InlineData("permission", "Collection is unavailable")]
    [InlineData("ready", "Ready to load")]
    public void Collection_guidance_explains_the_specific_blocker_without_granting_authority(string scenario, string message)
    {
        var item = Item(scenario != "permission") with {
            Status = scenario == "paid" ? "Paid" : "KitchenAccepted",
            CollectionStatus = scenario == "claimed" ? "collecting" : "awaiting_cashier"
        };
        var state = new PosOrderListState();
        state.Apply(state.Generation, Page(PosCheckoutIntegrationTests.Configuration(), item));
        Assert.StartsWith(message, state.CollectionGuidance(item.OrderId, true, scenario != "shift", false,
            scenario == "pending", scenario == "cart", true, "THB"));
        Assert.Equal("Preparing", (item with { Status = "KitchenAccepted" }).StatusLabel);
        Assert.Equal("Awaiting cashier", (item with { CollectionStatus = "awaiting_cashier" }).CollectionLabel);
    }
    private static PosBranchOrder Item(bool collect = true) => new(Guid.NewGuid(), "QR order", DateTimeOffset.UtcNow,
        "qr", "KitchenAccepted", "THB", 80, "A1", null, "awaiting_cashier", collect);
    private static PosBranchOrderPage Page(PosClientConfiguration c, PosBranchOrder item) =>
        new(c.OrganizationId, c.RestaurantId, c.BranchId, [item with { CollectionReference = "QR-" + item.OrderId.ToString("N") }], null);
    private static PosTokenSet Token() => new("synthetic", null, DateTimeOffset.UtcNow.AddMinutes(5), "Bearer");

    [Fact]
    public async Task List_is_correlated_readonly_get_scoped_to_configured_branch_without_shift_or_claim()
    {
        var c = PosCheckoutIntegrationTests.Configuration(); var item = Item();
        var handler = new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains($"organizations/{c.OrganizationId:D}/branches/{c.BranchId:D}/orders", request.RequestUri!.AbsolutePath);
            Assert.Contains($"restaurantId={c.RestaurantId:D}", request.RequestUri.Query);
            Assert.Contains("channel=qr&state=open&limit=25", request.RequestUri.Query);
            Assert.Equal(c.OrganizationId.ToString("D"), Assert.Single(request.Headers.GetValues("X-Nexa-Organization-Id")));
            Assert.Equal("nexa_connect", Assert.Single(request.Headers.GetValues("X-Nexa-Application-Code")));
            Assert.True(Guid.TryParse(Assert.Single(request.Headers.GetValues("X-Correlation-ID")), out _));
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Null(request.Content);
            return JsonContent.Create(Page(c, item));
        });
        using var api = new PosApiClient(c, orderHandler: handler);
        Assert.Equal(item.OrderId, Assert.Single((await api.ListOrdersAsync(Token(), "qr", "open")).Items).OrderId);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("organization")] [InlineData("branch")] [InlineData("restaurant")]
    [InlineData("paid-collect")] [InlineData("reference")] [InlineData("duplicates")] [InlineData("cursor")]
    public async Task Wrong_scope_or_unsafe_collection_projection_is_rejected(string change)
    {
        var c = PosCheckoutIntegrationTests.Configuration(); var page = Page(c, Item());
        page = change switch
        {
            "organization" => page with { OrganizationId = Guid.NewGuid() },
            "branch" => page with { BranchId = Guid.NewGuid() },
            "restaurant" => page with { RestaurantId = Guid.NewGuid() },
            "paid-collect" => page with { Items = [page.Items[0] with { Status = "Paid" }] },
            "reference" => page with { Items = [page.Items[0] with { CollectionReference = "QR-" + Guid.NewGuid().ToString("N") }] },
            "duplicates" => page with { Items = [page.Items[0], page.Items[0]] },
            _ => page with { NextCursor = "private?value" }
        };
        using var api = new PosApiClient(c, orderHandler: new Handler(_ => JsonContent.Create(page)));
        await Assert.ThrowsAsync<InvalidDataException>(() => api.ListOrdersAsync(Token()));
    }

    [Fact]
    public async Task Invalid_filters_do_not_send_a_request()
    {
        var handler = new Handler(_ => throw new InvalidOperationException());
        using var api = new PosApiClient(PosCheckoutIntegrationTests.Configuration(), orderHandler: handler);
        await Assert.ThrowsAsync<ArgumentException>(() => api.ListOrdersAsync(Token(), "foreign?scope"));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Oversized_history_response_is_rejected_before_deserialization()
    {
        using var api = new PosApiClient(PosCheckoutIntegrationTests.Configuration(), orderHandler:
            new Handler(_ => new StringContent(new string('x', 262145))));
        await Assert.ThrowsAsync<HttpRequestException>(() => api.ListOrdersAsync(Token()));
    }

    [Fact]
    public void Lock_or_signout_invalidates_inflight_pages_and_clears_identifiers_and_cursor()
    {
        var c = PosCheckoutIntegrationTests.Configuration(); var state = new PosOrderListState();
        int generation = state.Clear(); var page = Page(c, Item()) with { NextCursor = "next" };
        Assert.True(state.Apply(generation, page)); Assert.Single(state.Items);
        state.Clear();
        Assert.Empty(state.Items); Assert.Null(state.NextCursor);
        Assert.False(state.Apply(generation, page)); Assert.Empty(state.Items);
    }

    [Theory]
    [InlineData("signed-out")] [InlineData("no-shift")] [InlineData("busy")]
    [InlineData("pending")] [InlineData("cart")] [InlineData("card")] [InlineData("currency")]
    public void List_selection_cannot_replace_pending_work_or_bypass_shift_and_session(string blocked)
    {
        var c = PosCheckoutIntegrationTests.Configuration(); var state = new PosOrderListState(); var page = Page(c, Item());
        state.Apply(state.Clear(), page); Guid id = page.Items[0].OrderId;
        Assert.True(state.CanCollect(id, true, true, false, false, false, true, "THB"));
        Assert.False(state.CanCollect(id, blocked != "signed-out", blocked != "no-shift", blocked == "busy",
            blocked == "pending", blocked == "cart", blocked != "card", blocked == "currency" ? "USD" : "THB"));
        Assert.False(state.CanCollect(Guid.NewGuid(), true, true, false, false, false, true, "THB"));
    }

    [Fact]
    public void Paid_or_claimed_rows_never_enable_a_new_collection()
    {
        var c = PosCheckoutIntegrationTests.Configuration(); var state = new PosOrderListState(); var page = Page(c, Item(false));
        state.Apply(state.Clear(), page);
        Assert.False(state.CanCollect(page.Items[0].OrderId, true, true, false, false, false, true, "THB"));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpContent> content) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content(request) }); }
    }
}
