using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NexaConnect.POS;

namespace NexaConnect.UnitTests;

public sealed class CustomerDisplayTests
{
    private static PosOrderPricing Pricing => new(1, 7, false, 10, 100, 100, 10, 7.7m, 117.7m);
    private static PosOrderQuote Quote => new(new string('A', 64), Pricing, [new(Guid.NewGuid(), "Meal", 50, 2)]);

    [Fact]
    public void Cart_is_a_detached_estimate_and_quote_uses_exact_server_breakdown()
    {
        var state = new CustomerDisplayState();
        var cart = new List<CustomerDisplayLine> { new("Meal", 2, 40) };
        state.Cart("THB", cart); cart.Clear();
        var estimate = state.Visible(true, false);
        Assert.Equal(80m, estimate.Total); Assert.Equal("Cart estimate", estimate.TotalLabel);
        Assert.Null(estimate.Tax); Assert.Single(estimate.Lines);
        state.Quote(Quote, "THB");
        var quote = state.Visible(true, false);
        Assert.Equal(117.7m, quote.Total); Assert.Equal(100m, quote.Subtotal);
        Assert.Equal(10m, quote.ServiceCharge); Assert.Equal(7.7m, quote.Tax);
        Assert.Equal(50m, quote.Lines[0].UnitPrice); Assert.Equal("Quoted total", quote.TotalLabel);
    }

    [Theory]
    [InlineData(PosOrderStatus.KitchenAccepted)]
    [InlineData(PosOrderStatus.PaymentPending)]
    [InlineData(PosOrderStatus.PaymentReview)]
    [InlineData(PosOrderStatus.CancellationPending)]
    [InlineData(PosOrderStatus.CancellationReview)]
    public void Intermediate_states_never_confirm_payment(PosOrderStatus status)
    {
        var state = new CustomerDisplayState(); state.Quote(Quote, "THB");
        var order = new PosOrderResult(Guid.NewGuid(), status, 117.7m, "THB", Pricing: Pricing);
        string original = JsonSerializer.Serialize(order);
        state.Order(order, "promptpay_manual");
        Assert.NotEqual("Payment confirmed", state.Visible(true, false).Heading);
        Assert.Equal(117.7m, state.Visible(true, false).Total);
        Assert.Equal("PromptPay (manual confirmation)", state.Visible(true, false).Tender);
        state.Unconfirmed(); state.CancellationPending();
        Assert.DoesNotContain("complete", state.Visible(true, false).Message);
        Assert.Equal(original, JsonSerializer.Serialize(order)); // Projection never mutates its source.
    }

    [Fact]
    public void Lock_masks_current_and_inflight_updates_then_original_resume_reads_latest_projection()
    {
        var state = new CustomerDisplayState(); state.Quote(Quote, "THB");
        Assert.Equal(CustomerDisplaySnapshot.Paused, state.Visible(true, true));
        state.Order(new(Guid.NewGuid(), PosOrderStatus.KitchenAccepted, 117.7m, "THB", Pricing: Pricing), "cash");
        Assert.Empty(state.Visible(true, true).Lines); Assert.Null(state.Visible(true, true).Total);
        Assert.Equal("Payment pending", state.Visible(true, false).Heading);
        state.Complete(); Assert.Equal(CustomerDisplaySnapshot.Paused, state.Visible(true, true));
        Assert.Equal("Payment confirmed", state.Visible(true, false).Heading);
        Assert.Equal(CustomerDisplaySnapshot.Welcome, state.Visible(false, false));
    }

    [Theory]
    [InlineData(PosOrderStatus.Paid, "Payment confirmed")]
    [InlineData(PosOrderStatus.Cancelled, "Order cancelled")]
    [InlineData(PosOrderStatus.PaymentFailed, "Order not completed")]
    [InlineData(PosOrderStatus.Rejected, "Order not completed")]
    public void Terminal_outcomes_remove_all_transaction_details(PosOrderStatus status, string heading)
    {
        var state = new CustomerDisplayState(); state.Quote(Quote, "THB");
        state.Order(new(Guid.NewGuid(), status, 117.7m, "THB"), "cash");
        var snapshot = state.Visible(true, false);
        Assert.Equal(heading, snapshot.Heading); Assert.Empty(snapshot.Lines);
        Assert.Null(snapshot.Total); Assert.Null(snapshot.Subtotal); Assert.Null(snapshot.Tax);
        Assert.Empty(snapshot.Tender); Assert.Empty(snapshot.Currency);
        state.Clear(); Assert.Equal(CustomerDisplaySnapshot.Welcome, state.Visible(true, false));
    }

