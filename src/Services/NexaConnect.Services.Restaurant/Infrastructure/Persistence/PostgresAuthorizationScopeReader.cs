using Npgsql;
using NexaConnect.Services.Restaurant.Application.Authorization;

namespace NexaConnect.Services.Restaurant.Infrastructure.Persistence;

public sealed class PostgresAuthorizationScopeReader(NpgsqlDataSource dataSource) : IAuthorizationScopeReader
{
    public async Task<AuthorizationScope?> GetAsync(Guid branchId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT restaurant.organization_id, restaurant.id, branch.id
            FROM branches branch
            JOIN restaurants restaurant ON restaurant.id = branch.restaurant_id
            WHERE branch.id = $1 AND branch.status = 'active' AND restaurant.status = 'active';
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(branchId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new AuthorizationScope(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2))
            : null;
    }
}
