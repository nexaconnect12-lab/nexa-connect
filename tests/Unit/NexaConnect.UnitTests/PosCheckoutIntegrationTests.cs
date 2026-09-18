using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.POS;

namespace NexaConnect.UnitTests;

public sealed class PosCheckoutIntegrationTests
{
    [Fact]
    public async Task Omise_token_is_transient_and_not_part_of_saved_checkout()
    {
        var config = Configuration() with { PaymentMethod = "card_omise_test", EnableOmiseTestCheckout = true };
        var checkout = PendingCheckout.Create(config, [new CheckoutLine(Guid.NewGuid(), 1)]);
        const string cardToken = "tokn_test_aaaaaaaaaaaa";
        Assert.DoesNotContain(cardToken, JsonSerializer.Serialize(checkout));
        using var api = new PosApiClient(config, orderHandler: new Handler(async request =>
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(cardToken, json.RootElement.GetProperty("cardToken").GetString());
            Assert.Equal(checkout.OrderId, json.RootElement.GetProperty("orderId").GetGuid());
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = JsonContent.Create(new PosOrderResult(checkout.OrderId, PosOrderStatus.Paid, 50m, "THB")) };
        }));
        await api.PlaceOrderAsync(Token(), checkout, cardToken);
    }

    [Fact]
    public void Omise_checkout_requires_explicit_local_enablement()
    {
        var config = Configuration() with { PaymentMethod = "card_omise_test" };
        Assert.Throws<InvalidDataException>(() => config.ValidateCheckout());
        Assert.Throws<InvalidDataException>(() => (config with { EnableOmiseTestCheckout = true, OrderApi = "https://example.invalid/" }).ValidateCheckout());
    }

    [Fact]
    public async Task Manual_checkout_rejects_a_forged_card_token_required_flag()
    {
        var config = Configuration();
        var checkout = PendingCheckout.Create(config, [new CheckoutLine(Guid.NewGuid(), 1)]);
        using var api = new PosApiClient(config, orderHandler: new Handler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            { Content = JsonContent.Create(new PosOrderResult(checkout.OrderId, PosOrderStatus.KitchenAccepted, 50m, "THB", true)) })));
        await Assert.ThrowsAsync<InvalidDataException>(() => api.PlaceOrderAsync(Token(), checkout));
    }
    [Fact]
    public async Task Cash_session_conflict_surfaces_safe_problem_guidance()
    {
        const string guidance = "This shift already has a closed cash session. Close the shift and open a new shift before opening another cash session.";
        using var api = new PosApiClient(Configuration(), posHandler: new Handler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = JsonContent.Create(new ProblemDetails { Title = guidance, Status = 409 })
            })));

        PosApiException exception = await Assert.ThrowsAsync<PosApiException>(() => api.OpenCashSessionAsync(
            Token(), Guid.NewGuid(), Guid.NewGuid(), "THB", 100m));

        Assert.Equal(409, exception.StatusCode);
        Assert.Equal(guidance, exception.Message);
    }

    [Fact]
    public async Task Cash_reconciliation_and_close_send_terminal_scope_and_reviewed_version()
    {
        var config = Configuration();
        Guid cashSessionId = Guid.NewGuid();
        int requests = 0;
        var handler = new Handler(async request =>
        {
            Assert.Equal(config.TerminalId.ToString("D"),
                request.Headers.GetValues("X-Nexa-Terminal-Id").Single());
            if (requests++ == 0)
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new PosCashSessionSummary(
                        cashSessionId, Guid.NewGuid(), config.StoreId, config.TerminalId, "THB",
                        100m, 25m, 125m, null, null, "open", DateTimeOffset.UtcNow, null, 7,
                        [new PosCashMovementSummary(Guid.NewGuid(), "sale", 25m, "ORDER", DateTimeOffset.UtcNow)]))
                };
            }

            Assert.Equal(HttpMethod.Post, request.Method);
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(124m, body.RootElement.GetProperty("actualClosingAmount").GetDecimal());
            Assert.Equal(7, body.RootElement.GetProperty("expectedConcurrencyVersion").GetInt64());
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        using var api = new PosApiClient(config, posHandler: handler);

        PosCashSessionSummary summary = await api.GetCashSessionSummaryAsync(Token(), cashSessionId);
        await api.CloseCashSessionAsync(Token(), cashSessionId, 124m, summary.ConcurrencyVersion);

        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task Cash_reconciliation_rejects_a_summary_for_another_terminal()
    {
        var config = Configuration();
        Guid cashSessionId = Guid.NewGuid();
        using var api = new PosApiClient(config, posHandler: new Handler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new PosCashSessionSummary(
                    cashSessionId, Guid.NewGuid(), config.StoreId, Guid.NewGuid(), "THB",
                    100m, 0m, 100m, null, null, "open", DateTimeOffset.UtcNow, null, 1, []))
            })));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            api.GetCashSessionSummaryAsync(Token(), cashSessionId));
    }

    [Fact]
    public async Task Cash_review_client_sends_configured_scope_and_validates_decision_identity()
    {
        var config = Configuration();
        Guid sessionId = Guid.NewGuid();
        Guid operationId = Guid.NewGuid();
        int requestNumber = 0;
        PosCashReviewListItem openItem = ReviewItem(config, sessionId, "review_required", 0);
        var handler = new Handler(async request =>
        {
            if (request.Content is null)
            {
                Assert.Contains($"organizationId={config.OrganizationId:D}", request.RequestUri!.Query);
                Assert.Contains($"branchId={config.BranchId:D}", request.RequestUri.Query);
                Assert.Contains($"storeId={config.StoreId:D}", request.RequestUri.Query);
            }
            else
            {
                using JsonDocument scopeBody = JsonDocument.Parse(await request.Content.ReadAsStringAsync());
                Assert.Equal(config.OrganizationId, scopeBody.RootElement.GetProperty("organizationId").GetGuid());
                Assert.Equal(config.BranchId, scopeBody.RootElement.GetProperty("branchId").GetGuid());
                Assert.Equal(config.StoreId, scopeBody.RootElement.GetProperty("storeId").GetGuid());
            }
            return requestNumber++ switch
            {
                0 => new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = JsonContent.Create(new PosCashReviewAccess(true, true)) },
                1 => new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = JsonContent.Create(new PosCashReviewPage([openItem], null)) },
                2 => new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = JsonContent.Create(new PosCashReviewDetail(openItem, [], [])) },
                _ => await DecisionResponseAsync(request)
            };
        });
        using var api = new PosApiClient(config, posHandler: handler);

        Assert.True((await api.GetCashReviewAccessAsync(Token())).CanResolve);
        Assert.Single((await api.GetCashReviewsAsync(Token(), DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow)).Items);
        Assert.Equal(sessionId, (await api.GetCashReviewAsync(Token(), sessionId)).Session.CashSessionId);
        PosCashReviewDetail resolved = await api.ResolveCashReviewAsync(Token(), sessionId, "approve",
            "Drawer checked", 2, 0, operationId);

        Assert.Equal("approved", resolved.Session.ReviewStatus);
        Assert.Contains(resolved.History, entry => entry.Id == operationId);

        async Task<HttpResponseMessage> DecisionResponseAsync(HttpRequestMessage request)
        {
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(operationId, body.RootElement.GetProperty("idempotencyKey").GetGuid());
            Assert.Equal(2, body.RootElement.GetProperty("expectedSessionVersion").GetInt64());
            var approved = ReviewItem(config, sessionId, "approved", 1);
            var history = new PosCashReviewHistoryEntry(operationId, 2, "approve", "Drawer checked",
                "manager", Guid.NewGuid(), 1, DateTimeOffset.UtcNow);
            return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = JsonContent.Create(new PosCashReviewDetail(approved, [], [history])) };
        }
    }

    [Fact]
    public async Task Cash_review_client_rejects_cross_store_response()
    {
        var config = Configuration();
        using var api = new PosApiClient(config, posHandler: new Handler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new PosCashReviewPage(
                    [ReviewItem(config with { StoreId = Guid.NewGuid() }, Guid.NewGuid(), "review_required", 0)], null))
            })));

        await Assert.ThrowsAsync<InvalidDataException>(() => api.GetCashReviewsAsync(Token(),
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow));
    }

    private static PosCashReviewListItem ReviewItem(PosClientConfiguration config, Guid sessionId,
        string status, long reviewVersion) => new(sessionId, Guid.NewGuid(), config.StoreId,
        config.TerminalId, "SHIFT-REVIEW", "cashier", "THB", 100m, 98m, -2m,
        DateTimeOffset.UtcNow, 2, status, reviewVersion,
        reviewVersion == 0 ? null : DateTimeOffset.UtcNow);

    internal static PosClientConfiguration Configuration() => new("http://localhost:8080/realms/nexa-dev", "nexaconnect-pos",
        "nexaconnect-pos://oauth/callback", "openid", "http://localhost:5225/", "http://localhost:5230/",
        Guid.NewGuid(), Guid.NewGuid(), "THB", "cash_manual", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "http://localhost:5268/");
    private static PosTokenSet Token() => new("synthetic", null, DateTimeOffset.UtcNow.AddMinutes(5), "Bearer");

    [Fact]
    public async Task Menu_uses_catalog_origin_with_authenticated_tenant_and_correlation()
    {
        var config = Configuration();
        var catalog = new Handler(request =>
        {
            Assert.Equal(5268, request.RequestUri!.Port);
            Assert.Equal($"/api/catalog/v1/branches/{config.BranchId:D}/menu-items", request.RequestUri.AbsolutePath);
            Assert.Equal(config.OrganizationId.ToString("D"), request.Headers.GetValues("X-Nexa-Organization-Id").Single());
            Assert.Equal("nexa_connect", request.Headers.GetValues("X-Nexa-Application-Code").Single());
            Assert.NotNull(request.Headers.Authorization);
            Assert.True(Guid.TryParse(request.Headers.GetValues("X-Correlation-ID").Single(), out _));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<PosMenuItem>()) });
        });
        using var api = new PosApiClient(config, catalogHandler: catalog);
        Assert.Empty(await api.GetMenuAsync(Token(), config.BranchId));
    }

    [Fact]
    public async Task Lost_placement_response_replays_identical_command_and_order_identity()
    {
        var config = Configuration();
        var checkout = PendingCheckout.Create(config, [new(Guid.NewGuid(), 2)]);
        string? first = null;
        int count = 0;
        var handler = new Handler(async request =>
        {
            string body = await request.Content!.ReadAsStringAsync();
            if (count++ == 0) { first = body; throw new HttpRequestException("Lost response"); }
            Assert.Equal(first, body);
            using var json = JsonDocument.Parse(body);
            Assert.Equal(checkout.OrderId.ToString("N"), json.RootElement.GetProperty("idempotencyKey").GetString());
            Assert.Equal(checkout.OrderId, json.RootElement.GetProperty("orderId").GetGuid());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new PosOrderResult(checkout.OrderId, PosOrderStatus.KitchenAccepted, 240m, "THB")) };
        });
        using var api = new PosApiClient(config, orderHandler: handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => api.PlaceOrderAsync(Token(), checkout));
        Assert.Equal(checkout.OrderId, (await api.PlaceOrderAsync(Token(), checkout)).OrderId);
    }

    [Fact]
    public async Task Changed_terminal_scope_is_rejected_before_network()
    {
        var config = Configuration();
        var checkout = PendingCheckout.Create(config, [new(Guid.NewGuid(), 1)]);
        using var api = new PosApiClient(config with { TerminalId = Guid.NewGuid() }, orderHandler: new Handler(_ => throw new Exception("Must not send")));
        await Assert.ThrowsAsync<InvalidDataException>(() => api.PlaceOrderAsync(Token(), checkout));
    }

    [Fact]
    public async Task Confirmed_terminal_rejection_is_returned_for_recovery_cleanup()
    {
        var config = Configuration();
        var checkout = PendingCheckout.Create(config, [new(Guid.NewGuid(), 1)]);
        using var api = new PosApiClient(config, orderHandler: new Handler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = JsonContent.Create(new PosOrderResult(checkout.OrderId, PosOrderStatus.Rejected, 95m, "THB"))
            })));

        PosOrderResult result = await api.PlaceOrderAsync(Token(), checkout);

        Assert.Equal(PosOrderStatus.Rejected, result.Status);
        Assert.Equal(checkout.OrderId, result.OrderId);
    }

    [Fact]
    public void Invalid_setup_is_rejected()
    {
        var config = Configuration();
        Assert.Throws<InvalidDataException>(() => (config with { Currency = "SGD" }).ValidateCheckout());
        Assert.Throws<InvalidDataException>(() => (config with { CatalogApi = "http://untrusted.example/" }).ValidateCheckout());
        Assert.Throws<InvalidDataException>(() => (config with { BranchId = Guid.Empty }).ValidateCheckout());
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