    [Fact]
    public void Restart_projection_never_reconstructs_lines_or_treats_retained_pending_as_paid()
    {
        var state = new CustomerDisplayState();
        state.Order(new(Guid.NewGuid(), PosOrderStatus.KitchenAccepted, 117.7m, "THB", Pricing: Pricing), "cash");
        state.Unconfirmed();
        Assert.Empty(state.Visible(true, false).Lines);
        Assert.Contains("verification", state.Visible(true, false).Message);
        Assert.Equal(CustomerDisplaySnapshot.Welcome, new CustomerDisplayState().Visible(true, false));
        state.PricingChanged(); Assert.Null(state.Visible(true, false).Total);
    }

    [Fact]
    public void Customer_contract_is_an_explicit_data_allow_list_without_commands_or_identifiers()
    {
        Assert.Equal(new[] { "Currency", "Heading", "Lines", "Message", "ServiceCharge", "Subtotal", "Tax", "Tender", "Total", "TotalLabel" },
            typeof(CustomerDisplaySnapshot).GetProperties().Where(p => p.GetMethod is { IsStatic: false }).Select(p => p.Name).Order().ToArray());
        Assert.Equal(new[] { "LineTotal", "Name", "Quantity", "UnitPrice" }, typeof(CustomerDisplayLine).GetProperties().Select(p => p.Name).Order().ToArray());
        Assert.All(typeof(CustomerDisplayState).GetConstructors(), c => Assert.Empty(c.GetParameters()));
    }

    [Theory]
    [InlineData("status")]
    [InlineData("order")]
    [InlineData("settlement")]
    [InlineData("method")]
    [InlineData("amount")]
    [InlineData("currency")]
    public async Task Unmatched_success_response_retains_original_recovery_and_cannot_confirm_paid(string mismatch)
    {
        var orderId = Guid.NewGuid(); var idempotency = Guid.NewGuid();
        var valid = new ManualTenderResult(Guid.NewGuid(), orderId, "Paid", "cash", 117.7m, "THB", DateTimeOffset.UtcNow, false);
        var invalid = mismatch switch {
            "status" => valid with { Status = "Pending" }, "order" => valid with { OrderId = Guid.NewGuid() },
            "settlement" => valid with { SettlementId = Guid.Empty }, "method" => valid with { Method = "promptpay_manual" },
            "amount" => valid with { Amount = 1 }, _ => valid with { Currency = "SGD" }
        };
        int calls = 0; bool cleared = false; LocalPendingSettlementState? saved = null;
        using var api = new PosApiClient(PosCheckoutIntegrationTests.Configuration(), orderHandler: new Handler(request => {
            calls++; Assert.Equal(orderId.ToString("D"), request.Headers.GetValues("X-Correlation-ID").Single());
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(invalid) };
        }));
        var attempt = new LocalPendingSettlementState(orderId, 117.7m, "THB", idempotency, "cash");
        await Assert.ThrowsAsync<InvalidDataException>(() => SettlementAttempt.ExecuteAsync(attempt, s => saved = s,
            () => api.ConfirmManualSettlementAsync(new("synthetic", null, DateTimeOffset.UtcNow.AddMinutes(5), "Bearer"),
                orderId, idempotency, "cash", 117.7m, "THB", false, null), () => cleared = true));
        Assert.False(cleared); Assert.True(saved!.OutcomeUncertain); Assert.Equal(idempotency, saved.IdempotencyKey);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Valid_manual_confirmation_and_replay_clear_once_without_a_second_send(bool replayed)
    {
        var order = Guid.NewGuid(); int calls = 0, clears = 0;
        using var api = new PosApiClient(PosCheckoutIntegrationTests.Configuration(), orderHandler: new Handler(_ => {
            calls++; return new(HttpStatusCode.OK) { Content = JsonContent.Create(new ManualTenderResult(Guid.NewGuid(), order, "Paid", "cash", 10, "THB", DateTimeOffset.UtcNow, replayed)) };
        }));
        var result = await SettlementAttempt.ExecuteAsync(new(order, 10, "THB", Guid.NewGuid(), "cash"), _ => { },
            () => api.ConfirmManualSettlementAsync(new("synthetic", null, DateTimeOffset.UtcNow.AddMinutes(5), "Bearer"), order, Guid.NewGuid(), "cash", 10, "THB", false, null), () => clears++);
        Assert.Equal("Paid", result.Status); Assert.Equal(replayed, result.Replayed); Assert.Equal(1, calls); Assert.Equal(1, clears);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request));
    }
}
