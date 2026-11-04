using NexaConnect.Contracts.POS;
using NexaConnect.Services.POS.Application.CashierPins;
using NexaConnect.Services.POS.Domain.CashierPins;
using Npgsql;

namespace NexaConnect.Services.POS.Infrastructure.Persistence;

public sealed class PostgresCashierPinRepository(NpgsqlDataSource dataSource) : ICashierPinRepository
{
    public async Task<CashierPinStatusV1> ReadAsync(CashierPinKey key, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var credential = await Read(connection, null, key, ct);
        return Status(credential);
    }
    public async Task<CashierPinResultV1> MutateAsync(CashierPinKey key, CashierPinAudit audit, long version,
        Func<CashierPinCredential, bool> transition, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        // Also serialize initial enrollment where no row yet exists. Hash collisions only serialize more work.
        await using (var gate = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended($1, 0))", connection, transaction))
        { gate.Parameters.AddWithValue($"{key.OrganizationId:N}|{key.Issuer}|{key.Subject}"); await gate.ExecuteNonQueryAsync(ct); }
        var credential = await Read(connection, transaction, key, ct);
        await using (var duplicate = new NpgsqlCommand("SELECT 1 FROM cashier_pin_audit WHERE organization_id=$1 AND issuer=$2 AND subject=$3 AND operation_id=$4", connection, transaction))
        {
            Key(duplicate, key); duplicate.Parameters.AddWithValue(audit.Id);
            // Never replay a successful unlock decision, and never consume another attempt for the same ID.
            if (await duplicate.ExecuteScalarAsync(ct) is not null) return new(false, Status(credential));
        }
        bool accepted = transition(credential);
        await using (var save = new NpgsqlCommand("""
            INSERT INTO cashier_pin_credentials(organization_id,issuer,subject,state,version,protected_verifier,failed_attempts,locked_until_utc)
            VALUES($1,$2,$3,$4,$5,$6,$7,$8)
            ON CONFLICT(organization_id,issuer,subject) DO UPDATE SET state=EXCLUDED.state,version=EXCLUDED.version,
                protected_verifier=EXCLUDED.protected_verifier,failed_attempts=EXCLUDED.failed_attempts,locked_until_utc=EXCLUDED.locked_until_utc
            """, connection, transaction))
        {
            Key(save, key); save.Parameters.AddWithValue(credential.State); save.Parameters.AddWithValue(credential.Version);
            save.Parameters.Add(new() { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = (object?)credential.ProtectedVerifier ?? DBNull.Value });
            save.Parameters.AddWithValue(credential.FailedAttempts);
            save.Parameters.Add(new() { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.TimestampTz, Value = (object?)credential.LockedUntilUtc ?? DBNull.Value });
            await save.ExecuteNonQueryAsync(ct);
        }
        await using (var log = new NpgsqlCommand("""
            INSERT INTO cashier_pin_audit(organization_id,issuer,subject,operation_id,actor,action,decision_id,branch_id,store_id,terminal_id,expected_version,result_version,accepted)
            VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13)
            """, connection, transaction))
        {
            Key(log, key); log.Parameters.AddWithValue(audit.Id); log.Parameters.AddWithValue(audit.Actor);
            log.Parameters.AddWithValue(audit.Action); log.Parameters.AddWithValue(audit.DecisionId);
            log.Parameters.AddWithValue(audit.Scope.BranchId); log.Parameters.AddWithValue(audit.Scope.StoreId); log.Parameters.AddWithValue(audit.Scope.TerminalId);
            log.Parameters.AddWithValue(version); log.Parameters.AddWithValue(credential.Version); log.Parameters.AddWithValue(accepted);
            await log.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return new(accepted, Status(credential));
    }
    private static async Task<CashierPinCredential> Read(NpgsqlConnection connection, NpgsqlTransaction? transaction, CashierPinKey key, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT state,version,protected_verifier,failed_attempts,locked_until_utc FROM cashier_pin_credentials WHERE organization_id=$1 AND issuer=$2 AND subject=$3", connection, transaction);
        Key(command, key);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? CashierPinCredential.Restore(reader.GetString(0), reader.GetInt64(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetInt32(3), reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4))
            : new();
    }
    private static CashierPinStatusV1 Status(CashierPinCredential value) => new(value.State, value.Version, value.LockedUntilUtc);
    private static void Key(NpgsqlCommand command, CashierPinKey key)
    { command.Parameters.AddWithValue(key.OrganizationId); command.Parameters.AddWithValue(key.Issuer); command.Parameters.AddWithValue(key.Subject); }
}
