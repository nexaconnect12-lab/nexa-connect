using NexaConnect.Services.Order.Application.Workflow;

namespace NexaConnect.Services.Order.Application.Cancellations;

public sealed record CancelOrderCommand(Guid OrderId, Guid OrganizationId, Guid BranchId, Guid OperationId,
    string Reason, string ActorSubjectId, Guid AuthorizationDecisionId, Guid CorrelationId);
public sealed record OrderCancellationResult(Guid OrderId, Guid OperationId, string Status, bool Replayed);
public sealed record OrderCancellationRecord(Guid OrderId, Guid OrganizationId, Guid BranchId, Guid OperationId,
    string Reason, string ActorSubjectId, Guid AuthorizationDecisionId, Guid CorrelationId, string FromStatus,
    bool ReleaseInventory, bool CancelKitchen, string Status);
public sealed record ClaimedOrderCancellation(OrderCancellationRecord Cancellation, Guid ClaimId, int AttemptCount);

public interface IOrderCancellationRepository
{
    Task<(OrderCancellationRecord Cancellation, bool Replayed)?> BeginAsync(CancelOrderCommand command, DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<ClaimedOrderCancellation?> ClaimAsync(Guid? orderId, DateTimeOffset now, TimeSpan lease,
        CancellationToken cancellationToken);
    Task<bool> CompleteAsync(ClaimedOrderCancellation claim, DateTimeOffset now, CancellationToken cancellationToken);
    Task BlockAsync(ClaimedOrderCancellation claim, string category, DateTimeOffset now, CancellationToken cancellationToken);
    Task ReleaseAsync(ClaimedOrderCancellation claim, string category, DateTimeOffset nextAttemptAtUtc,
        CancellationToken cancellationToken);
    Task<OrderCancellationRecord?> GetAsync(Guid orderId, CancellationToken cancellationToken);
}

public sealed class OrderCancellationConflictException(string message) : InvalidOperationException(message);

public sealed class InMemoryOrderCancellationRepository(Application.Orders.InMemoryOrderApplicationService orders)
    : IOrderCancellationRepository
{
    private readonly Dictionary<Guid, OrderCancellationRecord> values = [];
    private readonly HashSet<Guid> claimed = [];
    private readonly object gate = new();

    public Task<(OrderCancellationRecord Cancellation, bool Replayed)?> BeginAsync(CancelOrderCommand command,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (values.TryGetValue(command.OrderId, out OrderCancellationRecord? existing))
            {
                if (existing.OperationId != command.OperationId || existing.Reason != command.Reason.Trim())
                    throw new OrderCancellationConflictException("The order already has a different cancellation request.");
                return Task.FromResult<(OrderCancellationRecord, bool)?>(new(existing, true));
            }
            Domain.OrderAggregate? order = orders.Get(command.OrderId);
            if (order is null || order.OrganizationId != command.OrganizationId || order.BranchId != command.BranchId)
                return Task.FromResult<(OrderCancellationRecord, bool)?>(null);
            Domain.OrderStatus from = order.BeginCancellation();
            var value = new OrderCancellationRecord(order.Id, order.OrganizationId, order.BranchId, command.OperationId,
                command.Reason.Trim(), command.ActorSubjectId, command.AuthorizationDecisionId, command.CorrelationId,
                from.ToString(), from is Domain.OrderStatus.InventoryReserved or Domain.OrderStatus.KitchenAccepted,
                from == Domain.OrderStatus.KitchenAccepted, "pending");
            values.Add(order.Id, value);
            return Task.FromResult<(OrderCancellationRecord, bool)?>(new(value, false));
        }
    }

