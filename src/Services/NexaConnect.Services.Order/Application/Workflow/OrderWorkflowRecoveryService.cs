using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Observability;
using NexaConnect.Services.Order.Domain;

namespace NexaConnect.Services.Order.Application.Workflow;

public sealed record ClaimedOrderWorkflow(OrderAggregate Order, Guid ClaimId, int AttemptCount);

public interface IOrderWorkflowRecoveryRepository
{
    Task<ClaimedOrderWorkflow?> ClaimNextAsync(DateTimeOffset now, TimeSpan lease, CancellationToken cancellationToken);
    Task<bool> CommitAsync(ClaimedOrderWorkflow claim, OrderAggregate order, IIntegrationEvent integrationEvent,
        DateTimeOffset now, CancellationToken cancellationToken);
    Task ReleaseAsync(ClaimedOrderWorkflow claim, string errorCategory, DateTimeOffset nextAttemptAtUtc,
        CancellationToken cancellationToken);
}

public sealed class OrderWorkflowRecoveryService(
    IOrderWorkflowRecoveryRepository repository,
    IInventoryReservationPort inventory,
    IKitchenPort kitchen,
    IPaymentPort payment,
    TimeProvider? timeProvider = null,
    ILogger<OrderWorkflowRecoveryService>? logger = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<bool> RecoverNextAsync(TimeSpan lease, TimeSpan retryDelay, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        ClaimedOrderWorkflow? claim = await repository.ClaimNextAsync(now, lease, cancellationToken);
        if (claim is null) return false;

        OrderAggregate order = claim.Order;
        Guid correlationId = order.WorkflowCorrelationId ?? order.Id;
        using IDisposable correlationScope = CorrelationContext.Push(correlationId.ToString("D"));
        using IDisposable? logScope = logger?.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId.ToString("D"),
            ["RecoveryStage"] = order.Status.ToString(),
            ["RecoveryAttempt"] = claim.AttemptCount
        });
        try
        {
            if (order.Status == OrderStatus.Submitted)
            {
                InventoryReservationResult reservation = await inventory.ReserveAsync(
                    order.OrganizationId, order.Id, order.BranchId, order.Lines, cancellationToken);
                IIntegrationEvent integrationEvent;
                if (!reservation.Reserved || reservation.ReservationId is null)
                {
                    order.Reject();
                    integrationEvent = new InventoryReservationRejectedV1(Guid.NewGuid(), correlationId,
                        clock.GetUtcNow(), order.Id, reservation.Reason ?? "Inventory could not be reserved.");
                }
                else
                {
                    order.MarkInventoryReserved();
                    integrationEvent = new InventoryReservedV1(Guid.NewGuid(), correlationId,
                        clock.GetUtcNow(), order.Id, reservation.ReservationId.Value);
                }
                if (!await repository.CommitAsync(claim, order, integrationEvent, clock.GetUtcNow(), cancellationToken))
                    throw new InvalidOperationException("The Order workflow recovery lease was lost before commit.");
                return true;
            }

            if (order.Status == OrderStatus.InventoryReserved)
            {
                KitchenTicketResult ticket = await kitchen.CreateTicketAsync(order.OrganizationId, order.RestaurantId,
                    order.Id, order.BranchId, order.Lines, cancellationToken);
                order.MarkKitchenAccepted();
                var integrationEvent = new KitchenTicketCreatedV1(Guid.NewGuid(), correlationId, clock.GetUtcNow(),
                    order.Id, ticket.TicketId, order.Lines.Select(line => new OrderLineSnapshot(
                        line.ProductId, line.Name, line.UnitPrice, line.Quantity, line.PreparationStation)).ToArray());
                if (!await repository.CommitAsync(claim, order, integrationEvent, clock.GetUtcNow(), cancellationToken))
                    throw new InvalidOperationException("The Order workflow recovery lease was lost before commit.");
                return true;
            }

            if (order.Status == OrderStatus.KitchenAccepted && !IsManualTender(order.WorkflowPaymentMethod))
            {
                string paymentMethod = order.WorkflowPaymentMethod
                    ?? throw new InvalidOperationException("A recoverable provider-payment order must retain its payment method.");
                PaymentResult paid = await payment.AuthorizeAsync(order.OrganizationId, order.RestaurantId,
                    order.BranchId, order.Id, order.TotalAmount, order.Currency, paymentMethod, cancellationToken);
                IIntegrationEvent integrationEvent;
                if (!paid.Completed && IsUncertain(paid.Outcome))
                {
                    if (paid.PaymentId is null)
                        throw new InvalidOperationException("An uncertain payment operation must identify its payment intent.");
                    order.MarkPaymentPending(paid.PaymentId.Value);
                    integrationEvent = new PaymentAuthorizationUncertainV1(Guid.NewGuid(), correlationId,
                        clock.GetUtcNow(), order.Id, paid.PaymentId,
                        paid.Reason ?? "Payment requires server-side reconciliation.");
                }
                else if (!paid.Completed || paid.PaymentId is null)
                {
                    // Both dependencies expose idempotent compensation endpoints. If a process dies
                    // between them, the next fenced attempt safely repeats the release before commit.
                    await inventory.ReleaseAsync(order.OrganizationId, order.Id, order.BranchId, cancellationToken);
                    await kitchen.CancelTicketAsync(order.OrganizationId, order.Id, order.BranchId, cancellationToken);
                    order.MarkPaymentFailed();
                    integrationEvent = new PaymentFailedV1(Guid.NewGuid(), correlationId, clock.GetUtcNow(), order.Id,
                        paid.Reason ?? "Payment was not completed.");
                }
                else
                {
                    order.MarkPaid(paid.PaymentId.Value);
                    integrationEvent = new PaymentCompletedV1(OrderPaymentEventIdentity.Completion(order.Id, paymentMethod), correlationId, clock.GetUtcNow(),
                        order.Id, paid.PaymentId.Value, order.TotalAmount, order.Currency, paymentMethod);
                }

                if (!await repository.CommitAsync(claim, order, integrationEvent, clock.GetUtcNow(), cancellationToken))
                    throw new InvalidOperationException("The Order workflow recovery lease was lost before commit.");
                return true;
            }

            throw new InvalidOperationException("The claimed order is not eligible for workflow recovery.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger?.LogWarning(exception,
                "Order workflow recovery dependency or fencing boundary failed; the durable claim will be released when still owned.");
            await repository.ReleaseAsync(claim, "dependency_unavailable", clock.GetUtcNow() + retryDelay,
                CancellationToken.None);
            throw;
        }
    }

    private static bool IsManualTender(string? method) => method is "cash_manual" or "promptpay_manual";

    internal static bool IsUncertain(string outcome) => outcome is
        "unknown" or "awaiting_token" or "authorizing" or "requires_action" or "capturing" or "capture_unknown"
        or "voiding" or "void_unknown";
}
