using Npgsql;
using RabbitMQ.Client;
namespace NexaConnect.FinancialPortalAcceptance.Infrastructure;

// Administration of exact run-owned disposable fixtures only; no production service references this tool.
internal sealed class FixtureStore(NpgsqlDataSource authorization,NpgsqlDataSource reporting)
{
    public async Task SetUnpaidOrderTimeAsync(NpgsqlDataSource order,Guid id,DateTimeOffset at,CancellationToken ct)
    {
        // Fixture-only timestamp before any immutable receipt/publication exists.
        await using var command=order.CreateCommand("UPDATE orders SET created_at_utc=$1 WHERE id=$2 AND status='kitchen_accepted' AND receipt_snapshot IS NULL");
        command.Parameters.AddWithValue(at);command.Parameters.AddWithValue(id);
        if(await command.ExecuteNonQueryAsync(ct)!=1)throw new InvalidOperationException();
    }
    public async Task<PosFixtureIds> CreateHistoricalCashAsync(NpgsqlDataSource pos,Guid restaurant,Guid branch,string subject,DateTimeOffset from,CancellationToken ct)
    {
        // Fresh synthetic historical snapshot, never a rewrite of existing financial history.
        await using var connection=await pos.OpenConnectionAsync(ct);
        await using var tx=await connection.BeginTransactionAsync(ct);
        await using(var empty=new NpgsqlCommand("SELECT NOT EXISTS(SELECT 1 FROM stores)",connection,tx))
            if(!(bool)(await empty.ExecuteScalarAsync(ct))!)throw new InvalidOperationException();
        Guid store=Guid.NewGuid(),terminal=Guid.NewGuid(),shift=Guid.NewGuid(),session=Guid.NewGuid();
        async Task Insert(string sql,params object[] values)
        { await using var command=new NpgsqlCommand(sql,connection,tx);foreach(var value in values)command.Parameters.AddWithValue(value);await command.ExecuteNonQueryAsync(ct); }
        await Insert("INSERT INTO stores(id,restaurant_id,branch_id,code,name,operational_status,created_at_utc,created_by,updated_at_utc,updated_by) VALUES($1,$2,$3,'fixture','Synthetic drawer','active',$4,$5,$4,$5)",store,restaurant,branch,from,subject);
        await Insert("INSERT INTO terminals(id,restaurant_id,store_id,code,device_type,registration_status,registered_at_utc,created_at_utc,updated_at_utc) VALUES($1,$2,$3,'fixture','pos','active',$4,$4,$4)",terminal,restaurant,store,from);
        await Insert("INSERT INTO shifts(id,store_id,terminal_id,employee_identity_subject_id,shift_number,authorization_decision_id,close_authorization_decision_id,status,opened_at_utc,closed_at_utc,opened_by,closed_by,created_at_utc,updated_at_utc) VALUES($1,$2,$3,$4,'DAY',$7,$8,'closed',$5,$6,$4,$4,$5,$6)",shift,store,terminal,subject,from.AddHours(8),from.AddHours(18),Guid.NewGuid(),Guid.NewGuid());
        await Insert("INSERT INTO cash_sessions(id,store_id,shift_id,currency,opening_amount,expected_closing_amount,actual_closing_amount,variance_amount,status,opened_at_utc,closed_at_utc,created_at_utc,updated_at_utc) VALUES($1,$2,$3,'THB',100,100,95,-5,'closed',$4,$5,$4,$5)",session,store,shift,from.AddHours(8),from.AddHours(18));
        Guid openShift=Guid.NewGuid(),olderTerminal=Guid.NewGuid(),olderSession=Guid.NewGuid();
        await Insert("INSERT INTO terminals(id,restaurant_id,store_id,code,device_type,registration_status,registered_at_utc,created_at_utc,updated_at_utc) VALUES($1,$2,$3,'older','pos','active',$4,$4,$4)",olderTerminal,restaurant,store,from.AddDays(-1));
        await Insert("INSERT INTO shifts(id,store_id,terminal_id,employee_identity_subject_id,shift_number,authorization_decision_id,status,opened_at_utc,opened_by,created_at_utc,updated_at_utc) VALUES($1,$2,$3,$4,'OLDER',$6,'open',$5,$4,$5,$5)",openShift,store,olderTerminal,subject,from.AddDays(-1),Guid.NewGuid());
        await Insert("INSERT INTO cash_sessions(id,store_id,shift_id,currency,opening_amount,status,opened_at_utc,created_at_utc,updated_at_utc) VALUES($1,$2,$3,'THB',100,'open',$4,$4,$4)",olderSession,store,openShift,from.AddDays(-1));
        await tx.CommitAsync(ct);
        return new(store,terminal,session,openShift,olderTerminal,olderSession);
    }
    public async Task<bool> SourcesEmptyAsync(NpgsqlDataSource order,NpgsqlDataSource payment,CancellationToken ct)
    {
        await using var orders=order.CreateCommand("SELECT NOT EXISTS(SELECT 1 FROM orders)");
        await using var intents=payment.CreateCommand("SELECT NOT EXISTS(SELECT 1 FROM payment_intents)");
        return (bool)(await orders.ExecuteScalarAsync(ct))! && (bool)(await intents.ExecuteScalarAsync(ct))!;
    }
    public async Task RevokeReadAsync(string subject,CancellationToken ct,string permission="reporting.sales.read")
    {
        await using var command=authorization.CreateCommand("UPDATE authorization_user_permission_overrides SET effect='deny' WHERE subject_id=$1 AND permission_code=$2 AND status='active'");
        command.Parameters.AddWithValue(subject);
        command.Parameters.AddWithValue(permission);
        if(await command.ExecuteNonQueryAsync(ct)!=1)throw new InvalidOperationException();
    }
    public async Task WaitForBindingsAsync(IChannel channel,CancellationToken ct)
    {
        // The runner also validates actual broker bindings before the browser starts.
        foreach(string queue in new[]{"nexaconnect.reporting.order-sales.v1","nexaconnect.reporting.payment-refunds.v1"})
            await channel.QueueDeclarePassiveAsync(queue,ct);
    }
    public async Task WaitForOutboxAsync(NpgsqlDataSource source,CancellationToken ct)
    {
        while(true)
        {
            await using var command=source.CreateCommand("SELECT count(*) FROM outbox_messages WHERE published_at_utc IS NULL");
            if(Convert.ToInt64(await command.ExecuteScalarAsync(ct))==0)return;
            await Task.Delay(100,ct);
        }
    }
    public async Task WaitForFactsAsync(CancellationToken ct)
    {
        while(true)
        {
            await using var command=reporting.CreateCommand("SELECT (SELECT count(*) FROM sales_facts)=1 AND (SELECT count(*) FROM payment_facts)=1 AND (SELECT count(*) FROM refund_facts)=1");
            if((bool)(await command.ExecuteScalarAsync(ct))!)return;
            await Task.Delay(100,ct);
        }
    }
}

internal sealed record PosFixtureIds(Guid StoreId,Guid TerminalId,Guid ClosedSessionId,Guid OpenShiftId,Guid OlderTerminalId,Guid OpenSessionId);
