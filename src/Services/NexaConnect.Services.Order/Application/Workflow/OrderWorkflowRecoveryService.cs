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

            throw new InvalidOperationException("Only submitted or inventory-reserved orders can be recovered.");
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
}
