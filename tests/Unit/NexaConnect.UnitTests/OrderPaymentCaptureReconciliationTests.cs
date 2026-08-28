using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Order.Application.Workflow;
using NexaConnect.Services.Order.Domain;
using NexaConnect.Services.Order.Infrastructure.Messaging;

namespace NexaConnect.UnitTests;

public sealed class OrderPaymentCaptureReconciliationTests
{
    [Fact]
    public async Task Captured_result_atomically_marks_payment_pending_order_paid()
    {
        var order = PendingOrder();
        var repository = new RecordingRepository(order);
        var publisher = new InMemoryIntegrationEventPublisher();
        var service = new PaymentReconciliationApplicationService(repository, new RecordingInventory(),
            new RecordingKitchen(), publisher);

        bool applied = await service.ApplyAsync(Capture(order, "captured"), default);

        Assert.True(applied);
        Assert.Equal(OrderStatus.Paid, order.Status);
        var completed = Assert.IsType<PaymentCompletedV1>(repository.Event);
        Assert.Equal(order.Id, completed.OrderId);
        Assert.Empty(publisher.Events);
    }

    [Fact]
    public async Task Definitive_failure_compensates_and_atomically_marks_order_failed()
    {
        var order = PendingOrder();
        var repository = new RecordingRepository(order);
        var inventory = new RecordingInventory();
        var kitchen = new RecordingKitchen();
        var service = new PaymentReconciliationApplicationService(repository, inventory, kitchen,
            new InMemoryIntegrationEventPublisher());

        bool applied = await service.ApplyAsync(Capture(order, "failed", "provider_declined"), default);

        Assert.True(applied);
        Assert.Equal(OrderStatus.PaymentFailed, order.Status);
        Assert.Equal(1, inventory.ReleaseCalls);
        Assert.Equal(1, kitchen.CancelCalls);
        Assert.Equal("provider_declined", Assert.IsType<PaymentFailedV1>(repository.Event).Reason);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("capturing")]
    public async Task Unresolved_result_retains_work_and_payment_pending_state(string outcome)
    {
        var order = PendingOrder();
        var repository = new RecordingRepository(order);
        var inventory = new RecordingInventory();
        var kitchen = new RecordingKitchen();
        var service = new PaymentReconciliationApplicationService(repository, inventory, kitchen,
            new InMemoryIntegrationEventPublisher());

        bool applied = await service.ApplyAsync(Capture(order, outcome), default);

        Assert.False(applied);
        Assert.Equal(OrderStatus.PaymentPending, order.Status);
        Assert.Null(repository.Event);
        Assert.Equal(0, inventory.ReleaseCalls);
        Assert.Equal(0, kitchen.CancelCalls);
    }

    [Fact]
    public async Task Duplicate_terminal_delivery_is_a_no_op()
    {
        var order = PendingOrder();
        order.MarkPaid();
        var repository = new RecordingRepository(order);
        var inventory = new RecordingInventory();
        var kitchen = new RecordingKitchen();
        var service = new PaymentReconciliationApplicationService(repository, inventory, kitchen,
            new InMemoryIntegrationEventPublisher());

        Assert.True(await service.ApplyAsync(Capture(order, "failed"), default));
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Null(repository.Event);
        Assert.Equal(0, inventory.ReleaseCalls);
        Assert.Equal(0, kitchen.CancelCalls);
    }

    [Fact]
    public async Task Authorization_recovery_does_not_complete_order_before_capture()
    {
        var order = PendingOrder();
        var repository = new RecordingRepository(order);
        var service = new PaymentReconciliationApplicationService(repository, new RecordingInventory(),
            new RecordingKitchen(), new InMemoryIntegrationEventPublisher());
        var message = new PaymentAuthorizationReconciledV1(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
            order.OrganizationId, order.Id, order.PaymentIntentId!.Value, "authorized", null);

        Assert.True(await service.ApplyAsync(message, default));
        Assert.Equal(OrderStatus.PaymentPending, order.Status);
        Assert.Null(repository.Event);
    }

