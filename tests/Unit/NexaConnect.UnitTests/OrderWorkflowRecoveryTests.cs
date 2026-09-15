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
        var payment = new RecoveryPayment();
        var service = new OrderWorkflowRecoveryService(repository, inventory, kitchen, payment);

        Assert.True(await service.RecoverNextAsync(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1), default));
        Assert.Equal(OrderStatus.InventoryReserved, repository.Current.Status);
        Assert.IsType<InventoryReservedV1>(repository.Events.Single());

        Assert.True(await service.RecoverNextAsync(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1), default));
        Assert.Equal(OrderStatus.KitchenAccepted, repository.Current.Status);
        Assert.IsType<KitchenTicketCreatedV1>(repository.Events.Last());
        Assert.Equal(1, inventory.Calls);
        Assert.Equal(1, kitchen.Calls);
        Assert.Equal(0, payment.Calls);
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
        var service = new OrderWorkflowRecoveryService(repository, new RecoveryInventory(fail: true), new RecoveryKitchen(),
            new RecoveryPayment());

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.RecoverNextAsync(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1), default));

        Assert.Equal("dependency_unavailable", repository.LastErrorCategory);
        Assert.Empty(repository.Events);
        Assert.Equal(OrderStatus.Submitted, repository.Current.Status);
    }

    [Fact]
    public async Task Provider_payment_recovery_resumes_all_durable_stages_and_reuses_the_order_identity()
    {
        Guid orderId = Guid.NewGuid();
        Guid paymentId = Guid.NewGuid();
        OrderAggregate order = CreateProviderOrder(orderId);
        var repository = new RecoveryRepository(order);
        var inventory = new RecoveryInventory();
        var kitchen = new RecoveryKitchen();
        var payment = new RecoveryPayment(new PaymentResult(true, paymentId, null, "captured"));
        var service = new OrderWorkflowRecoveryService(repository, inventory, kitchen, payment);

        Assert.True(await service.RecoverNextAsync(TimeSpan.FromSeconds(30), TimeSpan.Zero, default));
        Assert.True(await service.RecoverNextAsync(TimeSpan.FromSeconds(30), TimeSpan.Zero, default));
        Assert.True(await service.RecoverNextAsync(TimeSpan.FromSeconds(30), TimeSpan.Zero, default));

        Assert.Equal(OrderStatus.Paid, repository.Current.Status);
        Assert.Equal(paymentId, repository.Current.PaymentIntentId);
        Assert.IsType<PaymentCompletedV1>(repository.Events.Last());
        Assert.Equal(orderId, payment.LastOrderId);
        Assert.Equal(1, payment.Calls);
    }

    [Theory]
    [InlineData("authorizing")]
    [InlineData("unknown")]
    [InlineData("requires_action")]
    [InlineData("capturing")]
    [InlineData("capture_unknown")]
    public async Task Uncertain_provider_payment_moves_to_payment_pending_and_leaves_reconciliation_to_payment(string outcome)
    {
        Guid paymentId = Guid.NewGuid();
        OrderAggregate order = CreateProviderOrder(Guid.NewGuid());
        order.MarkInventoryReserved();
        order.MarkKitchenAccepted();
        var repository = new RecoveryRepository(order);
        var payment = new RecoveryPayment(new PaymentResult(false, paymentId, "reconcile", outcome));
        var service = new OrderWorkflowRecoveryService(repository, new RecoveryInventory(), new RecoveryKitchen(), payment);

        Assert.True(await service.RecoverNextAsync(TimeSpan.FromSeconds(30), TimeSpan.Zero, default));

        Assert.Equal(OrderStatus.PaymentPending, repository.Current.Status);
        Assert.Equal(paymentId, repository.Current.PaymentIntentId);
        Assert.IsType<PaymentAuthorizationUncertainV1>(Assert.Single(repository.Events));
        Assert.False(await service.RecoverNextAsync(TimeSpan.FromSeconds(30), TimeSpan.Zero, default));
    }

    [Fact]
    public async Task Definitive_provider_failure_compensates_before_committing_cancelled_order()
    {
        OrderAggregate order = CreateProviderOrder(Guid.NewGuid());
        order.MarkInventoryReserved();
        order.MarkKitchenAccepted();
        var repository = new RecoveryRepository(order);
        var inventory = new RecoveryInventory();
        var kitchen = new RecoveryKitchen();
        var service = new OrderWorkflowRecoveryService(repository, inventory, kitchen,
            new RecoveryPayment(new PaymentResult(false, Guid.NewGuid(), "declined", "failed")));

        Assert.True(await service.RecoverNextAsync(TimeSpan.FromSeconds(30), TimeSpan.Zero, default));

        Assert.Equal(OrderStatus.PaymentFailed, repository.Current.Status);
        Assert.Equal(1, inventory.ReleaseCalls);
        Assert.Equal(1, kitchen.CancelCalls);
        Assert.IsType<PaymentFailedV1>(Assert.Single(repository.Events));
    }

    [Fact]
    public async Task Payment_dependency_failure_releases_kitchen_claim_without_compensation()
    {
        OrderAggregate order = CreateProviderOrder(Guid.NewGuid());
        order.MarkInventoryReserved();
        order.MarkKitchenAccepted();
        var repository = new RecoveryRepository(order);
        var inventory = new RecoveryInventory();
        var kitchen = new RecoveryKitchen();
        var service = new OrderWorkflowRecoveryService(repository, inventory, kitchen,
            new RecoveryPayment(fail: true));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.RecoverNextAsync(TimeSpan.FromSeconds(30), TimeSpan.Zero, default));

        Assert.Equal(OrderStatus.KitchenAccepted, repository.Current.Status);
        Assert.Equal("dependency_unavailable", repository.LastErrorCategory);
        Assert.Equal(0, inventory.ReleaseCalls);
        Assert.Equal(0, kitchen.CancelCalls);
        Assert.Empty(repository.Events);
    }

    private static OrderAggregate CreateProviderOrder(Guid orderId)
    {
        OrderAggregate order = OrderAggregate.Create(orderId, Guid.NewGuid(), Guid.NewGuid(),
            [new OrderLine(Guid.NewGuid(), "Card item", 99m, 1, "kitchen")], "THB", Guid.NewGuid(),
            idempotencyKey: $"checkout-{orderId:N}", workflowPaymentMethod: "card",
            workflowCorrelationId: Guid.NewGuid());
        order.Submit();
        return order;
    }

    private sealed class RecoveryRepository(OrderAggregate order) : IOrderWorkflowRecoveryRepository
    {
        private bool available = true;
        public OrderAggregate Current { get; private set; } = order;
        public List<IIntegrationEvent> Events { get; } = [];
        public string? LastErrorCategory { get; private set; }

        public Task<ClaimedOrderWorkflow?> ClaimNextAsync(DateTimeOffset now, TimeSpan lease, CancellationToken cancellationToken)
        {
            bool eligible = Current.WorkflowPaymentMethod is not null &&
                (Current.Status is OrderStatus.Submitted or OrderStatus.InventoryReserved
                 || Current.Status == OrderStatus.KitchenAccepted
                 && Current.WorkflowPaymentMethod is not ("cash_manual" or "promptpay_manual"));
            if (!available || !eligible)
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
        public int ReleaseCalls { get; private set; }
        public Task<InventoryReservationResult> ReserveAsync(Guid organizationId, Guid orderId, Guid branchId,
            IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken)
        {
            Calls++;
            if (fail) throw new HttpRequestException("temporary");
            return Task.FromResult(new InventoryReservationResult(true, Guid.NewGuid(), null));
        }

        public Task ReleaseAsync(Guid organizationId, Guid orderId, Guid branchId, CancellationToken cancellationToken)
        {
            ReleaseCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecoveryKitchen : IKitchenPort
    {
        public int Calls { get; private set; }
        public int CancelCalls { get; private set; }
        public Task<KitchenTicketResult> CreateTicketAsync(Guid organizationId, Guid restaurantId, Guid orderId,
            Guid branchId, IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken)
        {
            Calls++; return Task.FromResult(new KitchenTicketResult(Guid.NewGuid()));
        }

        public Task CancelTicketAsync(Guid organizationId, Guid orderId, Guid branchId,
            CancellationToken cancellationToken)
        {
            CancelCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecoveryPayment(PaymentResult? result = null, bool fail = false) : IPaymentPort
    {
        private readonly PaymentResult response = result ?? new PaymentResult(true, Guid.NewGuid(), null, "captured");
        public int Calls { get; private set; }
        public Guid? LastOrderId { get; private set; }

        public Task<PaymentResult> AuthorizeAsync(Guid organizationId, Guid restaurantId, Guid branchId, Guid orderId,
            decimal amount, string currency, string method, CancellationToken cancellationToken)
        {
            Calls++;
            LastOrderId = orderId;
            if (fail) throw new HttpRequestException("temporary");
            return Task.FromResult(response);
        }
    }
}
