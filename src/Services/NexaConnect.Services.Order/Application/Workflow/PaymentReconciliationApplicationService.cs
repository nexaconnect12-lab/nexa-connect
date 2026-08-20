using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Order.Domain;

namespace NexaConnect.Services.Order.Application.Workflow;

public interface IOrderLookup
{
    Task<OrderAggregate?> GetAsync(Guid orderId, CancellationToken cancellationToken);
}

public sealed class PaymentReconciliationApplicationService(
    IOrderRepository orders,
    IInventoryReservationPort inventory,
    IKitchenPort kitchen,
    IIntegrationEventPublisher events,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<bool> ApplyAsync(PaymentAuthorizationReconciledV1 reconciliation,
        CancellationToken cancellationToken)
    {
        if (orders is not IOrderLookup lookup) return false;
        OrderAggregate? order = await lookup.GetAsync(reconciliation.OrderId, cancellationToken);
        if (order is null) return false;
        if (order.OrganizationId != reconciliation.OrganizationId)
            throw new InvalidOperationException("Payment reconciliation organization does not match the order.");
        if (order.Status is OrderStatus.Paid or OrderStatus.PaymentFailed or OrderStatus.Rejected)
            return true;
        if (order.Status != OrderStatus.PaymentPending) return false;

        if (string.Equals(reconciliation.Outcome, "authorized", StringComparison.Ordinal))
        {
            order.MarkPaid();
            await orders.SaveAsync(order, cancellationToken);
            await events.PublishAsync(new PaymentCompletedV1(Guid.NewGuid(), reconciliation.CorrelationId,
                clock.GetUtcNow(), order.Id, reconciliation.PaymentIntentId, order.TotalAmount, order.Currency, "unknown"), cancellationToken);
            return true;
        }

        if (string.Equals(reconciliation.Outcome, "failed", StringComparison.Ordinal))
        {
            await inventory.ReleaseAsync(order.Id, order.BranchId, cancellationToken);
            await kitchen.CancelTicketAsync(order.OrganizationId, order.Id, order.BranchId, cancellationToken);
            order.MarkPaymentFailed();
            await orders.SaveAsync(order, cancellationToken);
            await events.PublishAsync(new PaymentFailedV1(Guid.NewGuid(), reconciliation.CorrelationId,
                clock.GetUtcNow(), order.Id, reconciliation.FailureCode ?? "provider_declined"), cancellationToken);
            return true;
        }

        return false;
    }
}
