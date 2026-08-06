using Npgsql;

namespace NexaConnect.Services.POS.Infrastructure.Persistence;

public sealed class PostgresTerminalStore(NpgsqlDataSource dataSource)
{
    public async Task<bool> EnrollAsync(
        Guid organizationId,
        Guid restaurantId,
        Guid branchId,
        Guid storeId,
        Guid terminalId,
        string code,
        string deviceType,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO terminals
                (id, restaurant_id, store_id, code, device_type, registration_status,
                 registered_at_utc, created_at_utc, updated_at_utc)
            SELECT $1, store.restaurant_id, store.id, $5, $6, 'active', now(), now(), now()
            FROM stores store
            WHERE store.id = $2 AND store.restaurant_id = $3 AND store.branch_id = $4
              AND store.operational_status = 'active'
            ON CONFLICT (id) DO UPDATE
            SET code = EXCLUDED.code, device_type = EXCLUDED.device_type,
                registration_status = 'active', revoked_at_utc = NULL,
                registered_at_utc = now(), updated_at_utc = now();
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(terminalId);
        command.Parameters.AddWithValue(storeId);
        command.Parameters.AddWithValue(restaurantId);
        command.Parameters.AddWithValue(branchId);
        command.Parameters.AddWithValue(code);
        command.Parameters.AddWithValue(deviceType);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}
