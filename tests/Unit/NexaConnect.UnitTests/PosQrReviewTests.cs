using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using NexaConnect.Contracts.RestaurantQr;
using NexaConnect.POS;

namespace NexaConnect.UnitTests;

public sealed class PosQrReviewTests
{
    private static QrRequestView Request(string state = "pending")
    {
        var id = Guid.NewGuid();
        return new(id, "A1", state, 2, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10), null,
            new(new string('a', 64), "THB", DateTimeOffset.UtcNow.AddMinutes(2),
                [new(Guid.NewGuid(), "Rice", 80, 1, 80)], 80, 80, 0, 0, 80, 0, false, 0),
            state == "accepted" ? id : null, state == "accepted" ? "kitchen_accepted" : null);
    }
    private static PosTokenSet Token() => new("fixture", null, DateTimeOffset.UtcNow.AddMinutes(5), "Bearer");
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Calls++; return send(request); }
    }
    private static HttpResponseMessage Ok(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };

    [Fact]
    public async Task Queue_uses_existing_order_route_and_tenant_bearer_correlation_without_claim_or_shift()
    {
        var config = PosCheckoutIntegrationTests.Configuration(); var item = Request();
        using var api = new PosApiClient(config, orderHandler: new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"/api/order/v1/customer/organizations/{config.OrganizationId:D}/branches/{config.BranchId:D}/qr-requests", request.RequestUri!.AbsolutePath);
            Assert.Equal("?state=pending", request.RequestUri.Query);
            Assert.Equal(config.OrganizationId.ToString("D"), Assert.Single(request.Headers.GetValues("X-Nexa-Organization-Id")));
            Assert.Equal("nexa_connect", Assert.Single(request.Headers.GetValues("X-Nexa-Application-Code")));
            Assert.True(Guid.TryParse(Assert.Single(request.Headers.GetValues("X-Correlation-ID")), out _));
            Assert.Equal("fixture", request.Headers.Authorization!.Parameter);
            Assert.Null(request.Content);
            return Task.FromResult(Ok(new QrRequestList([item], true)));
        }));
        var page = await api.ListQrRequestsAsync(Token(), "pending");
        Assert.True(page.HasMore); Assert.Equal(item.Id, Assert.Single(page.Items).Id);
        Assert.Contains("Rice", page.Items[0].Details);
    }

    [Theory]
    [InlineData(null, "accept", "accepted")]
    [InlineData(null, "accept", "price_review")]
    [InlineData("closing", "reject", "rejected")]
    public async Task Decisions_send_only_original_id_version_and_allowlisted_reason(string? reason, string action, string outcome)
    {
        var item = Request(outcome); var config = PosCheckoutIntegrationTests.Configuration();
        using var api = new PosApiClient(config, orderHandler: new Handler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method); Assert.EndsWith($"/{item.Id:D}/{action}", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(reason is null ? 1 : 2, body.RootElement.EnumerateObject().Count());
            Assert.Equal(1, body.RootElement.GetProperty("expectedVersion").GetInt64());
            if (reason is not null) Assert.Equal(reason, body.RootElement.GetProperty("reason").GetString());
            return Ok(item);
        }));
        var response = await api.DecideQrRequestAsync(Token(), item.Id, 1, reason);
        Assert.Equal(outcome, response.Status);
        if (outcome == "price_review") Assert.Null(response.OrderId);
    }

    [Theory]
    [InlineData(401)] [InlineData(403)] [InlineData(404)] [InlineData(409)] [InlineData(503)]
    public async Task Denial_conflict_and_outage_never_retry_a_decision(int status)
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        using var api = new PosApiClient(PosCheckoutIntegrationTests.Configuration(), orderHandler: handler);
        Assert.Equal(status, (await Assert.ThrowsAsync<PosApiException>(() => api.DecideQrRequestAsync(Token(), Guid.NewGuid(), 1))).StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Lost_response_is_not_retried_and_reloading_preserves_original_accepted_order()
    {
        var original = Request("accepted");
        var handler = new Handler(request => request.Method == HttpMethod.Post
            ? throw new HttpRequestException("simulated response loss") : Task.FromResult(Ok(new QrRequestList([original], false))));
        using var api = new PosApiClient(PosCheckoutIntegrationTests.Configuration(), orderHandler: handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => api.DecideQrRequestAsync(Token(), original.Id, 1));
        Assert.Equal(1, handler.Calls);
        Assert.Equal(original.Id, Assert.Single((await api.ListQrRequestsAsync(Token(), "accepted")).Items).OrderId);
        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData("identity")] [InlineData("version")] [InlineData("status")] [InlineData("currency")]
    [InlineData("totals")] [InlineData("name")] [InlineData("count")] [InlineData("duplicate")]
    public async Task Unsafe_or_inconsistent_queue_data_is_rejected(string change)
    {
        var item = Request();
        item = change switch
        {
            "identity" => item with { Status = "accepted", OrderId = Guid.NewGuid() },
            "version" => item with { Version = 0 }, "status" => item with { Status = "foreign" },
            "currency" => item with { Quote = item.Quote with { Currency = "USD" } },
            "totals" => item with { Quote = item.Quote with { TotalAmount = 90 } },
            "name" => item with { Quote = item.Quote with { Lines = [item.Quote.Lines[0] with { Name = "unsafe\nline" }] } },
            _ => item
        };
        var items = change == "count" ? Enumerable.Repeat(item, 11).ToArray() : change == "duplicate" ? [item, item] : new[] { item };
        using var api = new PosApiClient(PosCheckoutIntegrationTests.Configuration(), orderHandler: new Handler(_ => Task.FromResult(Ok(new QrRequestList(items, false)))));
        await Assert.ThrowsAsync<InvalidDataException>(() => api.ListQrRequestsAsync(Token(), "pending"));
    }

    [Fact]
    public async Task Wrong_decision_identity_is_rejected_even_on_successful_http_response()
    {
        using var api = new PosApiClient(PosCheckoutIntegrationTests.Configuration(), orderHandler: new Handler(_ => Task.FromResult(Ok(Request("accepted")))));
        await Assert.ThrowsAsync<InvalidDataException>(() => api.DecideQrRequestAsync(Token(), Guid.NewGuid(), 1));
    }

    [Theory]
    [InlineData("invalid")] [InlineData("oversize")]
    public async Task Response_format_and_size_are_bounded(string mode)
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = mode == "invalid" ? new StringContent("html") : new StringContent(new string(' ', 2097153), Encoding.UTF8, "application/json") }));
        using var api = new PosApiClient(PosCheckoutIntegrationTests.Configuration(), orderHandler: handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => api.ListQrRequestsAsync(Token(), "pending"));
    }

    [Fact]
    public void Late_responses_cannot_restore_private_data_or_authority_after_clear()
    {
        var state = new PosQrReviewState(); var generation = state.Generation;
        var pending = PosQrRequest.From(Request(), "THB");
        state.Apply(generation, new([pending], true));
        Assert.False(state.CanDecide(pending.Id, true, false, false));
        Assert.False(state.CanDecide(pending.Id, false, false, true));
        Assert.False(state.CanDecide(pending.Id, true, true, true));
        Assert.True(state.CanDecide(pending.Id, true, false, true));
        state.Clear(); Assert.False(state.Apply(generation, new([pending], true))); Assert.Empty(state.Items);
        var accepted = PosQrRequest.From(Request("accepted"), "THB");
        state.Apply(state.Generation, new([accepted], false)); Assert.False(state.CanDecide(accepted.Id, true, false, true));
    }
}
