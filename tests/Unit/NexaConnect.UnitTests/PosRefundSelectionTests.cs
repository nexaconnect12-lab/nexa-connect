using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using NexaConnect.POS;

namespace NexaConnect.UnitTests;

public sealed class PosRefundSelectionTests
{
    [Fact]
    public async Task Existing_omise_adapter_mapping_keeps_the_original_card_receipt_selectable()
    {
        using var fixture = new Fixture();
        fixture.Documents["intent"]["paymentMethod"] = "card";
        var payment = await fixture.Read();
        Assert.Equal(fixture.IntentId, payment.PaymentIntentId);
        Assert.Equal(100, payment.AvailableAmount);
        Assert.Equal(4, fixture.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Selection_binds_original_receipt_and_reservations_with_reads_only(bool byOrder)
    {
        using var fixture = new Fixture();
        fixture.Refunds = [fixture.Refund("processing", 5), fixture.Refund("refund_unknown", 6),
            fixture.Refund("review_required", 7), fixture.Refund("completed", 8), fixture.Refund("failed", 40)];
        var selected = await fixture.Read(byOrder);
        Assert.Equal(fixture.IntentId, selected.PaymentIntentId);
        Assert.Equal(fixture.OrderId, selected.OrderId);
        Assert.Equal(74, selected.AvailableAmount);
        Assert.Equal(100, selected.CapturedAmount);
        Assert.Contains("ORDER-42", selected.Label);
        Assert.Contains("THB 100", selected.Label);
        Assert.Equal("1 × Noodle soup", selected.Items);
        Assert.Equal($"R-{fixture.OrderId:N}".ToUpperInvariant(), selected.ReceiptNumber);
        Assert.Equal(4, fixture.Calls);
    }

    [Theory]
    [InlineData("intent", "organizationId")]
    [InlineData("intent", "restaurantId")]
    [InlineData("intent", "branchId")]
    [InlineData("intent", "id")]
    [InlineData("order", "orderId")]
    [InlineData("order", "organizationId")]
    [InlineData("order", "branchId")]
    [InlineData("order", "paymentIntentId")]
    [InlineData("receipt", "orderId")]
    [InlineData("receipt", "branchId")]
    public async Task Foreign_or_mismatched_identity_never_becomes_selectable(string resource, string property)
    {
        using var fixture = new Fixture();
        fixture.Documents[resource][property] = Guid.NewGuid().ToString();
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Read());
    }

    [Theory]
    [InlineData("intent", "status", "authorized")]
    [InlineData("intent", "currency", "SGD")]
    [InlineData("intent", "paymentMethod", "promptpay_manual")]
    [InlineData("order", "status", "KitchenAccepted")]
    [InlineData("order", "currency", "SGD")]
    [InlineData("receipt", "tender", "cash")]
    [InlineData("receipt", "orderNumber", "bad\nlabel")]
    public async Task Unconfirmed_or_inconsistent_payment_is_rejected(string resource, string property, string value)
    {
        using var fixture = new Fixture();
        fixture.Documents[resource][property] = value;
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Read());
    }

