using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Order.Application.Orders;
using NexaConnect.Services.Order.Application.Workflow;
using NexaConnect.Services.Order.Domain;

namespace NexaConnect.UnitTests;

public sealed class OrderPricingTests
{
    [Theory]
    [InlineData(100, 7, false, 10, 100, 10, 7.7, 117.7)]
    [InlineData(107, 7, true, 10, 100, 10, 7.7, 117.7)]
    [InlineData(0.05, 7, true, 0, 0.05, 0, 0, 0.05)]
    [InlineData(0.05, 0, false, 10, 0.05, 0.01, 0, 0.06)]
    [InlineData(100, 0, false, 0, 100, 0, 0, 100)]
    public void Pricing_has_exact_components_and_preserves_inclusive_menu_amount(decimal price, decimal tax, bool inclusive,
        decimal service, decimal subtotal, decimal serviceAmount, decimal taxAmount, decimal total)
    {
        var value = OrderPricing.Calculate([new(Guid.NewGuid(), "Item", price, 1, "kitchen")], "THB", new(1, tax, inclusive, service));
        Assert.Equal(subtotal, value.SubtotalAmount);
        Assert.Equal(serviceAmount, value.ServiceChargeAmount);
        Assert.Equal(taxAmount, value.TaxAmount);
        Assert.Equal(total, value.TotalAmount);
        Assert.Equal(total, value.SubtotalAmount + value.ServiceChargeAmount + value.TaxAmount);
    }

    [Theory]
    [InlineData(-1, 0)] [InlineData(101, 0)] [InlineData(7.001, 0)] [InlineData(7, 100.01)]
    public void Invalid_rates_fail_closed(decimal tax, decimal service) =>
        Assert.Throws<ArgumentException>(() => OrderPricing.Calculate([new(Guid.NewGuid(), "Item", 100, 1, "kitchen")], "THB", new(1, tax, false, service)));

    [Fact]
    public async Task Changed_prices_require_reconfirmation_before_any_side_effect()
    {
        var fixture = new Fixture();
        var command = fixture.Command;
        var quote = await fixture.Pricing.QuoteAsync(command, default);
        fixture.Price = 200;
        await Assert.ThrowsAsync<PricingChangedException>(() => fixture.Workflow.ExecuteAsync(command with { PricingFingerprint = quote.Fingerprint }, default));
        Assert.Equal(0, fixture.Reservations);
        Assert.Null(await fixture.Store.FindByIdempotencyKeyAsync(command.RestaurantId!.Value, command.IdempotencyKey!, default));
    }

    [Fact]
    public async Task Accepted_order_replay_uses_snapshot_after_policy_and_catalog_changes()
    {
        var fixture = new Fixture();
        var quote = await fixture.Pricing.QuoteAsync(fixture.Command, default);
        var command = fixture.Command with { PricingFingerprint = quote.Fingerprint };
        var first = await fixture.Workflow.ExecuteAsync(command, default);
        fixture.Price = 999; fixture.Policy = new(2, 20, true, 50);
        var replay = await fixture.Workflow.ExecuteAsync(command, default);
        Assert.Equal(117.70m, first.TotalAmount);
        Assert.Equal(first, replay);
        Assert.Equal(1, fixture.Reservations);
        Assert.Equal(quote.Pricing, fixture.Store.Get(first.OrderId)!.Pricing);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Workflow.ExecuteAsync(command with { OrganizationId = Guid.NewGuid() }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Workflow.ExecuteAsync(command with { Lines = [new(fixture.ProductId, 2)] }, default));
    }

    [Fact]
    public async Task Provider_receives_authoritative_total()
    {
        var fixture = new Fixture();
        var command = fixture.Command with { PaymentMethod = "card" };
        var quote = await fixture.Pricing.QuoteAsync(command, default);
        var result = await fixture.Workflow.ExecuteAsync(command with { PricingFingerprint = quote.Fingerprint }, default);
        Assert.Equal(OrderStatus.Paid, result.Status);
        Assert.Equal(117.70m, fixture.AuthorizedAmount);
    }

    [Fact]
    public async Task Quote_identity_covers_policy_scope_and_menu_snapshot()
    {
        var fixture = new Fixture();
        var first = await fixture.Pricing.QuoteAsync(fixture.Command, default);
        fixture.Policy = fixture.Policy with { Version = 2 };
        Assert.NotEqual(first.Fingerprint, (await fixture.Pricing.QuoteAsync(fixture.Command, default)).Fingerprint);
        Assert.NotEqual(first.Fingerprint, (await fixture.Pricing.QuoteAsync(fixture.Command with { BranchId = Guid.NewGuid() }, default)).Fingerprint);
    }

    [Fact]
    public void Rehydration_rejects_tampered_total()
    {
        OrderLine[] lines = [new(Guid.NewGuid(), "Item", 100, 1, "kitchen")];
        var pricing = OrderPricing.Calculate(lines, "THB", new(1, 7, false, 10));
        Assert.Throws<ArgumentException>(() => OrderAggregate.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), lines,
            "THB", pricing: pricing with { TotalAmount = 1 }, pricingFingerprint: new string('A', 64)));
    }

    private sealed class Fixture : IMenuCatalogPort, IBranchPricingPort, IInventoryReservationPort, IKitchenPort, IPaymentPort, IIntegrationEventPublisher
    {
        public Guid ProductId { get; } = Guid.NewGuid();
        public decimal Price = 100;
        public PricingPolicy Policy = new(1, 7, false, 10);
        public int Reservations;
        public decimal AuthorizedAmount;
        public InMemoryOrderApplicationService Store { get; } = new();
        public OrderPricingService Pricing { get; }
        public PlaceOrderWorkflow Workflow { get; }
        public PlaceOrderCommand Command { get; }
        public Fixture()
        {
            Command = new(Guid.NewGuid(), Guid.NewGuid(), [new(ProductId, 1)], "THB", "cash_manual", Guid.NewGuid(), "checkout", Guid.NewGuid());
            Pricing = new(this, this);
            Workflow = new(this, this, this, this, Store, this, pricingService: Pricing);
        }
        public Task<PricingPolicy> GetAsync(Guid o, Guid r, Guid b, CancellationToken c) => Task.FromResult(Policy);
        public Task<IReadOnlyDictionary<Guid, CatalogMenuItem>> GetItemsAsync(Guid b, IReadOnlyCollection<Guid> ids, CancellationToken c)
            => Task.FromResult<IReadOnlyDictionary<Guid, CatalogMenuItem>>(new Dictionary<Guid, CatalogMenuItem> { [ProductId] = new(ProductId, "Item", Price, "THB", true, "kitchen") });
        public Task<InventoryReservationResult> ReserveAsync(Guid o, Guid id, Guid b, IReadOnlyCollection<OrderLine> l, CancellationToken c)
        { Reservations++; return Task.FromResult(new InventoryReservationResult(true, Guid.NewGuid(), null)); }
        public Task<KitchenTicketResult> CreateTicketAsync(Guid o, Guid r, Guid id, Guid b, IReadOnlyCollection<OrderLine> l, CancellationToken c)
            => Task.FromResult(new KitchenTicketResult(Guid.NewGuid()));
        public Task<PaymentResult> AuthorizeAsync(Guid o, Guid r, Guid b, Guid id, decimal amount, string currency, string method, CancellationToken c)
        { AuthorizedAmount = amount; return Task.FromResult(new PaymentResult(true, Guid.NewGuid(), null)); }
        public Task PublishAsync(IIntegrationEvent e, CancellationToken c) => Task.CompletedTask;
    }
}
