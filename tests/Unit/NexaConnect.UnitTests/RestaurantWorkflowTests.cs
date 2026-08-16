using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Infrastructure.Messaging;
using NexaConnect.Services.Order.Infrastructure.Messaging;
using NexaConnect.Services.Order.Application.Workflow;
using NexaConnect.Services.Order.Domain;

namespace NexaConnect.UnitTests;

public sealed class RestaurantWorkflowTests
{
    [Fact]
    public async Task Postgres_event_publisher_writes_versioned_event_to_outbox_port()
    {
        var store = new RecordingOutboxStore();
        var publisher = new PostgresIntegrationEventPublisher(store);
        Guid orderId = Guid.NewGuid();
        await publisher.PublishAsync(new PaymentCompletedV1(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
            orderId, Guid.NewGuid(), 12m, "USD", "cash"), CancellationToken.None);

        Assert.Equal("PaymentCompletedV1", store.Message!.EventType);
        Assert.Equal(1, store.Message.ContractVersion);
        Assert.Equal(orderId, store.Message.AggregateId);
    }
    [Fact]
    public async Task PlaceOrder_runs_catalog_inventory_kitchen_payment_and_publishes_versioned_events()
    {
        Guid productId = Guid.NewGuid();
        var catalog = new FakeCatalog(new CatalogMenuItem(productId, "Burger", 12.50m, "USD", true, "grill"));
        var inventory = new FakeInventory(true);
        var kitchen = new FakeKitchen();
        var payment = new FakePayment(true);
        var repository = new InMemoryOrderRepository();
        var events = new RecordingPublisher();
        var workflow = new PlaceOrderWorkflow(catalog, inventory, kitchen, payment, repository, events);

        PlaceOrderResult result = await workflow.ExecuteAsync(new PlaceOrderCommand(
            Guid.NewGuid(), Guid.NewGuid(), [new PlaceOrderLine(productId, 2)], "USD", "cash"),
            CancellationToken.None);

        Assert.Equal(OrderStatus.Paid, result.Status);
        Assert.Equal(25m, result.TotalAmount);
        Assert.Equal(["OrderSubmittedV1", "InventoryReservedV1", "KitchenTicketCreatedV1", "PaymentCompletedV1"],
            events.Events.Select(@event => @event.GetType().Name));
        Assert.Equal(result.OrderId, repository.Last!.Id);
        Assert.Equal(OrderStatus.Paid, repository.Last.Status);
        Assert.Equal(1, inventory.Calls);
        Assert.Equal(1, kitchen.Calls);
        Assert.Equal(1, payment.Calls);
    }

    [Fact]
    public async Task PlaceOrder_rejects_when_inventory_cannot_be_reserved_and_does_not_charge_or_route()
    {
        Guid productId = Guid.NewGuid();
        var events = new RecordingPublisher();
        var repository = new InMemoryOrderRepository();
        var workflow = new PlaceOrderWorkflow(
            new FakeCatalog(new CatalogMenuItem(productId, "Soup", 5m, "USD", true, "hot-line")),
            new FakeInventory(false), new FakeKitchen(), new FakePayment(true), repository, events);

        PlaceOrderResult result = await workflow.ExecuteAsync(new PlaceOrderCommand(
            Guid.NewGuid(), Guid.NewGuid(), [new PlaceOrderLine(productId, 1)], "USD", "cash"),
            CancellationToken.None);

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Contains(events.Events, @event => @event is InventoryReservationRejectedV1);
        Assert.DoesNotContain(events.Events, @event => @event is PaymentCompletedV1);
    }

    [Fact]
    public async Task PlaceOrder_records_payment_failure_after_kitchen_acceptance()
    {
        Guid productId = Guid.NewGuid();
        var events = new RecordingPublisher();
        var repository = new InMemoryOrderRepository();
        var workflow = new PlaceOrderWorkflow(
            new FakeCatalog(new CatalogMenuItem(productId, "Pasta", 15m, "USD", true, "kitchen")),
            new FakeInventory(true), new FakeKitchen(), new FakePayment(false), repository, events);

        PlaceOrderResult result = await workflow.ExecuteAsync(new PlaceOrderCommand(
            Guid.NewGuid(), Guid.NewGuid(), [new PlaceOrderLine(productId, 1)], "USD", "card"),
            CancellationToken.None);

        Assert.Equal(OrderStatus.PaymentFailed, result.Status);
        Assert.Contains(events.Events, @event => @event is PaymentFailedV1);
    }

    [Fact]
    public void Order_aggregate_rejects_invalid_transition()
    {
        var order = OrderAggregate.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            [new OrderLine(Guid.NewGuid(), "Tea", 2m, 1, "bar")], "USD");

        Assert.Throws<InvalidOperationException>(() => order.MarkPaid());
    }

    private sealed class FakeCatalog(params CatalogMenuItem[] items) : IMenuCatalogPort
    {
        private readonly IReadOnlyDictionary<Guid, CatalogMenuItem> values = items.ToDictionary(item => item.ProductId);

        public Task<IReadOnlyDictionary<Guid, CatalogMenuItem>> GetItemsAsync(
            Guid branchId, IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, CatalogMenuItem>>(
                values.Where(pair => productIds.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value));
    }

    private sealed class FakeInventory(bool reserved) : IInventoryReservationPort
    {
        public int Calls { get; private set; }

        public Task<InventoryReservationResult> ReserveAsync(
            Guid orderId, Guid branchId, IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(reserved
                ? new InventoryReservationResult(true, Guid.NewGuid(), null)
                : new InventoryReservationResult(false, null, "insufficient stock"));
        }
    }

    private sealed class FakeKitchen : IKitchenPort
    {
        public int Calls { get; private set; }

        public Task<KitchenTicketResult> CreateTicketAsync(
            Guid orderId, Guid branchId, IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new KitchenTicketResult(Guid.NewGuid()));
        }
    }

    private sealed class FakePayment(bool completed) : IPaymentPort
    {
        public int Calls { get; private set; }

        public Task<PaymentResult> AuthorizeAsync(
            Guid organizationId, Guid restaurantId, Guid branchId, Guid orderId, decimal amount, string currency,
            string method, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(completed
                ? new PaymentResult(true, Guid.NewGuid(), null)
                : new PaymentResult(false, null, "declined"));
        }
    }

    private sealed class InMemoryOrderRepository : IOrderRepository
    {
        public OrderAggregate? Last { get; private set; }

        public Task SaveAsync(OrderAggregate order, CancellationToken cancellationToken)
        {
            Last = order;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPublisher : IIntegrationEventPublisher
    {
        public List<IIntegrationEvent> Events { get; } = [];

        public Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            Events.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingOutboxStore : IOutboxStore
    {
        public OutboxMessage? Message { get; private set; }

        public Task EnqueueAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            Message = message;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(int batchSize, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OutboxMessage>>([]);
        public Task MarkPublishedAsync(Guid messageId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task MarkFailedAsync(Guid messageId, string category, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