    [Fact]
    public async Task Manual_payment_has_no_provider_intent_and_stops_after_order_read()
    {
        using var fixture = new Fixture();
        fixture.Documents["order"]["paymentIntentId"] = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Read());
        Assert.Equal(1, fixture.Calls);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("foreign")]
    [InlineData("over-refunded")]
    [InlineData("unknown-status")]
    [InlineData("empty-operation")]
    [InlineData("too-many")]
    public async Task Invalid_or_incomplete_history_fails_closed(string scenario)
    {
        using var fixture = new Fixture();
        var refund = fixture.Refund("processing", 10);
        fixture.Refunds = scenario switch
        {
            "duplicate" => [refund, refund],
            "foreign" => [refund with { OrderId = Guid.NewGuid() }],
            "over-refunded" => [refund with { Amount = 101 }],
            "unknown-status" => [refund with { Status = "new-status" }],
            "empty-operation" => [refund with { OperationId = Guid.Empty }],
            _ => Enumerable.Range(0, 1001).Select(_ => fixture.Refund("processing", .01m)).ToArray()
        };
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Read());
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(503)]
    public async Task Read_failure_does_not_issue_a_refund(int status)
    {
        using var fixture = new Fixture { Failure = (HttpStatusCode)status };
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Read());
        Assert.Equal(1, fixture.Calls);
    }

    [Fact]
    public async Task Cancellation_aborts_discovery_without_command()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Api.FindRefundPaymentAsync(
            fixture.Token, fixture.OrderId, true, cancellation.Token));
        Assert.Equal(0, fixture.Calls);
    }

    private sealed class Fixture : IDisposable
    {
        public Guid IntentId { get; } = Guid.NewGuid();
        public Guid OrderId { get; } = Guid.NewGuid();
        public PosClientConfiguration Configuration { get; } = PosCheckoutIntegrationTests.Configuration() with { PaymentApi = "http://localhost:5235/" };
        public PosTokenSet Token { get; } = new("synthetic-not-a-token", null, DateTimeOffset.UtcNow.AddMinutes(5), "Bearer");
        public Dictionary<string, JsonObject> Documents { get; } = [];
        public PosPaymentRefund[] Refunds { get; set; } = [];
        public HttpStatusCode? Failure { get; init; }
        public int Calls;
        public PosApiClient Api { get; }
        public Fixture()
        {
            Documents["order"] = JsonSerializer.SerializeToNode(new { orderId = OrderId, Configuration.OrganizationId,
                Configuration.BranchId, status = "Paid", totalAmount = 100m, currency = "THB", paymentIntentId = IntentId }, JsonSerializerOptions.Web)!.AsObject();
            Documents["intent"] = JsonSerializer.SerializeToNode(new { id = IntentId, Configuration.OrganizationId,
                Configuration.RestaurantId, Configuration.BranchId, orderId = OrderId, amount = 100m,
                currency = "THB", paymentMethod = "card_omise_test", status = "captured" }, JsonSerializerOptions.Web)!.AsObject();
            Documents["receipt"] = JsonSerializer.SerializeToNode(new PosReceipt(1, $"R-{OrderId:N}".ToUpperInvariant(),
                OrderId, Configuration.OrganizationId, Configuration.RestaurantId, Configuration.BranchId,
                "ORDER-42", DateTimeOffset.UtcNow, "THB", "card_omise_test", [new(Guid.NewGuid(), "Noodle soup", 100, 1, 100)],
                null, 100, 0, 0, 100), JsonSerializerOptions.Web)!.AsObject();
            Api = new(Configuration, orderHandler: new Handler(this), paymentHandler: new Handler(this));
        }
        public Task<PosRefundPayment> Read(bool byOrder = true) => Api.FindRefundPaymentAsync(Token, byOrder ? OrderId : IntentId, byOrder);
        public PosPaymentRefund Refund(string status, decimal amount)
        {
            Guid id = Guid.NewGuid();
            return new(id, Configuration.OrganizationId, Configuration.RestaurantId, Configuration.BranchId, OrderId,
                IntentId, Guid.NewGuid(), amount, "THB", "customer_request", status, DateTimeOffset.UtcNow,
                status == "completed" ? DateTimeOffset.UtcNow : null, null, status == "completed" ? new(
                    $"RF-{id:N}".ToUpperInvariant(), id, IntentId, OrderId, amount, "THB", "customer_request",
                    DateTimeOffset.UtcNow, 100, amount) : null);
        }
        public void Dispose() => Api.Dispose();
        private sealed class Handler(Fixture fixture) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested(); fixture.Calls++;
                Assert.Equal(HttpMethod.Get, request.Method); // Selection cannot capture, place or refund.
                Assert.Equal(fixture.Token.AccessToken, request.Headers.Authorization!.Parameter);
                Assert.Equal(fixture.Configuration.OrganizationId.ToString("D"), request.Headers.GetValues("X-Nexa-Organization-Id").Single());
                Assert.True(Guid.TryParse(request.Headers.GetValues("X-Correlation-ID").Single(), out _));
                if (fixture.Failure is {} failure) return Task.FromResult(new HttpResponseMessage(failure));
                string path = request.RequestUri!.AbsolutePath;
                object value = path.EndsWith("/refunds") ? fixture.Refunds : fixture.Documents[
                    path.EndsWith("/receipt") ? "receipt" : path.Contains("/intents/") ? "intent" : "order"];
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(value) });
            }
        }
    }
}
