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
    IPaymentPort payment,
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
        if (IsAwaitingProviderBinding(order)) return false;
        EnsurePaymentIntent(order, reconciliation.PaymentIntentId);
        if (order.Status is OrderStatus.Paid or OrderStatus.PaymentFailed or OrderStatus.Rejected)
            return true;
        if (order.Status != OrderStatus.PaymentPending) return false;

        if (string.Equals(reconciliation.Outcome, "authorized", StringComparison.Ordinal))
        {
            string paymentMethod = order.WorkflowPaymentMethod
                ?? throw new InvalidOperationException("An authorized recoverable order must retain its payment method.");
            PaymentResult capture = await payment.AuthorizeAsync(order.OrganizationId, order.RestaurantId,
                order.BranchId, order.Id, order.TotalAmount, order.Currency, paymentMethod, cancellationToken);
            EnsurePaymentIntent(order, capture.PaymentId ?? Guid.Empty);
            if (capture.Completed)
            {
                order.MarkPaid();
                await PersistAsync(order, new PaymentCompletedV1(OrderPaymentEventIdentity.Completion(order.Id, order.WorkflowPaymentMethod), reconciliation.CorrelationId,
                    clock.GetUtcNow(), order.Id, reconciliation.PaymentIntentId, order.TotalAmount, order.Currency,
                    paymentMethod), cancellationToken);
                return true;
            }
            if (string.Equals(capture.Outcome, "failed", StringComparison.Ordinal))
            {
                await inventory.ReleaseAsync(order.OrganizationId, order.Id, order.BranchId, cancellationToken);
                await kitchen.CancelTicketAsync(order.OrganizationId, order.Id, order.BranchId, cancellationToken);
                order.MarkPaymentFailed();
                await PersistAsync(order, new PaymentFailedV1(Guid.NewGuid(), reconciliation.CorrelationId,
                    clock.GetUtcNow(), order.Id, capture.Reason ?? "capture_failed"), cancellationToken);
                return true;
            }
            if (string.Equals(capture.Outcome, "requires_action", StringComparison.Ordinal))
            {
                order.MarkPaymentReview();
                await PersistAsync(order, new OrderPaymentReviewRequiredV1(Guid.NewGuid(), reconciliation.CorrelationId,
                    clock.GetUtcNow(), order.OrganizationId, order.Id, reconciliation.PaymentIntentId,
                    capture.Reason ?? "capture_attempts_exhausted"), cancellationToken);
            }
            // capturing/capture_unknown is now owned by Payment capture recovery. The state-aware
            // adapter throws for an unchanged authorized intent, causing inbox redelivery instead.
            return true;
        }

        if (string.Equals(reconciliation.Outcome, "failed", StringComparison.Ordinal))
        {
            await inventory.ReleaseAsync(order.OrganizationId, order.Id, order.BranchId, cancellationToken);
            await kitchen.CancelTicketAsync(order.OrganizationId, order.Id, order.BranchId, cancellationToken);
            order.MarkPaymentFailed();
            await PersistAsync(order, new PaymentFailedV1(Guid.NewGuid(), reconciliation.CorrelationId,
                clock.GetUtcNow(), order.Id, reconciliation.FailureCode ?? "provider_declined"), cancellationToken);
            return true;
        }

        if (string.Equals(reconciliation.Outcome, "requires_action", StringComparison.Ordinal))
        {
            order.MarkPaymentReview();
            await PersistAsync(order, new OrderPaymentReviewRequiredV1(Guid.NewGuid(), reconciliation.CorrelationId,
                clock.GetUtcNow(), order.OrganizationId, order.Id, reconciliation.PaymentIntentId,
                reconciliation.FailureCode ?? "authorization_attempts_exhausted"), cancellationToken);
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
        if (IsAwaitingProviderBinding(order)) return false;
        EnsurePaymentIntent(order, reconciliation.PaymentIntentId);
        if (order.Status is OrderStatus.Paid or OrderStatus.PaymentFailed or OrderStatus.Rejected)
            return true;
        if (order.Status != OrderStatus.PaymentPending) return false;

        if (string.Equals(outcome, "captured", StringComparison.Ordinal))
        {
            order.MarkPaid();
            await PersistAsync(order, new PaymentCompletedV1(OrderPaymentEventIdentity.Completion(order.Id, order.WorkflowPaymentMethod), reconciliation.CorrelationId,
                clock.GetUtcNow(), order.Id, reconciliation.PaymentIntentId, order.TotalAmount, order.Currency,
                "reconciled_capture"), cancellationToken);
            return true;
        }

        if (string.Equals(outcome, "failed", StringComparison.Ordinal))
        {
            // Both downstream operations are idempotent by order identity. A redelivery retries
            // incomplete compensation before the terminal Order transition is committed.
            await inventory.ReleaseAsync(order.OrganizationId, order.Id, order.BranchId, cancellationToken);
            await kitchen.CancelTicketAsync(order.OrganizationId, order.Id, order.BranchId, cancellationToken);
            order.MarkPaymentFailed();
            await PersistAsync(order, new PaymentFailedV1(Guid.NewGuid(), reconciliation.CorrelationId,
                clock.GetUtcNow(), order.Id, failureCode ?? "capture_failed"), cancellationToken);
            return true;
        }

        if (string.Equals(outcome, "requires_action", StringComparison.Ordinal))
        {
            order.MarkPaymentReview();
            await PersistAsync(order, new OrderPaymentReviewRequiredV1(Guid.NewGuid(), reconciliation.CorrelationId,
                clock.GetUtcNow(), order.OrganizationId, order.Id, reconciliation.PaymentIntentId,
                failureCode ?? "capture_attempts_exhausted"), cancellationToken);
            return true;
        }

        // Unknown/in-progress provider results intentionally retain Inventory and Kitchen work.
        // The current event is handled; Payment owns publication of a later definitive result.
        return string.Equals(outcome, "unknown", StringComparison.Ordinal)
            || string.Equals(outcome, "capturing", StringComparison.Ordinal)
            || string.Equals(outcome, "capture_unknown", StringComparison.Ordinal);
    }

    public Task<bool> ApplyAsync(PaymentVoidedV1 message, CancellationToken cancellationToken) =>
        ApplyVoidAsync(message.OrganizationId, message.OrderId, message.PaymentIntentId, message.CorrelationId,
            "voided", null, cancellationToken);

    public Task<bool> ApplyAsync(PaymentVoidFailedV1 message, CancellationToken cancellationToken) =>
        ApplyVoidAsync(message.OrganizationId, message.OrderId, message.PaymentIntentId, message.CorrelationId,
            "void_failed", message.FailureCode, cancellationToken);

    public Task<bool> ApplyAsync(PaymentVoidUncertainV1 message, CancellationToken cancellationToken) =>
        ApplyVoidAsync(message.OrganizationId, message.OrderId, message.PaymentIntentId, message.CorrelationId,
            "void_unknown", message.FailureCode, cancellationToken);

    public Task<bool> ApplyAsync(PaymentVoidReconciledV1 message, CancellationToken cancellationToken) =>
        ApplyVoidAsync(message.OrganizationId, message.OrderId, message.PaymentIntentId, message.CorrelationId,
            message.Status, message.FailureCode, cancellationToken);

    private async Task<bool> ApplyVoidAsync(Guid organizationId, Guid orderId, Guid paymentIntentId,
        Guid correlationId, string status, string? failureCode, CancellationToken cancellationToken)
    {
        if (orders is not IOrderLookup lookup) return false;
        OrderAggregate? order = await lookup.GetAsync(orderId, cancellationToken);
        if (order is null) return false;
        if (order.OrganizationId != organizationId)
            throw new InvalidOperationException("Payment void reconciliation organization does not match the order.");
        EnsurePaymentIntent(order, paymentIntentId);
        // Captured/paid orders are immutable at the void boundary. A provider-side reversal after
        // capture belongs to the refund workflow and must never cancel a paid Order.
        if (order.Status is OrderStatus.Paid or OrderStatus.PaymentFailed or OrderStatus.Rejected)
            return true;
        if (order.Status == OrderStatus.PaymentReview) return true;
        if (order.Status != OrderStatus.PaymentPending) return false;

        if (string.Equals(status, "voided", StringComparison.Ordinal))
        {
            await inventory.ReleaseAsync(order.OrganizationId, order.Id, order.BranchId, cancellationToken);
            await kitchen.CancelTicketAsync(order.OrganizationId, order.Id, order.BranchId, cancellationToken);
            order.MarkPaymentFailed();
            await PersistAsync(order, new PaymentFailedV1(Guid.NewGuid(), correlationId, clock.GetUtcNow(),
                order.Id, "authorization_voided"), cancellationToken);
            return true;
        }

        if (string.Equals(status, "requires_action", StringComparison.Ordinal)
            || string.Equals(status, "void_failed", StringComparison.Ordinal))
        {
            order.MarkPaymentReview();
            await PersistAsync(order, new OrderPaymentReviewRequiredV1(Guid.NewGuid(), correlationId,
                clock.GetUtcNow(), organizationId, order.Id, paymentIntentId,
                failureCode ?? (status == "void_failed" ? "provider_void_failed" : "void_attempts_exhausted")), cancellationToken);
            return true;
        }

        // voiding/void_unknown retain Inventory and Kitchen work. The event is safely acknowledged;
        // a later reconciled event owns the definitive transition.
        return string.Equals(status, "voiding", StringComparison.Ordinal)
            || string.Equals(status, "void_unknown", StringComparison.Ordinal);
    }

    private static void EnsurePaymentIntent(OrderAggregate order, Guid paymentIntentId)
    {
        if (order.PaymentIntentId is null || order.PaymentIntentId != paymentIntentId)
            throw new InvalidOperationException("Payment reconciliation intent does not match the order.");
    }

    private static bool IsAwaitingProviderBinding(OrderAggregate order) =>
        order.Status == OrderStatus.KitchenAccepted
        && order.PaymentIntentId is null
        && order.WorkflowPaymentMethod is not null
        && order.WorkflowPaymentMethod is not ("cash_manual" or "promptpay_manual");

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
