using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Order.Application.Workflow;
using NexaConnect.Services.Order.Domain;

namespace NexaConnect.UnitTests;

public sealed class OrderWorkflowRecoveryTests
{
    [Fact]
    public async Task Manual_tender_recovery_resumes_inventory_then_kitchen_without_payment_boundary()
    {
        OrderAggregate order = OrderAggregate.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            [new OrderLine(Guid.NewGuid(), "Pad thai", 120m, 1, "kitchen")], "THB", Guid.NewGuid(),
            idempotencyKey: "checkout-1", workflowPaymentMethod: "cash_manual", workflowCorrelationId: Guid.NewGuid());
        order.Submit();
        var repository = new RecoveryRepository(order);
        var inventory = new RecoveryInventory();
        var kitchen = new RecoveryKitchen();
        var service = new OrderWorkflowRecoveryService(repository, inventory, kitchen);

        Assert.True(await service.RecoverNextAsync(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1), default));
        Assert.Equal(OrderStatus.InventoryReserved, repository.Current.Status);
        Assert.IsType<InventoryReservedV1>(repository.Events.Single());

        Assert.True(await service.RecoverNextAsync(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1), default));
        Assert.Equal(OrderStatus.KitchenAccepted, repository.Current.Status);
        Assert.IsType<KitchenTicketCreatedV1>(repository.Events.Last());
        Assert.Equal(1, inventory.Calls);
        Assert.Equal(1, kitchen.Calls);
        Assert.False(await service.RecoverNextAsync(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1), default));
    }

    [Fact]
    public async Task Dependency_failure_releases_claim_for_bounded_retry()
    {
        OrderAggregate order = OrderAggregate.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            [new OrderLine(Guid.NewGuid(), "Tea", 40m, 1, "bar")], "THB", Guid.NewGuid(),
            workflowPaymentMethod: "cash_manual", workflowCorrelationId: Guid.NewGuid());
        order.Submit();
        var repository = new RecoveryRepository(order);
        var service = new OrderWorkflowRecoveryService(repository, new RecoveryInventory(fail: true), new RecoveryKitchen());

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.RecoverNextAsync(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1), default));

        Assert.Equal("dependency_unavailable", repository.LastErrorCategory);
        Assert.Empty(repository.Events);
        Assert.Equal(OrderStatus.Submitted, repository.Current.Status);
    }

    private sealed class RecoveryRepository(OrderAggregate order) : IOrderWorkflowRecoveryRepository
    {
        private bool available = true;
        public OrderAggregate Current { get; private set; } = order;
        public List<IIntegrationEvent> Events { get; } = [];
        public string? LastErrorCategory { get; private set; }

        public Task<ClaimedOrderWorkflow?> ClaimNextAsync(DateTimeOffset now, TimeSpan lease, CancellationToken cancellationToken)
        {
            if (!available || Current.Status is not (OrderStatus.Submitted or OrderStatus.InventoryReserved))
                return Task.FromResult<ClaimedOrderWorkflow?>(null);
            available = false;
            return Task.FromResult<ClaimedOrderWorkflow?>(new(Current, Guid.NewGuid(), Events.Count + 1));
        }

        public Task<bool> CommitAsync(ClaimedOrderWorkflow claim, OrderAggregate value, IIntegrationEvent integrationEvent,
            DateTimeOffset now, CancellationToken cancellationToken)
        {
            Current = value; Events.Add(integrationEvent); available = true; return Task.FromResult(true);
        }

        public Task ReleaseAsync(ClaimedOrderWorkflow claim, string errorCategory, DateTimeOffset nextAttemptAtUtc,
            CancellationToken cancellationToken)
        {
            LastErrorCategory = errorCategory; available = true; return Task.CompletedTask;
        }
    }

    private sealed class RecoveryInventory(bool fail = false) : IInventoryReservationPort
    {
        public int Calls { get; private set; }
        public Task<InventoryReservationResult> ReserveAsync(Guid organizationId, Guid orderId, Guid branchId,
            IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken)
        {
            Calls++;
            if (fail) throw new HttpRequestException("temporary");
            return Task.FromResult(new InventoryReservationResult(true, Guid.NewGuid(), null));
        }
    }

    private sealed class RecoveryKitchen : IKitchenPort
    {
        public int Calls { get; private set; }
        public Task<KitchenTicketResult> CreateTicketAsync(Guid organizationId, Guid restaurantId, Guid orderId,
            Guid branchId, IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken)
        {
            Calls++; return Task.FromResult(new KitchenTicketResult(Guid.NewGuid()));
        }
    }
}
