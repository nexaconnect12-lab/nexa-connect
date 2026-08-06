using Npgsql;

namespace NexaConnect.Services.PlatformDirectory;

public sealed class OrganizationAccessStore(NpgsqlDataSource dataSource)
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
}
