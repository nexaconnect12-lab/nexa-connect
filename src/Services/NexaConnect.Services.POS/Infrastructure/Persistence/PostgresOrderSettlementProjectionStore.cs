using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.POS.Application.OrderSettlements;
using Npgsql;

namespace NexaConnect.Services.POS.Infrastructure.Persistence;

public sealed class PostgresOrderSettlementProjectionStore(NpgsqlDataSource dataSource) : IOrderSettlementProjectionStore
{
    public async Task<OrderSettlementProjectionStatus> ProjectAsync(OrderManualTenderSettledV1 value,CancellationToken cancellationToken)
    {
        await using var connection=await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction=await connection.BeginTransactionAsync(cancellationToken);
        bool replayed=false;
        await using(var existing=new NpgsqlCommand("SELECT settlement_id,order_id,terminal_id,method,amount,btrim(currency) FROM pos_order_settlements WHERE event_id=$1 FOR UPDATE",connection,transaction))
        {
            existing.Parameters.AddWithValue(value.EventId);await using var reader=await existing.ExecuteReaderAsync(cancellationToken);
            if(await reader.ReadAsync(cancellationToken))
            {
                bool matches=reader.GetGuid(0)==value.SettlementId&&reader.GetGuid(1)==value.OrderId&&reader.GetGuid(2)==value.TerminalId&&reader.GetString(3)==value.Method&&reader.GetDecimal(4)==value.Amount&&reader.GetString(5)==value.Currency;
                if(!matches)throw new OrderSettlementProjectionConflictException("A settlement event identifier was reused with different content.");
                replayed=true;
            }
        }
        if(replayed)
        {
            await transaction.CommitAsync(cancellationToken);return OrderSettlementProjectionStatus.Replayed;
        }
        Guid? cashSessionId=null;
        await using(var scope=new NpgsqlCommand("SELECT session.id FROM terminals terminal JOIN stores store ON store.id=terminal.store_id JOIN shifts shift ON shift.store_id=store.id AND shift.terminal_id=terminal.id AND shift.opened_at_utc<=$4 AND (shift.closed_at_utc IS NULL OR shift.closed_at_utc>=$4) LEFT JOIN cash_sessions session ON session.shift_id=shift.id AND session.store_id=store.id AND session.opened_at_utc<=$4 AND (session.closed_at_utc IS NULL OR session.closed_at_utc>=$4) AND btrim(session.currency)=$5 WHERE terminal.id=$1 AND store.restaurant_id=$2 AND store.branch_id=$3",connection,transaction))
        {
            scope.Parameters.AddWithValue(value.TerminalId);scope.Parameters.AddWithValue(value.RestaurantId);scope.Parameters.AddWithValue(value.BranchId);scope.Parameters.AddWithValue(value.OccurredAtUtc);scope.Parameters.AddWithValue(value.Currency);
            object? result=await scope.ExecuteScalarAsync(cancellationToken);
            if(result is null)throw new OrderSettlementProjectionConflictException("The settlement has no matching terminal shift at its occurrence time.");
            cashSessionId=result is Guid id?id:null;
        }
        if(value.Method=="cash"&&cashSessionId is null)throw new OrderSettlementProjectionConflictException("Cash settlement has no matching THB cash session at its occurrence time.");
        if(value.Method=="promptpay_manual")cashSessionId=null;
        await using(var orderConflict=new NpgsqlCommand("SELECT event_id FROM pos_order_settlements WHERE order_id=$1",connection,transaction))
        {orderConflict.Parameters.AddWithValue(value.OrderId);if(await orderConflict.ExecuteScalarAsync(cancellationToken) is Guid other&&other!=value.EventId)throw new OrderSettlementProjectionConflictException("The Order already has a different POS settlement projection.");}
        await using(var insert=new NpgsqlCommand("INSERT INTO pos_order_settlements(event_id,settlement_id,order_id,organization_id,restaurant_id,branch_id,terminal_id,cash_session_id,method,amount,currency,occurred_at_utc,projected_at_utc) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,now())",connection,transaction))
        {
            insert.Parameters.AddWithValue(value.EventId);insert.Parameters.AddWithValue(value.SettlementId);insert.Parameters.AddWithValue(value.OrderId);insert.Parameters.AddWithValue(value.OrganizationId);insert.Parameters.AddWithValue(value.RestaurantId);insert.Parameters.AddWithValue(value.BranchId);insert.Parameters.AddWithValue(value.TerminalId);insert.Parameters.AddWithValue((object?)cashSessionId??DBNull.Value);insert.Parameters.AddWithValue(value.Method);insert.Parameters.AddWithValue(value.Amount);insert.Parameters.AddWithValue(value.Currency);insert.Parameters.AddWithValue(value.OccurredAtUtc);await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        if(value.Method=="cash")
        {
            await using var movement=new NpgsqlCommand("INSERT INTO cash_movements(id,cash_session_id,movement_type,amount,order_id,payment_id,reason_code,occurred_at_utc,recorded_by) VALUES($1,$2,'sale',$3,$4,$5,'ORDER_MANUAL_TENDER',$6,'order-settlement-consumer')",connection,transaction);
            movement.Parameters.AddWithValue(Guid.NewGuid());movement.Parameters.AddWithValue(cashSessionId!.Value);movement.Parameters.AddWithValue(value.Amount);movement.Parameters.AddWithValue(value.OrderId);movement.Parameters.AddWithValue(value.SettlementId);movement.Parameters.AddWithValue(value.OccurredAtUtc);await movement.ExecuteNonQueryAsync(cancellationToken);
            await using var reconcile=new NpgsqlCommand("UPDATE cash_sessions SET variance_amount=CASE WHEN status='closed' THEN actual_closing_amount-(opening_amount+COALESCE((SELECT SUM(CASE WHEN movement_type IN('sale','pay_in','float_adjustment') THEN amount ELSE -amount END) FROM cash_movements WHERE cash_session_id=cash_sessions.id),0)) ELSE variance_amount END,updated_at_utc=now(),concurrency_version=concurrency_version+1 WHERE id=$1",connection,transaction);
            reconcile.Parameters.AddWithValue(cashSessionId.Value);await reconcile.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);return OrderSettlementProjectionStatus.Applied;
    }
}