    [Fact]
    public async Task Partial_compensation_failure_keeps_order_pending_and_redelivery_retries_safely()
    {
        var order=PendingOrder();
        var repository=new RecordingRepository(order);
        var inventory=new RecordingInventory();
        var kitchen=new FailOnceKitchen();
        var service=new PaymentReconciliationApplicationService(repository,inventory,kitchen,new InMemoryIntegrationEventPublisher());
        PaymentCaptureReconciledV1 message=Capture(order,"failed","provider_capture_failed");

        await Assert.ThrowsAsync<HttpRequestException>(()=>service.ApplyAsync(message,default));
        Assert.Equal(OrderStatus.PaymentPending,order.Status);
        Assert.Null(repository.Event);

        Assert.True(await service.ApplyAsync(message,default));
        Assert.Equal(OrderStatus.PaymentFailed,order.Status);
        Assert.Equal(2,inventory.ReleaseCalls);
        Assert.Equal(2,kitchen.CancelCalls);
    }

    [Fact]
    public async Task Reconciled_void_compensates_and_cancels_unpaid_order()
    {
        var order=PendingOrder(); var repository=new RecordingRepository(order); var inventory=new RecordingInventory(); var kitchen=new RecordingKitchen();
        var service=new PaymentReconciliationApplicationService(repository,inventory,kitchen,new InMemoryIntegrationEventPublisher());
        var message=new PaymentVoidReconciledV1(Guid.NewGuid(),Guid.NewGuid(),DateTimeOffset.UtcNow,order.OrganizationId,order.Id,order.PaymentIntentId!.Value,"voided",null);
        Assert.True(await service.ApplyAsync(message,default));
        Assert.Equal(OrderStatus.PaymentFailed,order.Status); Assert.Equal(1,inventory.ReleaseCalls); Assert.Equal(1,kitchen.CancelCalls);
        Assert.Equal("authorization_voided",Assert.IsType<PaymentFailedV1>(repository.Event).Reason);
    }

    [Theory]
    [InlineData("voiding")]
    [InlineData("void_unknown")]
    public async Task Uncertain_void_is_acknowledged_without_releasing_work(string status)
    {
        var order=PendingOrder(); var repository=new RecordingRepository(order); var inventory=new RecordingInventory(); var kitchen=new RecordingKitchen();
        var service=new PaymentReconciliationApplicationService(repository,inventory,kitchen,new InMemoryIntegrationEventPublisher());
        var message=new PaymentVoidReconciledV1(Guid.NewGuid(),Guid.NewGuid(),DateTimeOffset.UtcNow,order.OrganizationId,order.Id,order.PaymentIntentId!.Value,status,"provider_timeout");
        Assert.True(await service.ApplyAsync(message,default)); Assert.Equal(OrderStatus.PaymentPending,order.Status);
        Assert.Equal(0,inventory.ReleaseCalls); Assert.Equal(0,kitchen.CancelCalls); Assert.Null(repository.Event);
    }

    [Theory]
    [InlineData("void_failed")]
    [InlineData("requires_action")]
    public async Task Definitive_or_exhausted_void_problem_enters_financial_review(string status)
    {
        var order=PendingOrder(); var repository=new RecordingRepository(order);
        var service=new PaymentReconciliationApplicationService(repository,new RecordingInventory(),new RecordingKitchen(),new InMemoryIntegrationEventPublisher());
        Guid paymentIntent=order.PaymentIntentId!.Value;
        var message=new PaymentVoidReconciledV1(Guid.NewGuid(),Guid.NewGuid(),DateTimeOffset.UtcNow,order.OrganizationId,order.Id,paymentIntent,status,"void_problem");
        Assert.True(await service.ApplyAsync(message,default)); Assert.Equal(OrderStatus.PaymentReview,order.Status);
        var review=Assert.IsType<OrderPaymentReviewRequiredV1>(repository.Event); Assert.Equal(paymentIntent,review.PaymentIntentId);
    }

