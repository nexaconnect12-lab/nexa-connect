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
            // Authorization is not settlement. Capture (including capture recovery) is the only
            // payment result that is allowed to complete an order.
            return true;
        }

        if (string.Equals(reconciliation.Outcome, "failed", StringComparison.Ordinal))
        {
            await inventory.ReleaseAsync(order.Id, order.BranchId, cancellationToken);
            await kitchen.CancelTicketAsync(order.OrganizationId, order.Id, order.BranchId, cancellationToken);
            order.MarkPaymentFailed();
            await PersistAsync(order, new PaymentFailedV1(Guid.NewGuid(), reconciliation.CorrelationId,
                clock.GetUtcNow(), order.Id, reconciliation.FailureCode ?? "provider_declined"), cancellationToken);
            return true;
        }

        return false;
    }

    public async Task<bool> ApplyAsync(PaymentCaptureReconciledV1 reconciliation,
        CancellationToken cancellationToken)
    {
        var (_, _, _, _, _, _, outcome, failureCode) = reconciliation;
        if (orders is not IOrderLookup lookup) return false;
        OrderAggregate? order = await lookup.GetAsync(reconciliation.OrderId, cancellationToken);
        if (order is null) return false;
        if (order.OrganizationId != reconciliation.OrganizationId)
            throw new InvalidOperationException("Payment capture reconciliation organization does not match the order.");
        if (order.Status is OrderStatus.Paid or OrderStatus.PaymentFailed or OrderStatus.Rejected)
            return true;
        if (order.Status != OrderStatus.PaymentPending) return false;

        if (string.Equals(outcome, "captured", StringComparison.Ordinal))
        {
            order.MarkPaid();
            await PersistAsync(order, new PaymentCompletedV1(Guid.NewGuid(), reconciliation.CorrelationId,
                clock.GetUtcNow(), order.Id, reconciliation.PaymentIntentId, order.TotalAmount, order.Currency,
                "reconciled_capture"), cancellationToken);
            return true;
        }

        if (string.Equals(outcome, "failed", StringComparison.Ordinal))
        {
            // Both downstream operations are idempotent by order identity. A redelivery retries
            // incomplete compensation before the terminal Order transition is committed.
            await inventory.ReleaseAsync(order.Id, order.BranchId, cancellationToken);
            await kitchen.CancelTicketAsync(order.OrganizationId, order.Id, order.BranchId, cancellationToken);
            order.MarkPaymentFailed();
            await PersistAsync(order, new PaymentFailedV1(Guid.NewGuid(), reconciliation.CorrelationId,
                clock.GetUtcNow(), order.Id, failureCode ?? "capture_failed"), cancellationToken);
            return true;
        }

        // Unknown/in-progress provider results intentionally retain Inventory and Kitchen work.
        return false;
    }

    private async Task PersistAsync(OrderAggregate order, IIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        if (orders is ITransactionalOrderRepository transactional)
            await transactional.SaveWithEventAsync(order, integrationEvent, cancellationToken);
        else
        {
            await orders.SaveAsync(order, cancellationToken);
            await events.PublishAsync(integrationEvent, cancellationToken);
        }
    }
}