    public Task<ClaimedOrderCancellation?> ClaimAsync(Guid? orderId, DateTimeOffset now, TimeSpan lease,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            OrderCancellationRecord? value = values.Values.FirstOrDefault(v => v.Status == "pending"
                && (orderId is null || v.OrderId == orderId) && !claimed.Contains(v.OrderId));
            if (value is null) return Task.FromResult<ClaimedOrderCancellation?>(null);
            claimed.Add(value.OrderId);
            return Task.FromResult<ClaimedOrderCancellation?>(new(value, Guid.NewGuid(), 1));
        }
    }

    public Task<bool> CompleteAsync(ClaimedOrderCancellation claim, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (!claimed.Remove(claim.Cancellation.OrderId)) return Task.FromResult(false);
            orders.Get(claim.Cancellation.OrderId)!.CompleteCancellation();
            values[claim.Cancellation.OrderId] = claim.Cancellation with { Status = "completed" };
            return Task.FromResult(true);
        }
    }

    public Task BlockAsync(ClaimedOrderCancellation claim, string category, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            claimed.Remove(claim.Cancellation.OrderId); orders.Get(claim.Cancellation.OrderId)!.RequireCancellationReview();
            values[claim.Cancellation.OrderId] = claim.Cancellation with { Status = "blocked" };
            return Task.CompletedTask;
        }
    }

    public Task ReleaseAsync(ClaimedOrderCancellation claim, string category, DateTimeOffset nextAttemptAtUtc,
        CancellationToken cancellationToken) { lock (gate) claimed.Remove(claim.Cancellation.OrderId); return Task.CompletedTask; }
    public Task<OrderCancellationRecord?> GetAsync(Guid orderId, CancellationToken cancellationToken)
    { lock (gate) return Task.FromResult(values.GetValueOrDefault(orderId)); }
}

public sealed class OrderCancellationApplicationService(IOrderCancellationRepository repository,
    IInventoryReservationPort inventory, IKitchenPort kitchen, TimeProvider? timeProvider = null,
    ILogger<OrderCancellationApplicationService>? logger = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<OrderCancellationResult?> RequestAsync(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        Validate(command);
        var begun = await repository.BeginAsync(command, clock.GetUtcNow(), cancellationToken);
        if (begun is null) return null;
        if (begun.Value.Cancellation.Status is "completed" or "blocked")
            return Result(begun.Value.Cancellation, true);
        await RecoverAsync(command.OrderId, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15), cancellationToken);
        OrderCancellationRecord current = await repository.GetAsync(command.OrderId, cancellationToken)
            ?? throw new InvalidOperationException("Cancellation disappeared after it was accepted.");
        return Result(current, begun.Value.Replayed);
    }

    public async Task<bool> RecoverNextAsync(TimeSpan lease, TimeSpan retryDelay, CancellationToken cancellationToken) =>
        await RecoverAsync(null, lease, retryDelay, cancellationToken);

    private async Task<bool> RecoverAsync(Guid? orderId, TimeSpan lease, TimeSpan retryDelay, CancellationToken cancellationToken)
    {
        ClaimedOrderCancellation? claim = await repository.ClaimAsync(orderId, clock.GetUtcNow(), lease, cancellationToken);
        if (claim is null) return false;
        try
        {
            if (claim.Cancellation.CancelKitchen)
                await kitchen.CancelTicketAsync(claim.Cancellation.OrganizationId, claim.Cancellation.OrderId,
                    claim.Cancellation.BranchId, cancellationToken);
            if (claim.Cancellation.ReleaseInventory)
                await inventory.ReleaseAsync(claim.Cancellation.OrganizationId, claim.Cancellation.OrderId,
                    claim.Cancellation.BranchId, cancellationToken);
            if (!await repository.CompleteAsync(claim, clock.GetUtcNow(), cancellationToken))
                throw new InvalidOperationException("The cancellation recovery lease was lost before commit.");
            logger?.LogInformation("Order cancellation completed {CancellationAttempt}", claim.AttemptCount);
            return true;
        }
        catch (OrderCancellationConflictException)
        {
            await repository.BlockAsync(claim, "kitchen_terminal_state", clock.GetUtcNow(), cancellationToken);
            logger?.LogWarning("Order cancellation requires operator review after Kitchen conflict");
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            await repository.ReleaseAsync(claim, "dependency_unavailable", clock.GetUtcNow() + retryDelay, cancellationToken);
            logger?.LogWarning("Order cancellation dependency unavailable; durable retry scheduled");
            return true;
        }
    }

    private static OrderCancellationResult Result(OrderCancellationRecord value, bool replayed) =>
        new(value.OrderId, value.OperationId, value.Status, replayed);

    private static void Validate(CancelOrderCommand command)
    {
        if (command.OrderId == Guid.Empty || command.OrganizationId == Guid.Empty || command.BranchId == Guid.Empty
            || command.OperationId == Guid.Empty || command.AuthorizationDecisionId == Guid.Empty
            || command.CorrelationId == Guid.Empty || string.IsNullOrWhiteSpace(command.ActorSubjectId))
            throw new ArgumentException("Cancellation scope, identity, actor, authorization and correlation are required.");
        string reason = command.Reason.Trim();
        if (reason.Length is < 1 or > 200 || reason.Any(char.IsControl))
            throw new ArgumentException("Cancellation reason must contain 1-200 printable characters.");
    }
}
