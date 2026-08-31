using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Order.Application.Workflow;
using NexaConnect.Services.Order.Domain;

namespace NexaConnect.Services.Order.Application.PaymentReviews;

public sealed record PaymentReviewCase(Guid OrderId,Guid OrganizationId,Guid BranchId,Guid PaymentIntentId,string Status,
    string Reason,string? Resolution,long ConcurrencyVersion,DateTimeOffset CreatedAtUtc,DateTimeOffset UpdatedAtUtc);
public sealed record ResolvePaymentReviewCommand(Guid OrganizationId,Guid OrderId,string Resolution,string Reason,
    long ExpectedConcurrencyVersion,string ActorSubjectId,Guid CorrelationId,Guid AuthorizationDecisionId);

public sealed record PaymentReviewHistoryEntry(Guid Id,string Action,string Reason,string ActorSubjectId,Guid AuthorizationDecisionId,long ConcurrencyVersion,DateTimeOffset OccurredAtUtc);
public interface IPaymentReviewHistoryRepository
{
    Task<IReadOnlyCollection<PaymentReviewHistoryEntry>> ReadHistoryAsync(Guid organizationId,Guid orderId,CancellationToken cancellationToken);
}

public interface IPaymentReviewRepository
{
    Task<IReadOnlyCollection<PaymentReviewCase>> ListOpenAsync(Guid organizationId,Guid branchId,int limit,CancellationToken cancellationToken);
    Task<PaymentReviewCase?> GetReviewAsync(Guid organizationId,Guid orderId,CancellationToken cancellationToken);
    Task<Guid?> ClaimResolutionAsync(PaymentReviewCase review,string resolution,string actor,DateTimeOffset now,CancellationToken cancellationToken);
    Task ReleaseResolutionAsync(PaymentReviewCase review,Guid claimId,CancellationToken cancellationToken);
    Task<bool> ResolveAsync(OrderAggregate order,PaymentReviewCase review,string resolution,string reason,string actor,
        Guid claimId,OrderPaymentReviewResolvedV1 integrationEvent,PlatformAuditEventV1 audit,CancellationToken cancellationToken);
}

public sealed class PaymentReviewApplicationService(IOrderRepository orders,IInventoryReservationPort inventory,
    IKitchenPort kitchen,TimeProvider? timeProvider=null)
{
    private readonly TimeProvider clock=timeProvider??TimeProvider.System;

    public Task<IReadOnlyCollection<PaymentReviewHistoryEntry>> HistoryAsync(Guid organizationId,Guid orderId,CancellationToken cancellationToken)=>
        orders is IPaymentReviewHistoryRepository history?history.ReadHistoryAsync(organizationId,orderId,cancellationToken):throw new InvalidOperationException("Payment review history requires PostgreSQL persistence.");

    public Task<IReadOnlyCollection<PaymentReviewCase>> ListAsync(Guid organizationId,Guid branchId,int limit,CancellationToken cancellationToken)
    {
        if(orders is not IPaymentReviewRepository reviews)throw new InvalidOperationException("Payment review requires PostgreSQL persistence.");
        if(organizationId==Guid.Empty||branchId==Guid.Empty||limit is <1 or >200)throw new ArgumentException("Organization, branch, and a limit from 1 to 200 are required.");
        return reviews.ListOpenAsync(organizationId,branchId,limit,cancellationToken);
    }

    public Task<PaymentReviewCase?> GetAsync(Guid organizationId,Guid orderId,CancellationToken cancellationToken)=>
        orders is IPaymentReviewRepository reviews?reviews.GetReviewAsync(organizationId,orderId,cancellationToken):Task.FromResult<PaymentReviewCase?>(null);

    public async Task<PaymentReviewCase?> ResolveAsync(ResolvePaymentReviewCommand command,CancellationToken cancellationToken)
    {
        if(orders is not IPaymentReviewRepository reviews||orders is not IOrderLookup lookup)
            throw new InvalidOperationException("Payment review requires PostgreSQL persistence.");
        string resolution=command.Resolution.Trim().ToLowerInvariant();string reason=command.Reason.Trim();string actor=command.ActorSubjectId.Trim();
        if(resolution is not ("confirm_void" or "resume_payment" or "escalate")||reason.Length is <1 or >200||actor.Length is <1 or >200||command.AuthorizationDecisionId==Guid.Empty)
            throw new ArgumentException("Resolution, bounded reason, and actor are required.");
        PaymentReviewCase? review=await reviews.GetReviewAsync(command.OrganizationId,command.OrderId,cancellationToken);
        if(review is null)return null;
        if(review.Status!="open"||review.ConcurrencyVersion!=command.ExpectedConcurrencyVersion)
            throw new InvalidOperationException("Payment review concurrency conflict; reload the current case before deciding.");
        OrderAggregate order=await lookup.GetAsync(command.OrderId,cancellationToken)??throw new InvalidOperationException("Reviewed order is missing.");
        if(order.OrganizationId!=command.OrganizationId||order.PaymentIntentId!=review.PaymentIntentId||order.Status!=OrderStatus.PaymentReview)
            throw new InvalidOperationException("Payment review ownership or state does not match the order.");
        DateTimeOffset now=clock.GetUtcNow();
        Guid? claimId=await reviews.ClaimResolutionAsync(review,resolution,actor,now,cancellationToken);
        if(claimId is null)throw new InvalidOperationException("Payment review concurrency conflict.");
        try
        {
            if(resolution=="confirm_void")
            {
                await inventory.ReleaseAsync(order.Id,order.BranchId,cancellationToken);
                await kitchen.CancelTicketAsync(order.OrganizationId,order.Id,order.BranchId,cancellationToken);
                order.ResolvePaymentReviewAsVoided();
            }
            else if(resolution=="resume_payment") order.ResumePaymentPending();
        }
        catch{await reviews.ReleaseResolutionAsync(review,claimId.Value,cancellationToken);throw;}
        long nextVersion=review.ConcurrencyVersion+1;
        var resolved=new OrderPaymentReviewResolvedV1(Guid.NewGuid(),command.CorrelationId,now,order.OrganizationId,
            order.Id,review.PaymentIntentId,resolution,nextVersion,command.AuthorizationDecisionId);
        var audit=new PlatformAuditEventV1(Guid.NewGuid(),command.CorrelationId,now,actor,order.OrganizationId,
            "order.payment-review.resolved","order",order.Id.ToString("D"),"succeeded");
        if(!await reviews.ResolveAsync(order,review,resolution,reason,actor,claimId.Value,resolved,audit,cancellationToken))
            throw new InvalidOperationException("Payment review concurrency conflict.");
        return await reviews.GetReviewAsync(command.OrganizationId,command.OrderId,cancellationToken);
    }
}
