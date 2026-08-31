using System.Text.Json;
using Npgsql;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Order.Application.Workflow;
using NexaConnect.Services.Order.Application.PaymentReviews;
using NexaConnect.Services.Order.Domain;

namespace NexaConnect.Services.Order.Infrastructure.Persistence;

public sealed class PostgresOrderRepository(NpgsqlDataSource dataSource)
    : IOrderRepository, ITransactionalOrderRepository, IIdempotentOrderRepository, IOrderLookup, IPaymentReviewRepository, IPaymentReviewHistoryRepository
{
    public async Task<IReadOnlyCollection<PaymentReviewHistoryEntry>> ReadHistoryAsync(Guid organizationId,Guid orderId,CancellationToken cancellationToken)
    {
        await using var command=dataSource.CreateCommand("SELECT id,action,reason,actor_subject_id,authorization_decision_id,concurrency_version,occurred_at_utc FROM order_payment_review_history WHERE organization_id=$1 AND order_id=$2 ORDER BY concurrency_version DESC LIMIT 100");
        command.Parameters.AddWithValue(organizationId);command.Parameters.AddWithValue(orderId);
        var result=new List<PaymentReviewHistoryEntry>();await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken))result.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetGuid(4),reader.GetInt64(5),reader.GetFieldValue<DateTimeOffset>(6)));
        return result;
    }
    public async Task SaveAsync(OrderAggregate order, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await SaveOrderAsync(connection, null, order, cancellationToken);
    }

    public async Task SaveWithEventAsync(OrderAggregate order, IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SaveOrderAsync(connection, transaction, order, cancellationToken);
        var payload = JsonSerializer.SerializeToDocument(integrationEvent, integrationEvent.GetType()).RootElement;
        await using var command = new NpgsqlCommand("""
            INSERT INTO outbox_messages (id,event_type,contract_version,aggregate_type,aggregate_id,payload,correlation_id,occurred_at_utc)
            VALUES (@id,@type,@version,@aggregate_type,@aggregate_id,@payload::jsonb,@correlation_id,@occurred_at)
            ON CONFLICT (id) DO NOTHING
            """, connection, transaction);
        command.Parameters.AddWithValue("id", integrationEvent.EventId);
        command.Parameters.AddWithValue("type", EventType(integrationEvent));
        command.Parameters.AddWithValue("version", 1);
        command.Parameters.AddWithValue("aggregate_type", "Order");
        command.Parameters.AddWithValue("aggregate_id", order.Id);
        command.Parameters.AddWithValue("payload", payload.GetRawText());
        command.Parameters.AddWithValue("correlation_id", integrationEvent.CorrelationId.ToString());
        command.Parameters.AddWithValue("occurred_at", integrationEvent.OccurredAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
        if(integrationEvent is OrderPaymentReviewRequiredV1 review)
        {
            await using var reviewCommand=new NpgsqlCommand("""
                INSERT INTO order_payment_reviews(order_id,organization_id,branch_id,payment_intent_id,status,reason,concurrency_version,created_at_utc,updated_at_utc)
                VALUES($1,$2,$3,$4,'open',$5,1,$6,$6)
                ON CONFLICT(order_id) DO NOTHING
                """,connection,transaction);
            reviewCommand.Parameters.AddWithValue(order.Id);reviewCommand.Parameters.AddWithValue(order.OrganizationId);
            reviewCommand.Parameters.AddWithValue(order.BranchId);reviewCommand.Parameters.AddWithValue(review.PaymentIntentId);
            reviewCommand.Parameters.AddWithValue(review.Reason);reviewCommand.Parameters.AddWithValue(review.OccurredAtUtc);
            await reviewCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<PaymentReviewCase>> ListOpenAsync(Guid organizationId,Guid branchId,int limit,CancellationToken cancellationToken)
    {
        await using var command=dataSource.CreateCommand("SELECT order_id,organization_id,branch_id,payment_intent_id,CASE WHEN status='resolving' AND resolution_locked_until_utc<=now() THEN 'open' ELSE status END,reason,resolution,concurrency_version,created_at_utc,updated_at_utc FROM order_payment_reviews WHERE organization_id=$1 AND branch_id=$2 AND (status='open' OR (status='resolving' AND resolution_locked_until_utc<=now())) ORDER BY created_at_utc LIMIT $3");
        command.Parameters.AddWithValue(organizationId);command.Parameters.AddWithValue(branchId);command.Parameters.AddWithValue(limit);
        var values=new List<PaymentReviewCase>();await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken))values.Add(ReadReview(reader));return values;
    }

    public async Task<PaymentReviewCase?> GetReviewAsync(Guid organizationId,Guid orderId,CancellationToken cancellationToken)
    {
        await using var command=dataSource.CreateCommand("SELECT order_id,organization_id,branch_id,payment_intent_id,CASE WHEN status='resolving' AND resolution_locked_until_utc<=now() THEN 'open' ELSE status END,reason,resolution,concurrency_version,created_at_utc,updated_at_utc FROM order_payment_reviews WHERE organization_id=$1 AND order_id=$2");
        command.Parameters.AddWithValue(organizationId);command.Parameters.AddWithValue(orderId);await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)?ReadReview(reader):null;
    }

    public async Task<Guid?> ClaimResolutionAsync(PaymentReviewCase review,string resolution,string actor,DateTimeOffset now,CancellationToken cancellationToken)
    {
        Guid claimId=Guid.NewGuid();
        await using var command=dataSource.CreateCommand("UPDATE order_payment_reviews SET status='resolving',resolution=$1,resolved_by=$2,resolution_locked_until_utc=$3,resolution_claim_id=$4,updated_at_utc=$5,concurrency_version=concurrency_version+1 WHERE organization_id=$6 AND order_id=$7 AND concurrency_version=$8 AND (status='open' OR (status='resolving' AND resolution_locked_until_utc<=$5 AND resolution=$1)) RETURNING resolution_claim_id");
        command.Parameters.AddWithValue(resolution);command.Parameters.AddWithValue(actor);command.Parameters.AddWithValue(now.AddMinutes(2));command.Parameters.AddWithValue(claimId);command.Parameters.AddWithValue(now);
        command.Parameters.AddWithValue(review.OrganizationId);command.Parameters.AddWithValue(review.OrderId);command.Parameters.AddWithValue(review.ConcurrencyVersion);
        return await command.ExecuteScalarAsync(cancellationToken) is Guid claimed?claimed:null;
    }

    public async Task ReleaseResolutionAsync(PaymentReviewCase review,Guid claimId,CancellationToken cancellationToken)
    {
        await using var command=dataSource.CreateCommand("UPDATE order_payment_reviews SET status='open',resolution=NULL,resolved_by=NULL,resolution_locked_until_utc=NULL,resolution_claim_id=NULL WHERE organization_id=$1 AND order_id=$2 AND resolution_claim_id=$3 AND status='resolving'");
        command.Parameters.AddWithValue(review.OrganizationId);command.Parameters.AddWithValue(review.OrderId);command.Parameters.AddWithValue(claimId);await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> ResolveAsync(OrderAggregate order,PaymentReviewCase review,string resolution,string reason,string actor,
        Guid claimId,OrderPaymentReviewResolvedV1 integrationEvent,PlatformAuditEventV1 audit,CancellationToken cancellationToken)
    {
        await using var connection=await dataSource.OpenConnectionAsync(cancellationToken);await using var transaction=await connection.BeginTransactionAsync(cancellationToken);
        await using(var update=new NpgsqlCommand("UPDATE order_payment_reviews SET status=CASE WHEN $1='escalate' THEN 'open' ELSE 'resolved' END,resolution=$1,resolution_reason=$2,resolved_by=$3,resolved_at_utc=CASE WHEN $1='escalate' THEN resolved_at_utc ELSE $4 END,resolution_locked_until_utc=NULL,resolution_claim_id=NULL,updated_at_utc=$4 WHERE organization_id=$5 AND order_id=$6 AND status='resolving' AND resolution=$1 AND concurrency_version=$7 AND resolution_claim_id=$8",connection,transaction))
        {
            update.Parameters.AddWithValue(resolution);update.Parameters.AddWithValue(reason);update.Parameters.AddWithValue(actor);update.Parameters.AddWithValue(integrationEvent.OccurredAtUtc);
            update.Parameters.AddWithValue(order.OrganizationId);update.Parameters.AddWithValue(order.Id);update.Parameters.AddWithValue(integrationEvent.ConcurrencyVersion);update.Parameters.AddWithValue(claimId);
            if(await update.ExecuteNonQueryAsync(cancellationToken)!=1){await transaction.RollbackAsync(cancellationToken);return false;}
        }
        await SaveOrderAsync(connection,transaction,order,cancellationToken);
        await using(var history=new NpgsqlCommand("INSERT INTO order_payment_review_history(id,order_id,organization_id,action,reason,actor_subject_id,authorization_decision_id,concurrency_version,occurred_at_utc) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9)",connection,transaction))
        {history.Parameters.AddWithValue(Guid.NewGuid());history.Parameters.AddWithValue(order.Id);history.Parameters.AddWithValue(order.OrganizationId);history.Parameters.AddWithValue(resolution);history.Parameters.AddWithValue(reason);history.Parameters.AddWithValue(actor);history.Parameters.AddWithValue(integrationEvent.AuthorizationDecisionId);history.Parameters.AddWithValue(integrationEvent.ConcurrencyVersion);history.Parameters.AddWithValue(integrationEvent.OccurredAtUtc);await history.ExecuteNonQueryAsync(cancellationToken);}
        await EnqueueAsync(connection,transaction,integrationEvent,"order.payment-review-resolved.v1",order.Id,cancellationToken);
        await EnqueueAsync(connection,transaction,audit,"order.audit.v1",order.Id,cancellationToken);
        await transaction.CommitAsync(cancellationToken);return true;
    }

    private static PaymentReviewCase ReadReview(NpgsqlDataReader reader)=>new(reader.GetGuid(0),reader.GetGuid(1),reader.GetGuid(2),reader.GetGuid(3),reader.GetString(4),reader.GetString(5),reader.IsDBNull(6)?null:reader.GetString(6),reader.GetInt64(7),reader.GetFieldValue<DateTimeOffset>(8),reader.GetFieldValue<DateTimeOffset>(9));

    private static async Task EnqueueAsync(NpgsqlConnection connection,NpgsqlTransaction transaction,IIntegrationEvent value,string eventType,Guid aggregateId,CancellationToken cancellationToken)
    {
        await using var command=new NpgsqlCommand("INSERT INTO outbox_messages(id,event_type,contract_version,aggregate_type,aggregate_id,payload,correlation_id,occurred_at_utc) VALUES($1,$2,1,'Order',$3,$4::jsonb,$5,$6)",connection,transaction);
        command.Parameters.AddWithValue(value.EventId);command.Parameters.AddWithValue(eventType);command.Parameters.AddWithValue(aggregateId);
        command.Parameters.AddWithValue(JsonSerializer.Serialize(value,value.GetType()));command.Parameters.AddWithValue(value.CorrelationId.ToString("D"));command.Parameters.AddWithValue(value.OccurredAtUtc);await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<OrderAggregate?> FindByIdempotencyKeyAsync(Guid restaurantId, string key, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT resource_id FROM idempotency_records WHERE operation_scope = @scope AND idempotency_key = @key AND expires_at_utc > now()");
        command.Parameters.AddWithValue("scope", $"order:{restaurantId:N}");
        command.Parameters.AddWithValue("key", key);
        var resource = await command.ExecuteScalarAsync(cancellationToken);
        return resource is Guid id ? await GetAsync(id, cancellationToken) : null;
    }

    public async Task<OrderAggregate?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT organization_id, restaurant_id, branch_id, currency, status, order_number, channel, service_type, payment_intent_id FROM orders WHERE id=@id", connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        if (reader.IsDBNull(0)) throw new InvalidOperationException($"Order {id} has no organization ownership metadata; backfill it before processing.");
        var organization = reader.GetGuid(0); var restaurant = reader.GetGuid(1); var branch = reader.GetGuid(2); var currency = reader.GetString(3).Trim();
        var status = reader.GetString(4); var orderNumber = reader.GetString(5); var channel = reader.GetString(6); var serviceType = reader.GetString(7);
        Guid? paymentIntentId = reader.IsDBNull(8) ? null : reader.GetGuid(8);
        await reader.CloseAsync();
        await using var linesCommand = new NpgsqlCommand("SELECT product_id, name_snapshot, unit_price, quantity, COALESCE(notes,'') FROM order_lines WHERE order_id=@id ORDER BY line_number", connection);
        linesCommand.Parameters.AddWithValue("id", id);
        var lines = new List<OrderLine>();
        await using var linesReader = await linesCommand.ExecuteReaderAsync(cancellationToken);
        while (await linesReader.ReadAsync(cancellationToken)) lines.Add(new OrderLine(linesReader.GetGuid(0), linesReader.GetString(1), linesReader.GetDecimal(2), (int)linesReader.GetDecimal(3), linesReader.GetString(4)));
        var order = OrderAggregate.Create(id, organization, branch, lines, currency, restaurant, channel, serviceType, orderNumber);
        order.RestorePaymentIntent(paymentIntentId);
        ApplyStatus(order, status);
        return order;
    }

    private static async Task SaveOrderAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, OrderAggregate order, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO orders (id,organization_id,restaurant_id,branch_id,payment_intent_id,order_number,currency,channel,service_type,subtotal_amount,total_amount,status,created_at_utc,created_by,updated_at_utc,updated_by)
            VALUES (@id,@organization,@restaurant,@branch,@payment_intent,@number,@currency,@channel,@service,@subtotal,@total,@status,@now,'order-service',@now,'order-service')
            ON CONFLICT (id) DO UPDATE SET status=EXCLUDED.status,payment_intent_id=COALESCE(orders.payment_intent_id,EXCLUDED.payment_intent_id),total_amount=EXCLUDED.total_amount,updated_at_utc=EXCLUDED.updated_at_utc,updated_by=EXCLUDED.updated_by,concurrency_version=orders.concurrency_version+1
            WHERE orders.organization_id=EXCLUDED.organization_id
              AND (orders.payment_intent_id IS NULL OR EXCLUDED.payment_intent_id IS NULL OR orders.payment_intent_id=EXCLUDED.payment_intent_id)
              AND (orders.status NOT IN ('completed','cancelled') OR orders.status=EXCLUDED.status)
            """, connection, transaction);
        var now = DateTime.UtcNow;
        command.Parameters.AddWithValue("id", order.Id); command.Parameters.AddWithValue("organization", order.OrganizationId); command.Parameters.AddWithValue("restaurant", order.RestaurantId); command.Parameters.AddWithValue("branch", order.BranchId);
        command.Parameters.AddWithValue("payment_intent", (object?)order.PaymentIntentId ?? DBNull.Value);
        command.Parameters.AddWithValue("number", order.OrderNumber); command.Parameters.AddWithValue("currency", order.Currency); command.Parameters.AddWithValue("channel", order.Channel); command.Parameters.AddWithValue("service", order.ServiceType);
        command.Parameters.AddWithValue("subtotal", order.TotalAmount); command.Parameters.AddWithValue("total", order.TotalAmount); command.Parameters.AddWithValue("status", ToDbStatus(order.Status)); command.Parameters.AddWithValue("now", now);
        int affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
            throw new InvalidOperationException($"Order {order.Id} has already reached a conflicting terminal state.");
        await using var delete = new NpgsqlCommand("DELETE FROM order_lines WHERE order_id=@id", connection, transaction); delete.Parameters.AddWithValue("id", order.Id); await delete.ExecuteNonQueryAsync(cancellationToken);
        for (var i = 0; i < order.Lines.Count; i++)
        {
            var line = order.Lines[i];
            await using var lineCommand = new NpgsqlCommand("INSERT INTO order_lines (id,restaurant_id,branch_id,order_id,line_number,product_id,sku_snapshot,name_snapshot,quantity,unit_price,line_total,status,created_at_utc,created_by,updated_at_utc,updated_by) VALUES (@id,@restaurant,@branch,@order,@number,@product,@sku,@name,@quantity,@unit,@total,'active',@now,'order-service',@now,'order-service')", connection, transaction);
            lineCommand.Parameters.AddWithValue("id", Guid.NewGuid()); lineCommand.Parameters.AddWithValue("restaurant", order.RestaurantId); lineCommand.Parameters.AddWithValue("branch", order.BranchId); lineCommand.Parameters.AddWithValue("order", order.Id); lineCommand.Parameters.AddWithValue("number", i + 1); lineCommand.Parameters.AddWithValue("product", line.ProductId); lineCommand.Parameters.AddWithValue("sku", line.ProductId.ToString("N")); lineCommand.Parameters.AddWithValue("name", line.Name); lineCommand.Parameters.AddWithValue("quantity", (decimal)line.Quantity); lineCommand.Parameters.AddWithValue("unit", line.UnitPrice); lineCommand.Parameters.AddWithValue("total", line.Total); lineCommand.Parameters.AddWithValue("now", now);
            await lineCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        if (!string.IsNullOrWhiteSpace(order.IdempotencyKey))
        {
            await using var idem = new NpgsqlCommand("INSERT INTO idempotency_records (operation_scope,idempotency_key,request_hash,response_status,response_body,resource_id,created_at_utc,expires_at_utc) VALUES (@scope,@key,'order',201,NULL,@resource,@now,@expires) ON CONFLICT DO NOTHING", connection, transaction);
            idem.Parameters.AddWithValue("scope", $"order:{order.RestaurantId:N}"); idem.Parameters.AddWithValue("key", order.IdempotencyKey); idem.Parameters.AddWithValue("resource", order.Id); idem.Parameters.AddWithValue("now", now); idem.Parameters.AddWithValue("expires", now.AddDays(1)); await idem.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static string ToDbStatus(OrderStatus status) => status switch { OrderStatus.Paid => "completed", OrderStatus.PaymentFailed or OrderStatus.Rejected => "cancelled", OrderStatus.PaymentPending => "payment_pending", OrderStatus.PaymentReview => "payment_review", OrderStatus.KitchenAccepted => "accepted", OrderStatus.InventoryReserved => "accepted", _ => status.ToString().ToLowerInvariant() };
    private static string EventType(IIntegrationEvent integrationEvent) => integrationEvent switch
    {
        OrderPaymentReviewRequiredV1 => "order.payment-review-required.v1",
        OrderPaymentReviewResolvedV1 => "order.payment-review-resolved.v1",
        _ => integrationEvent.GetType().Name
    };
    private static void ApplyStatus(OrderAggregate order, string status) { if (status == "submitted") order.Submit(); else if (status == "accepted") { order.Submit(); order.MarkInventoryReserved(); order.MarkKitchenAccepted(); } else if (status is "payment_pending" or "payment_review") { order.Submit(); order.MarkInventoryReserved(); order.MarkKitchenAccepted(); order.MarkPaymentPending(); if(status=="payment_review")order.MarkPaymentReview(); } else if (status == "completed") { order.Submit(); order.MarkInventoryReserved(); order.MarkKitchenAccepted(); order.MarkPaid(); } else if (status == "cancelled") order.Reject(); }
}
