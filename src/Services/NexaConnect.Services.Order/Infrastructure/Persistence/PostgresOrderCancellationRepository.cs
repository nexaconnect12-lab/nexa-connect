using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Order.Application.Cancellations;
using Npgsql;
using NpgsqlTypes;

namespace NexaConnect.Services.Order.Infrastructure.Persistence;

public sealed class PostgresOrderCancellationRepository(NpgsqlDataSource dataSource) : IOrderCancellationRepository
{
    public async Task<(OrderCancellationRecord Cancellation, bool Replayed)?> BeginAsync(CancelOrderCommand command,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        string? orderStatus;
        await using (var order = new NpgsqlCommand("SELECT status FROM orders WHERE id=$1 AND organization_id=$2 AND branch_id=$3 FOR UPDATE", connection, transaction))
        {
            order.Parameters.AddWithValue(command.OrderId); order.Parameters.AddWithValue(command.OrganizationId);
            order.Parameters.AddWithValue(command.BranchId);
            orderStatus = await order.ExecuteScalarAsync(cancellationToken) as string;
        }
        if (orderStatus is null) { await transaction.RollbackAsync(cancellationToken); return null; }

        OrderCancellationRecord? existing = await ReadAsync(connection, transaction, command.OrderId, cancellationToken);
        if (existing is not null)
        {
            if (existing.OperationId != command.OperationId || existing.Reason != command.Reason.Trim())
                throw new OrderCancellationConflictException("The order already has a different cancellation request.");
            await transaction.CommitAsync(cancellationToken);
            return (existing, true);
        }
        if (orderStatus is not ("submitted" or "inventory_reserved" or "kitchen_accepted" or "accepted"))
            throw new OrderCancellationConflictException($"Order cannot be cancelled from {orderStatus}.");

        string from = orderStatus == "accepted" ? "kitchen_accepted" : orderStatus;
        bool releaseInventory = from is "inventory_reserved" or "kitchen_accepted";
        bool cancelKitchen = from == "kitchen_accepted";
        var value = new OrderCancellationRecord(command.OrderId, command.OrganizationId, command.BranchId,
            command.OperationId, command.Reason.Trim(), command.ActorSubjectId, command.AuthorizationDecisionId,
            command.CorrelationId, from, releaseInventory, cancelKitchen, "pending");
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO order_cancellations(order_id,organization_id,branch_id,operation_id,reason,actor_subject_id,
              authorization_decision_id,correlation_id,from_status,release_inventory,cancel_kitchen,status,
              next_attempt_at_utc,requested_at_utc,updated_at_utc)
            VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,'pending',$12,$12,$12)
            """, connection, transaction))
        {
            AddRecordParameters(insert, value); insert.Parameters.AddWithValue(now);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var update = new NpgsqlCommand("UPDATE orders SET status='cancellation_pending',workflow_recovery_next_attempt_at_utc=NULL,workflow_recovery_claim_id=NULL,workflow_recovery_locked_until_utc=NULL,updated_at_utc=$1,updated_by=$2,concurrency_version=concurrency_version+1 WHERE id=$3 AND status=$4", connection, transaction))
        {
            update.Parameters.AddWithValue(now); update.Parameters.AddWithValue(command.ActorSubjectId);
            update.Parameters.AddWithValue(command.OrderId); update.Parameters.AddWithValue(orderStatus);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new OrderCancellationConflictException("Order changed before cancellation could be recorded.");
        }
        var requested = new OrderCancellationRequestedV1(Guid.NewGuid(), command.CorrelationId, now, command.OrderId,
            command.OrganizationId, command.BranchId, command.OperationId, from);
        var audit = Audit(value, now, "order.cancellation.requested", "succeeded");
        await EnqueueAsync(connection, transaction, requested, "order.cancellation-requested.v1", cancellationToken);
        await EnqueueAsync(connection, transaction, audit, "order.audit.v1", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (value, false);
    }

    public async Task<ClaimedOrderCancellation?> ClaimAsync(Guid? orderId, DateTimeOffset now, TimeSpan lease,
        CancellationToken cancellationToken)
    {
        Guid claimId = Guid.NewGuid(); Guid? claimedOrder;
        await using (var command = dataSource.CreateCommand("""
            WITH candidate AS (
              SELECT order_id FROM order_cancellations
              WHERE status='pending' AND ($1::uuid IS NULL OR order_id=$1)
                AND next_attempt_at_utc <= $2 AND (locked_until_utc IS NULL OR locked_until_utc <= $2)
              ORDER BY next_attempt_at_utc,order_id FOR UPDATE SKIP LOCKED LIMIT 1)
            UPDATE order_cancellations value SET claim_id=$3,locked_until_utc=$4,
              attempt_count=attempt_count+1,last_error_category=NULL,updated_at_utc=$2
            FROM candidate WHERE value.order_id=candidate.order_id RETURNING value.order_id
            """))
        {
            command.Parameters.AddWithValue(NpgsqlDbType.Uuid, (object?)orderId ?? DBNull.Value);
            command.Parameters.AddWithValue(now); command.Parameters.AddWithValue(claimId); command.Parameters.AddWithValue(now + lease);
            claimedOrder = await command.ExecuteScalarAsync(cancellationToken) as Guid?;
        }
        if (claimedOrder is null) return null;
        OrderCancellationRecord value = await GetAsync(claimedOrder.Value, cancellationToken)
            ?? throw new InvalidOperationException("Claimed cancellation disappeared.");
        await using var attempts = dataSource.CreateCommand("SELECT attempt_count FROM order_cancellations WHERE order_id=$1 AND claim_id=$2");
        attempts.Parameters.AddWithValue(claimedOrder.Value); attempts.Parameters.AddWithValue(claimId);
        return new(value, claimId, Convert.ToInt32(await attempts.ExecuteScalarAsync(cancellationToken)));
    }

    public async Task<bool> CompleteAsync(ClaimedOrderCancellation claim, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var update = new NpgsqlCommand("""
            UPDATE order_cancellations SET status='completed',completed_at_utc=$1,claim_id=NULL,locked_until_utc=NULL,
              next_attempt_at_utc=NULL,updated_at_utc=$1
            WHERE order_id=$2 AND claim_id=$3 AND locked_until_utc>$1 AND status='pending'
            """, connection, transaction))
        {
            update.Parameters.AddWithValue(now); update.Parameters.AddWithValue(claim.Cancellation.OrderId);
            update.Parameters.AddWithValue(claim.ClaimId);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1) { await transaction.RollbackAsync(cancellationToken); return false; }
        }
        await using (var order = new NpgsqlCommand("UPDATE orders SET status='cancelled',updated_at_utc=$1,updated_by=$2,concurrency_version=concurrency_version+1 WHERE id=$3 AND status='cancellation_pending'", connection, transaction))
        {
            order.Parameters.AddWithValue(now); order.Parameters.AddWithValue(claim.Cancellation.ActorSubjectId);
            order.Parameters.AddWithValue(claim.Cancellation.OrderId);
            if (await order.ExecuteNonQueryAsync(cancellationToken) != 1) throw new OrderCancellationConflictException("Order left cancellation-pending state.");
        }
        var cancelled = new OrderCancelledV1(Guid.NewGuid(), claim.Cancellation.CorrelationId, now,
            claim.Cancellation.OrderId, claim.Cancellation.OrganizationId, claim.Cancellation.BranchId,
            claim.Cancellation.OperationId);
        await EnqueueAsync(connection, transaction, cancelled, "order.cancelled.v1", cancellationToken);
        await EnqueueAsync(connection, transaction, Audit(claim.Cancellation, now, "order.cancelled", "succeeded"), "order.audit.v1", cancellationToken);
        await transaction.CommitAsync(cancellationToken); return true;
    }

    public async Task BlockAsync(ClaimedOrderCancellation claim, string category, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var update = new NpgsqlCommand("UPDATE order_cancellations SET status='blocked',claim_id=NULL,locked_until_utc=NULL,next_attempt_at_utc=NULL,last_error_category=$1,updated_at_utc=$2 WHERE order_id=$3 AND claim_id=$4", connection, transaction))
        {
            update.Parameters.AddWithValue(category); update.Parameters.AddWithValue(now);
            update.Parameters.AddWithValue(claim.Cancellation.OrderId); update.Parameters.AddWithValue(claim.ClaimId);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("Cancellation claim was lost.");
        }
        await using (var order = new NpgsqlCommand("UPDATE orders SET status='cancellation_review',updated_at_utc=$1,updated_by=$2,concurrency_version=concurrency_version+1 WHERE id=$3 AND status='cancellation_pending'", connection, transaction))
        { order.Parameters.AddWithValue(now); order.Parameters.AddWithValue(claim.Cancellation.ActorSubjectId); order.Parameters.AddWithValue(claim.Cancellation.OrderId); await order.ExecuteNonQueryAsync(cancellationToken); }
        var blocked = new OrderCancellationReviewRequiredV1(Guid.NewGuid(), claim.Cancellation.CorrelationId, now,
            claim.Cancellation.OrderId, claim.Cancellation.OrganizationId, claim.Cancellation.BranchId,
            claim.Cancellation.OperationId, category);
        await EnqueueAsync(connection, transaction, blocked, "order.cancellation-review-required.v1", cancellationToken);
        await EnqueueAsync(connection, transaction, Audit(claim.Cancellation, now, "order.cancellation.review-required", "failed"), "order.audit.v1", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ReleaseAsync(ClaimedOrderCancellation claim, string category, DateTimeOffset nextAttemptAtUtc,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("UPDATE order_cancellations SET claim_id=NULL,locked_until_utc=NULL,next_attempt_at_utc=$1,last_error_category=$2,updated_at_utc=now() WHERE order_id=$3 AND claim_id=$4 AND status='pending'");
        command.Parameters.AddWithValue(nextAttemptAtUtc); command.Parameters.AddWithValue(category);
        command.Parameters.AddWithValue(claim.Cancellation.OrderId); command.Parameters.AddWithValue(claim.ClaimId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<OrderCancellationRecord?> GetAsync(Guid orderId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await ReadAsync(connection, null, orderId, cancellationToken);
    }

    private static async Task<OrderCancellationRecord?> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        Guid orderId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT order_id,organization_id,branch_id,operation_id,reason,actor_subject_id,authorization_decision_id,correlation_id,from_status,release_inventory,cancel_kitchen,status FROM order_cancellations WHERE order_id=$1", connection, transaction);
        command.Parameters.AddWithValue(orderId); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
            reader.GetGuid(3), reader.GetString(4), reader.GetString(5), reader.GetGuid(6), reader.GetGuid(7),
            reader.GetString(8), reader.GetBoolean(9), reader.GetBoolean(10), reader.GetString(11)) : null;
    }

    private static void AddRecordParameters(NpgsqlCommand command, OrderCancellationRecord value)
    {
        command.Parameters.AddWithValue(value.OrderId); command.Parameters.AddWithValue(value.OrganizationId);
        command.Parameters.AddWithValue(value.BranchId); command.Parameters.AddWithValue(value.OperationId);
        command.Parameters.AddWithValue(value.Reason); command.Parameters.AddWithValue(value.ActorSubjectId);
        command.Parameters.AddWithValue(value.AuthorizationDecisionId); command.Parameters.AddWithValue(value.CorrelationId);
        command.Parameters.AddWithValue(value.FromStatus); command.Parameters.AddWithValue(value.ReleaseInventory);
        command.Parameters.AddWithValue(value.CancelKitchen);
    }

    private static PlatformAuditEventV1 Audit(OrderCancellationRecord value, DateTimeOffset now, string action, string outcome) =>
        new(Guid.NewGuid(), value.CorrelationId, now, value.ActorSubjectId, value.OrganizationId, action,
            "order", value.OrderId.ToString("D"), outcome);

    private static async Task EnqueueAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        IIntegrationEvent value, string eventType, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("INSERT INTO outbox_messages(id,event_type,contract_version,aggregate_type,aggregate_id,payload,correlation_id,occurred_at_utc) VALUES($1,$2,1,'Order',$3,$4::jsonb,$5,$6)", connection, transaction);
        Guid aggregateId = value switch
        {
            OrderCancellationRequestedV1 e => e.OrderId, OrderCancelledV1 e => e.OrderId,
            OrderCancellationReviewRequiredV1 e => e.OrderId,
            PlatformAuditEventV1 e => Guid.Parse(e.ResourceId), _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
        command.Parameters.AddWithValue(value.EventId); command.Parameters.AddWithValue(eventType);
        command.Parameters.AddWithValue(aggregateId); command.Parameters.AddWithValue(JsonSerializer.Serialize(value, value.GetType()));
        command.Parameters.AddWithValue(value.CorrelationId.ToString("D")); command.Parameters.AddWithValue(value.OccurredAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
