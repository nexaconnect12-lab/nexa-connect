using NexaConnect.Services.Restaurant.Application.Provisioning;
using Npgsql;

namespace NexaConnect.Services.Restaurant.Infrastructure.Persistence;

public sealed class PostgresRestaurantProvisioningRepository(NpgsqlDataSource dataSource) : IRestaurantProvisioningRepository
{
    public async Task<IReadOnlyCollection<PlatformRestaurantSummary>> ListRestaurantsAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT id, organization_id, code, name, default_currency, default_time_zone, status FROM restaurants WHERE organization_id=$1 ORDER BY name, id;";
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var db = new NpgsqlCommand(sql, connection);
        db.Parameters.AddWithValue(organizationId);
        var rows = new List<PlatformRestaurantSummary>();
        await using NpgsqlDataReader reader = await db.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6)));
        return rows;
    }

    public async Task<IReadOnlyCollection<PlatformBranchSummary>> ListBranchesAsync(Guid restaurantId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT b.id, b.restaurant_id, r.organization_id, b.code, b.name, b.currency, b.time_zone, b.status FROM branches b JOIN restaurants r ON r.id=b.restaurant_id WHERE b.restaurant_id=$1 ORDER BY b.name, b.id;";
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var db = new NpgsqlCommand(sql, connection);
        db.Parameters.AddWithValue(restaurantId);
        var rows = new List<PlatformBranchSummary>();
        await using NpgsqlDataReader reader = await db.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7)));
        return rows;
    }

    public async Task<RestaurantProvisioningResult> CreateRestaurantAsync(CreateRestaurantCommand command, string actor, CancellationToken cancellationToken)
    {
        Guid id = Guid.NewGuid();
        const string sql = """
            INSERT INTO restaurants (id, organization_id, code, name, default_currency, default_time_zone, status, created_at_utc, created_by, updated_at_utc, updated_by)
            VALUES ($1, $2, $3, $4, $5, $6, 'active', now(), $7, now(), $7)
            ON CONFLICT (organization_id, code) DO UPDATE SET name=EXCLUDED.name, default_currency=EXCLUDED.default_currency, default_time_zone=EXCLUDED.default_time_zone, status='active', updated_at_utc=now(), updated_by=EXCLUDED.updated_by, concurrency_version=restaurants.concurrency_version+1
            RETURNING id;
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var db = new NpgsqlCommand(sql, connection);
        db.Parameters.AddWithValue(id); db.Parameters.AddWithValue(command.OrganizationId); db.Parameters.AddWithValue(command.Code); db.Parameters.AddWithValue(command.Name); db.Parameters.AddWithValue(command.Currency); db.Parameters.AddWithValue(command.TimeZone); db.Parameters.AddWithValue(actor);
        Guid result = (Guid)(await db.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException("Restaurant was not persisted."));
        return new(result, command.OrganizationId, command.Code, command.Name);
    }

    public async Task<BranchProvisioningResult?> CreateBranchAsync(Guid restaurantId, CreateBranchCommand command, string actor, CancellationToken cancellationToken)
    {
        Guid id = Guid.NewGuid();
        const string sql = """
            INSERT INTO branches (id, restaurant_id, code, name, time_zone, currency, status, opened_at_utc, created_at_utc, created_by, updated_at_utc, updated_by)
            SELECT $1, r.id, $2, $3, $4, $5, 'active', now(), now(), $6, now(), $6 FROM restaurants r WHERE r.id=$7 AND r.status='active'
            ON CONFLICT (restaurant_id, code) DO UPDATE SET name=EXCLUDED.name, time_zone=EXCLUDED.time_zone, currency=EXCLUDED.currency, status='active', closed_at_utc=NULL, updated_at_utc=now(), updated_by=EXCLUDED.updated_by, concurrency_version=branches.concurrency_version+1
            RETURNING id, (SELECT organization_id FROM restaurants WHERE id=$7);
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var db = new NpgsqlCommand(sql, connection);
        db.Parameters.AddWithValue(id); db.Parameters.AddWithValue(command.Code); db.Parameters.AddWithValue(command.Name); db.Parameters.AddWithValue(command.TimeZone); db.Parameters.AddWithValue(command.Currency); db.Parameters.AddWithValue(actor); db.Parameters.AddWithValue(restaurantId);
        await using NpgsqlDataReader reader = await db.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new(reader.GetGuid(0), restaurantId, reader.GetGuid(1), command.Code, command.Name);
    }
}
