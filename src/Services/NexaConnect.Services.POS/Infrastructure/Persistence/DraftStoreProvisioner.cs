using Npgsql;
namespace NexaConnect.Services.POS.Infrastructure.Persistence;

// Reference-only tooling; never registered in the HTTP host.
public sealed class DraftStoreProvisioner(NpgsqlDataSource source)
{
    public async Task EnsureAsync(Guid restaurant, Guid branch, Guid store, Guid terminal, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        if (!System.Text.RegularExpressions.Regex.IsMatch(connection.Database, "^nexa_review_it_[a-f0-9]{32}_pos$"))
            throw new InvalidOperationException("Draft database required.");
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var storeCommand = new NpgsqlCommand("""
            INSERT INTO stores(id,restaurant_id,branch_id,code,name,operational_status,created_at_utc,created_by,updated_at_utc,updated_by)
            VALUES($1,$2,$3,'draft','Draft Windows store','active',now(),'draft-provisioner',now(),'draft-provisioner') ON CONFLICT DO NOTHING;
            """, connection, transaction);
        foreach (var id in new[] { store, restaurant, branch }) storeCommand.Parameters.AddWithValue(id);
        await storeCommand.ExecuteNonQueryAsync(ct);
        await using var terminalCommand = new NpgsqlCommand("""
            INSERT INTO terminals(id,restaurant_id,store_id,code,device_type,registration_status,registered_at_utc,created_at_utc,updated_at_utc)
            SELECT $1,$2,$3,'windows-pc','pos','active',now(),now(),now() FROM stores WHERE id=$3 AND restaurant_id=$2 AND branch_id=$4
            ON CONFLICT DO NOTHING;
            """, connection, transaction);
        foreach (var id in new[] { terminal, restaurant, store, branch }) terminalCommand.Parameters.AddWithValue(id);
        await terminalCommand.ExecuteNonQueryAsync(ct);
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS(SELECT 1 FROM terminals t JOIN stores s ON t.store_id=s.id AND t.restaurant_id=s.restaurant_id
              WHERE s.id=$1 AND s.restaurant_id=$2 AND s.branch_id=$3 AND t.id=$4 AND s.operational_status='active' AND t.registration_status='active');
            """, connection, transaction);
        foreach (var id in new[] { store, restaurant, branch, terminal }) command.Parameters.AddWithValue(id);
        if (!(bool)(await command.ExecuteScalarAsync(ct) ?? false)) throw new InvalidOperationException("Draft store identity mismatch.");
        await transaction.CommitAsync(ct);
    }
}
