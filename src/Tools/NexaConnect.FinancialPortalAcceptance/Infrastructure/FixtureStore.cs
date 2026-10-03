using Npgsql;
using RabbitMQ.Client;
namespace NexaConnect.FinancialPortalAcceptance.Infrastructure;

// Administration of exact run-owned disposable fixtures only; no production service references this tool.
internal sealed class FixtureStore(NpgsqlDataSource authorization,NpgsqlDataSource reporting)
{
    public async Task<bool> SourcesEmptyAsync(NpgsqlDataSource order,NpgsqlDataSource payment,CancellationToken ct)
    {
        await using var orders=order.CreateCommand("SELECT NOT EXISTS(SELECT 1 FROM orders)");
        await using var intents=payment.CreateCommand("SELECT NOT EXISTS(SELECT 1 FROM payment_intents)");
        return (bool)(await orders.ExecuteScalarAsync(ct))! && (bool)(await intents.ExecuteScalarAsync(ct))!;
    }
    public async Task RevokeReadAsync(string subject,CancellationToken ct)
    {
        await using var command=authorization.CreateCommand("UPDATE authorization_user_permission_overrides SET effect='deny' WHERE subject_id=$1 AND permission_code='reporting.sales.read' AND status='active'");
        command.Parameters.AddWithValue(subject);
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