    [Fact]
    public async Task Void_event_cannot_change_captured_paid_order()
    {
        var order=PendingOrder(); order.MarkPaid(); var repository=new RecordingRepository(order); var inventory=new RecordingInventory();
        var service=new PaymentReconciliationApplicationService(repository,inventory,new RecordingKitchen(),new InMemoryIntegrationEventPublisher());
        var message=new PaymentVoidedV1(Guid.NewGuid(),Guid.NewGuid(),DateTimeOffset.UtcNow,order.OrganizationId,order.Id,order.PaymentIntentId!.Value);
        Assert.True(await service.ApplyAsync(message,default)); Assert.Equal(OrderStatus.Paid,order.Status); Assert.Equal(0,inventory.ReleaseCalls); Assert.Null(repository.Event);
    }

    [Fact]
    public async Task Cross_tenant_void_event_is_rejected()
    {
        var order=PendingOrder(); var service=new PaymentReconciliationApplicationService(new RecordingRepository(order),new RecordingInventory(),new RecordingKitchen(),new InMemoryIntegrationEventPublisher());
        var message=new PaymentVoidReconciledV1(Guid.NewGuid(),Guid.NewGuid(),DateTimeOffset.UtcNow,Guid.NewGuid(),order.Id,Guid.NewGuid(),"voided",null);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ApplyAsync(message,default));
    }

    [Fact]
    public async Task Void_event_for_another_payment_intent_is_rejected()
    {
        var order=PendingOrder(); var service=new PaymentReconciliationApplicationService(new RecordingRepository(order),new RecordingInventory(),new RecordingKitchen(),new InMemoryIntegrationEventPublisher());
        var message=new PaymentVoidReconciledV1(Guid.NewGuid(),Guid.NewGuid(),DateTimeOffset.UtcNow,order.OrganizationId,order.Id,Guid.NewGuid(),"voided",null);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ApplyAsync(message,default));
    }

    private static PaymentCaptureReconciledV1 Capture(OrderAggregate order, string outcome, string? failure = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, order.OrganizationId, order.Id,
            order.PaymentIntentId!.Value, outcome, failure);

    private static OrderAggregate PendingOrder()
    {
        var order = OrderAggregate.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            [new OrderLine(Guid.NewGuid(), "Soup", 10m, 1, "kitchen")], "USD");
        order.Submit();
        order.MarkInventoryReserved();
        order.MarkKitchenAccepted();
        order.MarkPaymentPending(Guid.NewGuid());
        return order;
    }

    private sealed class RecordingRepository(OrderAggregate order)
        : IOrderRepository, IOrderLookup, ITransactionalOrderRepository
    {
        public IIntegrationEvent? Event { get; private set; }
        public Task<OrderAggregate?> GetAsync(Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult<OrderAggregate?>(order.Id == orderId ? order : null);
        public Task SaveAsync(OrderAggregate value, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveWithEventAsync(OrderAggregate value, IIntegrationEvent integrationEvent,
            CancellationToken cancellationToken)
        {
            Event = integrationEvent;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingInventory : IInventoryReservationPort
    {
        public int ReleaseCalls { get; private set; }
        public Task<InventoryReservationResult> ReserveAsync(Guid orderId, Guid branchId,
            IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReleaseAsync(Guid orderId, Guid branchId, CancellationToken cancellationToken)
        {
            ReleaseCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingKitchen : IKitchenPort
    {
        public int CancelCalls { get; private set; }
        public Task<KitchenTicketResult> CreateTicketAsync(Guid organizationId, Guid restaurantId, Guid orderId,
            Guid branchId, IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task CancelTicketAsync(Guid organizationId, Guid orderId, Guid branchId,
            CancellationToken cancellationToken)
        {
            CancelCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FailOnceKitchen : IKitchenPort
    {
        public int CancelCalls { get; private set; }
        public Task<KitchenTicketResult> CreateTicketAsync(Guid organizationId,Guid restaurantId,Guid orderId,Guid branchId,IReadOnlyCollection<OrderLine> lines,CancellationToken cancellationToken)=>throw new NotSupportedException();
        public Task CancelTicketAsync(Guid organizationId,Guid orderId,Guid branchId,CancellationToken cancellationToken)
        {
            CancelCalls++;
            if(CancelCalls==1)throw new HttpRequestException("simulated partial compensation failure");
            return Task.CompletedTask;
        }
    }
}
