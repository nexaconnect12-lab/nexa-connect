using Npgsql;
namespace NexaConnect.CashClosePortalAcceptance.Infrastructure;

// Only run-scoped disposable fixture administration. No production service references this tool.
internal sealed class FixtureStore(NpgsqlDataSource pos, NpgsqlDataSource authorization)
{
    public async Task<bool> IsEmptyAsync()
    {
        await using var command = pos.CreateCommand("SELECT NOT EXISTS(SELECT 1 FROM stores)");
        return (bool)(await command.ExecuteScalarAsync())!;
    }
    public async Task<DateTimeOffset> NowAsync()
    {
        await using var command = pos.CreateCommand("SELECT clock_timestamp()");
        return new DateTimeOffset((DateTime)(await command.ExecuteScalarAsync())!);
    }
    public async Task CreateStoreAsync(Guid store, Guid restaurant, Guid branch)
    {
        await using var command = pos.CreateCommand("INSERT INTO stores(id,restaurant_id,branch_id,code,name,operational_status,created_at_utc,created_by,updated_at_utc,updated_by) VALUES($1,$2,$3,$4,'Acceptance','active',now(),'acceptance',now(),'acceptance')");
        command.Parameters.AddWithValue(store); command.Parameters.AddWithValue(restaurant); command.Parameters.AddWithValue(branch); command.Parameters.AddWithValue("acceptance-" + store.ToString("N"));
        await command.ExecuteNonQueryAsync();
    }
    public async Task RevokeReadAsync(string subject)
    {
        // Explicit deny takes precedence over the role grant while membership/session remain valid.
        await using var command = authorization.CreateCommand("UPDATE authorization_user_permission_overrides SET effect='deny' WHERE subject_id=$1 AND permission_code='pos.cash-review.read' AND status='active'");
        command.Parameters.AddWithValue(subject);
        if (await command.ExecuteNonQueryAsync() != 1) throw new InvalidOperationException();
    }
}
