using Npgsql;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.PlatformDirectory.Application.ControlPlane;

namespace NexaConnect.Services.PlatformDirectory.Infrastructure.Persistence;

public sealed class PostgresPlatformDirectoryManagementRepository(NpgsqlDataSource dataSource)
    : IPlatformDirectoryManagementRepository
{
    public async Task<OrganizationSummary> CreateOrganizationAsync(CreateOrganizationRequest request, string actorSubjectId, CancellationToken cancellationToken)
    {
        Guid organizationId = Guid.NewGuid();
        const string sql = """
            INSERT INTO organizations (id, code, name, status, default_time_zone, created_at_utc, created_by, updated_at_utc, updated_by)
            VALUES ($1, $2, $3, 'active', $4, now(), $5, now(), $5)
            RETURNING id, code, name, status, default_time_zone;
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(organizationId);
        command.Parameters.AddWithValue(request.Code);
        command.Parameters.AddWithValue(request.Name);
        command.Parameters.AddWithValue(request.DefaultTimeZone);
        command.Parameters.AddWithValue(actorSubjectId);
        try
        {
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Organization was not created.");
            return ReadOrganization(reader);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new PlatformDirectoryConflictException("Organization code is already registered.");
        }
    }

    public async Task<bool> UpdateOrganizationAsync(Guid organizationId, UpdateOrganizationRequest request, string actorSubjectId, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE organizations
            SET name = $2, status = $3, default_time_zone = $4, updated_at_utc = now(), updated_by = $5, concurrency_version = concurrency_version + 1
            WHERE id = $1;
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(organizationId);
        command.Parameters.AddWithValue(request.Name);
        command.Parameters.AddWithValue(request.Status);
        command.Parameters.AddWithValue(request.DefaultTimeZone);
        command.Parameters.AddWithValue(actorSubjectId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> ChangeMembershipAsync(Guid organizationId, string subjectId, ChangeOrganizationMembershipRequest request, string actorSubjectId, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO organization_memberships (id, organization_id, identity_subject_id, status, invited_at_utc, joined_at_utc, created_at_utc, created_by, updated_at_utc, updated_by)
            VALUES ($1, $2, $3, $4, CASE WHEN $4 = 'invited' THEN now() ELSE NULL END, CASE WHEN $4 = 'active' THEN now() ELSE NULL END, now(), $5, now(), $5)
            ON CONFLICT (organization_id, identity_subject_id) DO UPDATE
            SET status = EXCLUDED.status,
                joined_at_utc = CASE WHEN EXCLUDED.status = 'active' THEN COALESCE(organization_memberships.joined_at_utc, now()) ELSE organization_memberships.joined_at_utc END,
                suspended_at_utc = CASE WHEN EXCLUDED.status = 'suspended' THEN now() ELSE organization_memberships.suspended_at_utc END,
                removed_at_utc = CASE WHEN EXCLUDED.status = 'removed' THEN now() ELSE NULL END,
                updated_at_utc = now(), updated_by = EXCLUDED.updated_by,
                concurrency_version = organization_memberships.concurrency_version + 1;
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(organizationId);
        command.Parameters.AddWithValue(subjectId);
        command.Parameters.AddWithValue(request.Status);
        command.Parameters.AddWithValue(actorSubjectId);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<ProductRegistration> RegisterProductAsync(RegisterProductRequest request, string actorSubjectId, CancellationToken cancellationToken)
    {
        Guid applicationId = Guid.NewGuid();
        const string sql = """
            INSERT INTO applications (id, code, name, status, created_at_utc, created_by, updated_at_utc, updated_by)
            VALUES ($1, $2, $3, 'active', now(), $4, now(), $4)
            RETURNING code, name, status;
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(applicationId);
        command.Parameters.AddWithValue(request.ApplicationCode);
        command.Parameters.AddWithValue(request.Name);
        command.Parameters.AddWithValue(actorSubjectId);
        try
        {
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Product was not registered.");
            return new ProductRegistration(reader.GetString(0), reader.GetString(1), reader.GetString(2));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new PlatformDirectoryConflictException("Product code is already registered.");
        }
    }

    public async Task<bool> ChangeProductAccessAsync(Guid organizationId, ChangeOrganizationProductAccessRequest request, string actorSubjectId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string applicationSql = "SELECT id FROM applications WHERE code = $1 AND status <> 'retired';";
        await using var applicationCommand = new NpgsqlCommand(applicationSql, connection, transaction);
        applicationCommand.Parameters.AddWithValue(request.ApplicationCode);
        object? applicationId = await applicationCommand.ExecuteScalarAsync(cancellationToken);
        if (applicationId is null) return false;

        const string accessSql = """
            INSERT INTO organization_application_access (organization_id, application_id, status, enabled_at_utc, created_at_utc, created_by, updated_at_utc, updated_by)
            VALUES ($1, $2, $3, now(), now(), $4, now(), $4)
            ON CONFLICT (organization_id, application_id) DO UPDATE
            SET status = EXCLUDED.status, updated_at_utc = now(), updated_by = EXCLUDED.updated_by,
                concurrency_version = organization_application_access.concurrency_version + 1;
            """;
        await using var accessCommand = new NpgsqlCommand(accessSql, connection, transaction);
        accessCommand.Parameters.AddWithValue(organizationId);
        accessCommand.Parameters.AddWithValue((Guid)applicationId);
        accessCommand.Parameters.AddWithValue(request.Status);
        accessCommand.Parameters.AddWithValue(actorSubjectId);
        bool changed = await accessCommand.ExecuteNonQueryAsync(cancellationToken) > 0;
        await transaction.CommitAsync(cancellationToken);
        return changed;
    }

    private static OrganizationSummary ReadOrganization(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4));
}
