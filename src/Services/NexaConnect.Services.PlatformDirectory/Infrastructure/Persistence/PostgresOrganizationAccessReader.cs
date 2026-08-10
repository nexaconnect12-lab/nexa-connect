using Npgsql;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.PlatformDirectory.Application.Access;

namespace NexaConnect.Services.PlatformDirectory.Infrastructure.Persistence;

public sealed class PostgresOrganizationAccessReader(NpgsqlDataSource dataSource) : IOrganizationAccessReader
{
    public async Task<bool> HasNexaConnectAccessAsync(
        Guid organizationId,
        string subjectId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS
            (
                SELECT 1
                FROM organization_memberships membership
                JOIN organizations organization ON organization.id = membership.organization_id
                JOIN organization_application_access access
                    ON access.organization_id = organization.id
                JOIN applications application ON application.id = access.application_id
                WHERE membership.organization_id = $1
                  AND membership.identity_subject_id = $2
                  AND membership.status = 'active'
                  AND organization.status = 'active'
                  AND application.code = 'nexa_connect'
                  AND application.status = 'active'
                  AND access.status = 'enabled'
            );
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(organizationId);
        command.Parameters.AddWithValue(subjectId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<IReadOnlyList<OrganizationApplicationAccess>> GetCurrentAccessAsync(
        string subjectId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(subjectId))
            throw new ArgumentException("Identity subject is required.", nameof(subjectId));

        const string sql = """
            SELECT organization.id, organization.code, organization.name, application.code
            FROM organization_memberships membership
            JOIN organizations organization ON organization.id = membership.organization_id
            JOIN organization_application_access access
                ON access.organization_id = organization.id
            JOIN applications application ON application.id = access.application_id
            WHERE membership.identity_subject_id = $1
              AND membership.status = 'active'
              AND organization.status = 'active'
              AND application.status = 'active'
              AND access.status = 'enabled'
            ORDER BY organization.id, application.code;
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(subjectId.Trim());
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<OrganizationApplicationAccess>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new OrganizationApplicationAccess(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return results;
    }
}
