using Npgsql;
using NexaConnect.Services.PlatformDirectory.Application.Support;
using NexaConnect.Services.PlatformDirectory.Domain.Support;

namespace NexaConnect.Services.PlatformDirectory.Infrastructure.Persistence;

public sealed class PostgresSupportElevationRepository(NpgsqlDataSource dataSource) : ISupportElevationRepository
{
    public async Task CreateAsync(SupportElevation elevation, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string insert = """
            INSERT INTO support_elevations
                (id, organization_id, application_code, support_subject_id, reason, duration_minutes,
                 status, requested_at_utc, created_at_utc, updated_at_utc)
            SELECT $1, organization.id, application.code, $4, $5, $6, 'pending', $7, $7, $7
            FROM organizations organization
            JOIN applications application ON application.code = $3 AND application.status = 'active'
            JOIN organization_application_access access
              ON access.organization_id = organization.id AND access.application_id = application.id
             AND access.status = 'enabled'
            WHERE organization.id = $2 AND organization.status = 'active';
            """;
        await using var command = new NpgsqlCommand(insert, connection, transaction);
        command.Parameters.AddWithValue(elevation.Id);
        command.Parameters.AddWithValue(elevation.OrganizationId);
        command.Parameters.AddWithValue(elevation.ApplicationCode);
        command.Parameters.AddWithValue(elevation.SupportSubjectId);
        command.Parameters.AddWithValue(elevation.Reason);
        command.Parameters.AddWithValue(elevation.DurationMinutes);
        command.Parameters.AddWithValue(elevation.RequestedAtUtc);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new ArgumentException("The active organization or enabled product access was not found.");
        await InsertAuditAsync(connection, transaction, elevation.Id, "requested", elevation.SupportSubjectId, elevation.RequestedAtUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<SupportElevation?> FindAsync(Guid elevationId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, organization_id, application_code, support_subject_id, reason, duration_minutes,
                   status, requested_at_utc, approved_at_utc, expires_at_utc, revoked_at_utc,
                   approved_by_subject_id, revoked_by_subject_id
            FROM support_elevations WHERE id = $1;
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(elevationId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return Read(reader);
    }

    public async Task<SupportElevation?> FindEffectiveAsync(
        Guid organizationId,
        string applicationCode,
        string supportSubjectId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, organization_id, application_code, support_subject_id, reason, duration_minutes,
                   status, requested_at_utc, approved_at_utc, expires_at_utc, revoked_at_utc,
                   approved_by_subject_id, revoked_by_subject_id
            FROM support_elevations
            WHERE organization_id = $1 AND application_code = $2 AND support_subject_id = $3
              AND status = 'active' AND expires_at_utc > $4 AND revoked_at_utc IS NULL
            ORDER BY expires_at_utc DESC LIMIT 1;
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(organizationId);
        command.Parameters.AddWithValue(applicationCode);
        command.Parameters.AddWithValue(supportSubjectId);
        command.Parameters.AddWithValue(now);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    private static SupportElevation Read(NpgsqlDataReader reader) => SupportElevation.Rehydrate(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
            reader.GetInt32(5), Enum.Parse<SupportElevationStatus>(reader.GetString(6), true), reader.GetFieldValue<DateTimeOffset>(7),
            reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
            reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
            reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
            reader.IsDBNull(11) ? null : reader.GetString(11), reader.IsDBNull(12) ? null : reader.GetString(12));

    public Task<bool> TryApproveAsync(SupportElevation elevation, CancellationToken cancellationToken) =>
        ChangeStateAsync(elevation, "pending", "approved", cancellationToken);

    public Task<bool> TryRevokeAsync(SupportElevation elevation, CancellationToken cancellationToken) =>
        ChangeStateAsync(elevation, null, "revoked", cancellationToken);

    private async Task<bool> ChangeStateAsync(
        SupportElevation elevation,
        string? requiredStatus,
        string auditAction,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        string sql = requiredStatus is null
            ? """
              UPDATE support_elevations
              SET status = 'revoked', revoked_at_utc = $2, revoked_by_subject_id = $3, updated_at_utc = $2
              WHERE id = $1 AND status IN ('pending', 'active');
              """
            : """
              UPDATE support_elevations
              SET status = 'active', approved_at_utc = $2, expires_at_utc = $3,
                  approved_by_subject_id = $4, updated_at_utc = $2
              WHERE id = $1 AND status = 'pending';
              """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(elevation.Id);
        if (requiredStatus is null)
        {
            command.Parameters.AddWithValue(elevation.RevokedAtUtc!.Value);
            command.Parameters.AddWithValue(elevation.RevokedBySubjectId!);
        }
        else
        {
            command.Parameters.AddWithValue(elevation.ApprovedAtUtc!.Value);
            command.Parameters.AddWithValue(elevation.ExpiresAtUtc!.Value);
            command.Parameters.AddWithValue(elevation.ApprovedBySubjectId!);
        }
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) return false;
        string actor = requiredStatus is null ? elevation.RevokedBySubjectId! : elevation.ApprovedBySubjectId!;
        DateTimeOffset occurred = requiredStatus is null ? elevation.RevokedAtUtc!.Value : elevation.ApprovedAtUtc!.Value;
        await InsertAuditAsync(connection, transaction, elevation.Id, auditAction, actor, occurred, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static async Task InsertAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid elevationId,
        string action,
        string actorSubjectId,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO support_elevation_audit
                (id, support_elevation_id, action, actor_subject_id, occurred_at_utc)
            VALUES ($1, $2, $3, $4, $5);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(elevationId);
        command.Parameters.AddWithValue(action);
        command.Parameters.AddWithValue(actorSubjectId);
        command.Parameters.AddWithValue(occurredAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
