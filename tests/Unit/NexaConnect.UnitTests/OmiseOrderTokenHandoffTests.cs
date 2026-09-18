using Microsoft.Extensions.Options;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Order.Application.Workflow;
using NexaConnect.Services.Order.Domain;
using System.Text.Json;

namespace NexaConnect.UnitTests;

public sealed class OmiseOrderTokenHandoffTests
{
    [Theory]
    [InlineData("tokn_test_aaaaaaaaaaaa\n")]
    [InlineData("pkey_test_aaaaaaaaaaaa")]
    [InlineData("skey_test_aaaaaaaaaaaa")]
    public async Task Card_input_rejects_keys_and_whitespace_before_persistence(string input)
    {
        var fixture = new Fixture();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Workflow.ExecuteAsync(fixture.Command with { CardToken = input }, default));
        Assert.Null(fixture.Order);
        Assert.Equal(0, fixture.PaymentCalls);
    }
    [Fact]
    public async Task Interrupted_tokenless_checkout_resumes_same_order_with_fresh_token_without_repeating_kitchen()
    {
        var fixture = new Fixture();
        var initial = await fixture.Workflow.ExecuteAsync(fixture.Command, default);
        Assert.Equal(OrderStatus.PaymentPending, initial.Status);
        Assert.True(initial.CardTokenRequired);
        var restarted = JsonSerializer.Deserialize<PlaceOrderCommand>(JsonSerializer.Serialize(fixture.Command))!;
        Assert.Null(restarted.CardToken);
        var paid = await fixture.Workflow.ExecuteAsync(restarted with { CardToken = "tokn_test_aaaaaaaaaaaa" }, default);
        Assert.Equal(initial.OrderId, paid.OrderId);
        Assert.Equal(OrderStatus.Paid, paid.Status);
        Assert.False(paid.CardTokenRequired);
        Assert.Equal(1, fixture.Reservations);
        Assert.Equal(1, fixture.Tickets);
        int calls = fixture.PaymentCalls;
        await fixture.Workflow.ExecuteAsync(restarted with { CardToken = "tokn_test_bbbbbbbbbbbb" }, default);
        Assert.Equal(calls, fixture.PaymentCalls);
        Assert.Single(fixture.Events.OfType<PaymentCompletedV1>());
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("branch")]
    [InlineData("lines")]
    public async Task Existing_card_checkout_rejects_changed_scope_or_contents_before_payment(string changed)
    {
        var fixture = new Fixture();
        await fixture.Workflow.ExecuteAsync(fixture.Command, default);
        var command = changed switch
        {
            "organization" => fixture.Command with { OrganizationId = Guid.NewGuid() },
            "branch" => fixture.Command with { BranchId = Guid.NewGuid() },
            _ => fixture.Command with { Lines = [new PlaceOrderLine(fixture.ProductId, 2)] }
        };
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Workflow.ExecuteAsync(command with { CardToken = "tokn_test_aaaaaaaaaaaa" }, default));
        Assert.Equal(1, fixture.PaymentCalls);
    }

    [Fact]
    public async Task Foreground_and_reconciliation_share_one_card_completion_event_identity()
    {
        var fixture = new Fixture();
        await fixture.Workflow.ExecuteAsync(fixture.Command with { CardToken = "tokn_test_aaaaaaaaaaaa" }, default);
        PaymentCompletedV1 foreground = Assert.Single(fixture.Events.OfType<PaymentCompletedV1>());
        fixture.RestorePendingSnapshot();
        var reconciliation = new PaymentReconciliationApplicationService(fixture, fixture, fixture, fixture, fixture);
        Assert.True(await reconciliation.ApplyAsync(new PaymentCaptureReconciledV1(Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UtcNow, fixture.Command.OrganizationId, fixture.Order!.Id,
            fixture.Order.PaymentIntentId!.Value, "captured", null), default));
        Assert.Equal(foreground.EventId, fixture.Events.OfType<PaymentCompletedV1>().Last().EventId);
    }

    [Fact]
    public async Task Disabled_card_checkout_and_invalid_tokens_fail_before_any_side_effect()
    {
        var fixture = new Fixture(false);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Workflow.ExecuteAsync(fixture.Command, default));
        Assert.Null(fixture.Order);
        fixture = new Fixture();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Workflow.ExecuteAsync(fixture.Command with { CardToken = "4242424242424242" }, default));
        Assert.Null(fixture.Order);
        Assert.DoesNotContain("tokn_test_aaaaaaaaaaaa", (fixture.Command with { CardToken = "tokn_test_aaaaaaaaaaaa" }).ToString());
    }

    private sealed class Fixture : IOrderRepository, IOrderLookup, IIdempotentOrderRepository, IMenuCatalogPort,
        IInventoryReservationPort, IKitchenPort, IPaymentPort, IIntegrationEventPublisher
    {
        public Guid ProductId { get; } = Guid.NewGuid();
        private readonly Guid paymentId = Guid.NewGuid();
        public OrderAggregate? Order { get; private set; }
        public int Reservations { get; private set; }
        public int Tickets { get; private set; }
        public int PaymentCalls { get; private set; }
        public List<IIntegrationEvent> Events { get; } = [];
        public PlaceOrderCommand Command { get; }
        public PlaceOrderWorkflow Workflow { get; }
        public Fixture(bool enabled = true)
        {
            Command = new(Guid.NewGuid(), Guid.NewGuid(), [new(ProductId, 1)], "THB", "card_omise_test",
                Guid.NewGuid(), "original-checkout", Guid.NewGuid());
            Workflow = new(this, this, this, this, this, this, cardCheckout: Options.Create(new CardCheckoutOptions { EnableOmiseTestCheckout = enabled }));
        }
        public Task SaveAsync(OrderAggregate order, CancellationToken ct) { Order = order; return Task.CompletedTask; }
        public Task<OrderAggregate?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult(Order);
        public void RestorePendingSnapshot()
        {
            var original = Order!;
            Order = OrderAggregate.Create(original.Id, original.OrganizationId, original.BranchId, original.Lines,
                original.Currency, original.RestaurantId, idempotencyKey: original.IdempotencyKey,
                workflowPaymentMethod: original.WorkflowPaymentMethod, workflowCorrelationId: original.WorkflowCorrelationId);
            Order.Submit(); Order.MarkInventoryReserved(); Order.MarkKitchenAccepted(); Order.MarkPaymentPending(paymentId);
        }
        public Task<OrderAggregate?> FindByIdempotencyKeyAsync(Guid restaurant, string key, CancellationToken ct) => Task.FromResult(Order);
        public Task<IReadOnlyDictionary<Guid, CatalogMenuItem>> GetItemsAsync(Guid branch, IReadOnlyCollection<Guid> products, CancellationToken ct)
            => Task.FromResult<IReadOnlyDictionary<Guid, CatalogMenuItem>>(new Dictionary<Guid, CatalogMenuItem> { [ProductId] = new(ProductId, "Test item", 50m, "THB", true, "kitchen") });
        public Task<InventoryReservationResult> ReserveAsync(Guid org, Guid order, Guid branch, IReadOnlyCollection<OrderLine> lines, CancellationToken ct)
        { Reservations++; return Task.FromResult(new InventoryReservationResult(true, Guid.NewGuid(), null)); }
        public Task<KitchenTicketResult> CreateTicketAsync(Guid org, Guid restaurant, Guid order, Guid branch, IReadOnlyCollection<OrderLine> lines, CancellationToken ct)
        { Tickets++; return Task.FromResult(new KitchenTicketResult(Guid.NewGuid())); }
        public Task<PaymentResult> AuthorizeAsync(Guid org, Guid restaurant, Guid branch, Guid order, decimal amount, string currency, string method, CancellationToken ct)
            => AuthorizeAsync(org, restaurant, branch, order, amount, currency, method, null, ct);
        public Task<PaymentResult> AuthorizeAsync(Guid org, Guid restaurant, Guid branch, Guid order, decimal amount, string currency, string method, string? token, CancellationToken ct)
        { PaymentCalls++; return Task.FromResult(token is null ? new PaymentResult(false, paymentId, "fresh_card_token_required", "awaiting_token") : new PaymentResult(true, paymentId, null, "captured")); }
        public Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken ct) { Events.Add(integrationEvent); return Task.CompletedTask; }
    }
}
