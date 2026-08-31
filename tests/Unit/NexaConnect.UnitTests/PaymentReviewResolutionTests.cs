using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Order.Application.PaymentReviews;
using NexaConnect.Services.Order.Application.Workflow;
using NexaConnect.Services.Order.Domain;

namespace NexaConnect.UnitTests;

public sealed class PaymentReviewResolutionTests
{
    [Fact]
    public async Task Confirm_void_compensates_and_atomically_resolves_review()
    {
        var (order,review)=ReviewedOrder();var repository=new ReviewRepository(order,review);var inventory=new Inventory();var kitchen=new Kitchen();
        var service=new PaymentReviewApplicationService(repository,inventory,kitchen);
        PaymentReviewCase? result=await service.ResolveAsync(Command(order,review,"confirm_void"),default);
        Assert.Equal(OrderStatus.PaymentFailed,order.Status);Assert.Equal("resolved",result!.Status);Assert.Equal(1,inventory.Calls);Assert.Equal(1,kitchen.Calls);
        Assert.Equal("order.payment-review.resolved",repository.Audit!.Action);Assert.Equal("confirm_void",repository.Event!.Resolution);
    }

    [Fact]
    public async Task Resume_payment_retains_dependencies_and_returns_pending()
    {
        var (order,review)=ReviewedOrder();var repository=new ReviewRepository(order,review);var inventory=new Inventory();var kitchen=new Kitchen();
        var service=new PaymentReviewApplicationService(repository,inventory,kitchen);
        await service.ResolveAsync(Command(order,review,"resume_payment"),default);
        Assert.Equal(OrderStatus.PaymentPending,order.Status);Assert.Equal(0,inventory.Calls);Assert.Equal(0,kitchen.Calls);
    }

    [Fact]
    public async Task Escalation_records_history_but_keeps_case_open()
    {
        var (order,review)=ReviewedOrder();var repository=new ReviewRepository(order,review);var service=new PaymentReviewApplicationService(repository,new Inventory(),new Kitchen());
        PaymentReviewCase? result=await service.ResolveAsync(Command(order,review,"escalate"),default);
        Assert.Equal(OrderStatus.PaymentReview,order.Status);Assert.Equal("open",result!.Status);Assert.Equal(2,result.ConcurrencyVersion);
    }

    [Fact]
    public async Task Stale_operator_decision_is_rejected()
    {
        var (order,review)=ReviewedOrder();var service=new PaymentReviewApplicationService(new ReviewRepository(order,review),new Inventory(),new Kitchen());
        ResolvePaymentReviewCommand command=Command(order,review,"resume_payment") with{ExpectedConcurrencyVersion=99};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ResolveAsync(command,default));
    }

    private static ResolvePaymentReviewCommand Command(OrderAggregate order,PaymentReviewCase review,string resolution)=>
        new(order.OrganizationId,order.Id,resolution,"operator_verified",review.ConcurrencyVersion,"operator-subject",Guid.NewGuid(),Guid.NewGuid());

    [Theory]
    [InlineData("resolved")]
    [InlineData("resolving")]
    public async Task Non_open_case_is_a_conflict_not_a_successful_operator_decision(string status)
    {
        var (order,review)=ReviewedOrder();var repository=new ReviewRepository(order,review with{Status=status});
        var inventory=new Inventory();var kitchen=new Kitchen();var service=new PaymentReviewApplicationService(repository,inventory,kitchen);
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ResolveAsync(Command(order,review,"confirm_void"),default));
        Assert.Contains("concurrency",error.Message);Assert.Null(repository.Event);Assert.Null(repository.Audit);
        Assert.Equal(0,inventory.Calls);Assert.Equal(0,kitchen.Calls);
    }

    private static (OrderAggregate,PaymentReviewCase) ReviewedOrder()
    {
        Guid intent=Guid.NewGuid();var order=OrderAggregate.Create(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),[new OrderLine(Guid.NewGuid(),"Meal",10,1,"kitchen")],"USD");
        order.Submit();order.MarkInventoryReserved();order.MarkKitchenAccepted();order.MarkPaymentPending(intent);order.MarkPaymentReview();
        return(order,new(order.Id,order.OrganizationId,order.BranchId,intent,"open","void_failed",null,1,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow));
    }

    private sealed class ReviewRepository(OrderAggregate order,PaymentReviewCase review):IOrderRepository,IOrderLookup,IPaymentReviewRepository
    {
        private PaymentReviewCase value=review;public OrderPaymentReviewResolvedV1? Event{get;private set;}public PlatformAuditEventV1? Audit{get;private set;}
        public Task SaveAsync(OrderAggregate value,CancellationToken cancellationToken)=>Task.CompletedTask;
        public Task<OrderAggregate?> GetAsync(Guid orderId,CancellationToken cancellationToken)=>Task.FromResult<OrderAggregate?>(orderId==order.Id?order:null);
        public Task<IReadOnlyCollection<PaymentReviewCase>> ListOpenAsync(Guid organizationId,Guid branchId,int limit,CancellationToken cancellationToken)=>Task.FromResult<IReadOnlyCollection<PaymentReviewCase>>([value]);
        public Task<PaymentReviewCase?> GetReviewAsync(Guid organizationId,Guid orderId,CancellationToken cancellationToken)=>Task.FromResult<PaymentReviewCase?>(organizationId==value.OrganizationId&&orderId==value.OrderId?value:null);
        public Task<Guid?> ClaimResolutionAsync(PaymentReviewCase review,string resolution,string actor,DateTimeOffset now,CancellationToken cancellationToken){if(value.Status!="open")return Task.FromResult<Guid?>(null);value=value with{Status="resolving",Resolution=resolution,ConcurrencyVersion=value.ConcurrencyVersion+1};return Task.FromResult<Guid?>(Guid.NewGuid());}
        public Task ReleaseResolutionAsync(PaymentReviewCase review,Guid claimId,CancellationToken cancellationToken){value=value with{Status="open",Resolution=null};return Task.CompletedTask;}
        public Task<bool> ResolveAsync(OrderAggregate aggregate,PaymentReviewCase current,string resolution,string reason,string actor,Guid claimId,OrderPaymentReviewResolvedV1 integrationEvent,PlatformAuditEventV1 audit,CancellationToken cancellationToken)
        {Event=integrationEvent;Audit=audit;value=value with{Status=resolution=="escalate"?"open":"resolved",Resolution=resolution,ConcurrencyVersion=integrationEvent.ConcurrencyVersion,UpdatedAtUtc=integrationEvent.OccurredAtUtc};return Task.FromResult(true);}
    }
    private sealed class Inventory:IInventoryReservationPort{public int Calls{get;private set;}public Task<InventoryReservationResult> ReserveAsync(Guid orderId,Guid branchId,IReadOnlyCollection<OrderLine> lines,CancellationToken cancellationToken)=>throw new NotSupportedException();public Task ReleaseAsync(Guid orderId,Guid branchId,CancellationToken cancellationToken){Calls++;return Task.CompletedTask;}}
    private sealed class Kitchen:IKitchenPort{public int Calls{get;private set;}public Task<KitchenTicketResult> CreateTicketAsync(Guid organizationId,Guid restaurantId,Guid orderId,Guid branchId,IReadOnlyCollection<OrderLine> lines,CancellationToken cancellationToken)=>throw new NotSupportedException();public Task CancelTicketAsync(Guid organizationId,Guid orderId,Guid branchId,CancellationToken cancellationToken){Calls++;return Task.CompletedTask;}}
}
