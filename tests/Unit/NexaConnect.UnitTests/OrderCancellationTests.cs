using NexaConnect.Services.Order.Application.Cancellations;
using NexaConnect.Services.Order.Application.Orders;
using NexaConnect.Services.Order.Application.Workflow;
using NexaConnect.Services.Order.Domain;

namespace NexaConnect.UnitTests;

public sealed class OrderCancellationTests
{
    [Fact]
    public async Task Kitchen_accepted_order_cancels_once_and_replay_does_not_repeat_compensation()
    {
        var fixture = Create(OrderStatus.KitchenAccepted);
        Guid operation = Guid.NewGuid();
        var command = Command(fixture.Order, operation, "Customer changed order");

        OrderCancellationResult first = (await fixture.Service.RequestAsync(command, default))!;
        OrderCancellationResult replay = (await fixture.Service.RequestAsync(command, default))!;

        Assert.Equal("completed", first.Status);
        Assert.Equal("completed", replay.Status);
        Assert.True(replay.Replayed);
        Assert.Equal(OrderStatus.Cancelled, fixture.Order.Status);
        Assert.Equal(1, fixture.Inventory.ReleaseCalls);
        Assert.Equal(1, fixture.Kitchen.CancelCalls);
    }

    [Fact]
    public async Task Dependency_failure_retains_pending_request_and_worker_retries_same_identity()
    {
        var fixture = Create(OrderStatus.KitchenAccepted, failInventoryOnce: true);
        Guid operation = Guid.NewGuid();

        OrderCancellationResult pending = (await fixture.Service.RequestAsync(
            Command(fixture.Order, operation, "Entered twice"), default))!;
        Assert.Equal("pending", pending.Status);
        Assert.Equal(OrderStatus.CancellationPending, fixture.Order.Status);

        Assert.True(await fixture.Service.RecoverNextAsync(TimeSpan.FromSeconds(30), TimeSpan.Zero, default));
        Assert.Equal(OrderStatus.Cancelled, fixture.Order.Status);
        Assert.Equal(2, fixture.Inventory.ReleaseCalls);
        Assert.Equal(2, fixture.Kitchen.CancelCalls);
        Assert.Equal(operation, (await fixture.Repository.GetAsync(fixture.Order.Id, default))!.OperationId);
    }

    [Fact]
    public async Task Terminal_kitchen_state_blocks_inventory_release_and_requires_review()
    {
        var fixture = Create(OrderStatus.KitchenAccepted, kitchenConflict: true);

        OrderCancellationResult result = (await fixture.Service.RequestAsync(
            Command(fixture.Order, Guid.NewGuid(), "Guest left"), default))!;

        Assert.Equal("blocked", result.Status);
        Assert.Equal(OrderStatus.CancellationReview, fixture.Order.Status);
        Assert.Equal(0, fixture.Inventory.ReleaseCalls);
        Assert.Equal(1, fixture.Kitchen.CancelCalls);
    }

    [Theory]
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.PaymentPending)]
    [InlineData(OrderStatus.PaymentFailed)]
    public async Task Financial_states_cannot_enter_cancellation(OrderStatus status)
    {
        var fixture = Create(status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.RequestAsync(
            Command(fixture.Order, Guid.NewGuid(), "Invalid cancellation"), default));
        Assert.Equal(0, fixture.Inventory.ReleaseCalls);
        Assert.Equal(0, fixture.Kitchen.CancelCalls);
    }

    private static CancelOrderCommand Command(OrderAggregate order, Guid operation, string reason) =>
        new(order.Id, order.OrganizationId, order.BranchId, operation, reason, "cashier-subject",
            Guid.NewGuid(), operation);

    private static Fixture Create(OrderStatus target, bool failInventoryOnce = false, bool kitchenConflict = false)
    {
        var application = new InMemoryOrderApplicationService();
        var order = OrderAggregate.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            [new OrderLine(Guid.NewGuid(), "Rice", 80, 1, "kitchen")], "THB", Guid.NewGuid(),
            workflowPaymentMethod: "cash_manual", workflowCorrelationId: Guid.NewGuid());
        if (target != OrderStatus.Draft) order.Submit();
        if (target is OrderStatus.InventoryReserved or OrderStatus.KitchenAccepted or OrderStatus.Paid
            or OrderStatus.PaymentPending or OrderStatus.PaymentFailed) order.MarkInventoryReserved();
        if (target is OrderStatus.KitchenAccepted or OrderStatus.Paid or OrderStatus.PaymentPending
            or OrderStatus.PaymentFailed) order.MarkKitchenAccepted();
        if (target == OrderStatus.Paid) order.MarkPaid();
        if (target == OrderStatus.PaymentPending) order.MarkPaymentPending(Guid.NewGuid());
        if (target == OrderStatus.PaymentFailed) order.MarkPaymentFailed();
        application.SaveAsync(order, default).GetAwaiter().GetResult();
        var repository = new InMemoryOrderCancellationRepository(application);
        var inventory = new Inventory(failInventoryOnce);
        var kitchen = new Kitchen(kitchenConflict);
        return new(order, repository, inventory, kitchen,
            new OrderCancellationApplicationService(repository, inventory, kitchen));
    }

    private sealed record Fixture(OrderAggregate Order, InMemoryOrderCancellationRepository Repository,
        Inventory Inventory, Kitchen Kitchen, OrderCancellationApplicationService Service);

    private sealed class Inventory(bool failOnce) : IInventoryReservationPort
    {
        private bool fail = failOnce;
        public int ReleaseCalls { get; private set; }
        public Task<InventoryReservationResult> ReserveAsync(Guid organizationId, Guid orderId, Guid branchId,
            IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReleaseAsync(Guid organizationId, Guid orderId, Guid branchId, CancellationToken cancellationToken)
        {
            ReleaseCalls++;
            if (fail) { fail = false; throw new HttpRequestException("temporary"); }
            return Task.CompletedTask;
        }
    }

    private sealed class Kitchen(bool conflict) : IKitchenPort
    {
        public int CancelCalls { get; private set; }
        public Task<KitchenTicketResult> CreateTicketAsync(Guid organizationId, Guid restaurantId, Guid orderId,
            Guid branchId, IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CancelTicketAsync(Guid organizationId, Guid orderId, Guid branchId, CancellationToken cancellationToken)
        {
            CancelCalls++;
            if (conflict) throw new OrderCancellationConflictException("completed");
            return Task.CompletedTask;
        }
    }
}
