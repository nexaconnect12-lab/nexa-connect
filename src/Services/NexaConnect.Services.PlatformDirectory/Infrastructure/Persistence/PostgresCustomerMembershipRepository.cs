using NexaConnect.Contracts.Platform;
using NexaConnect.Services.PlatformDirectory.Application.CustomerMemberships;
using Npgsql;

namespace NexaConnect.Services.PlatformDirectory.Infrastructure.Persistence;

public sealed class PostgresCustomerMembershipRepository(NpgsqlDataSource dataSource) : ICustomerMembershipRepository
{
    public async Task<IReadOnlyCollection<CustomerMembershipSummary>> ListAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT organization_id, identity_subject_id, status, invited_at_utc, joined_at_utc, suspended_at_utc, removed_at_utc, concurrency_version FROM organization_memberships WHERE organization_id=$1 AND status <> 'removed' ORDER BY identity_subject_id;";
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection); command.Parameters.AddWithValue(organizationId);
        var results = new List<CustomerMembershipSummary>(); await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) results.Add(Read(reader)); return results;
    }

    public async Task<CustomerMembershipSummary?> ChangeAsync(Guid organizationId, string subjectId, string status, long? expectedVersion, string actorSubjectId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string versionSql = "SELECT concurrency_version FROM organization_memberships WHERE organization_id=$1 AND identity_subject_id=$2 FOR UPDATE;";
        await using var versionCommand = new NpgsqlCommand(versionSql, connection, transaction); versionCommand.Parameters.AddWithValue(organizationId); versionCommand.Parameters.AddWithValue(subjectId);
        object? currentVersion = await versionCommand.ExecuteScalarAsync(cancellationToken);
        if (currentVersion is not null && expectedVersion is null) throw new CustomerMembershipConflictException("Expected version is required when updating an existing membership.");
        if (currentVersion is null && expectedVersion is not null) throw new CustomerMembershipConflictException("The membership does not exist at the expected version.");
        if (currentVersion is not null && (long)currentVersion != expectedVersion) throw new CustomerMembershipConflictException("Membership changed concurrently.");
        const string sql = """
            INSERT INTO organization_memberships (id,organization_id,identity_subject_id,status,invited_at_utc,joined_at_utc,suspended_at_utc,removed_at_utc,created_at_utc,created_by,updated_at_utc,updated_by)
            VALUES ($1,$2,$3,$4,CASE WHEN $4='invited' THEN now() END,CASE WHEN $4='active' THEN now() END,CASE WHEN $4='suspended' THEN now() END,CASE WHEN $4='removed' THEN now() END,now(),$5,now(),$5)
            ON CONFLICT (organization_id,identity_subject_id) DO UPDATE SET status=EXCLUDED.status,
              joined_at_utc=CASE WHEN EXCLUDED.status='active' THEN COALESCE(organization_memberships.joined_at_utc,now()) ELSE organization_memberships.joined_at_utc END,
              suspended_at_utc=CASE WHEN EXCLUDED.status='suspended' THEN now() ELSE organization_memberships.suspended_at_utc END,
              removed_at_utc=CASE WHEN EXCLUDED.status='removed' THEN now() ELSE NULL END,updated_at_utc=now(),updated_by=$5,concurrency_version=organization_memberships.concurrency_version+1
            RETURNING organization_id,identity_subject_id,status,invited_at_utc,joined_at_utc,suspended_at_utc,removed_at_utc,concurrency_version;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(Guid.NewGuid()); command.Parameters.AddWithValue(organizationId); command.Parameters.AddWithValue(subjectId); command.Parameters.AddWithValue(status); command.Parameters.AddWithValue(actorSubjectId);
        CustomerMembershipSummary? result; await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)) result = await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
        if (result is null) { await transaction.RollbackAsync(cancellationToken); throw new CustomerMembershipConflictException("Membership changed concurrently or could not be updated."); }
        const string audit = "INSERT INTO platform_audit_records (id,action,resource_type,resource_id,actor_subject_id,outcome,occurred_at_utc) VALUES ($1,'customer-membership.changed','organization-membership',$2,$3,'succeeded',now());";
        await using var auditCommand = new NpgsqlCommand(audit, connection, transaction); auditCommand.Parameters.AddWithValue(Guid.NewGuid()); auditCommand.Parameters.AddWithValue($"{organizationId:D}:{subjectId}"); auditCommand.Parameters.AddWithValue(actorSubjectId); await auditCommand.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken); return result;
    }

    private static CustomerMembershipSummary Read(NpgsqlDataReader reader) => new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.IsDBNull(3)?null:reader.GetFieldValue<DateTimeOffset>(3),reader.IsDBNull(4)?null:reader.GetFieldValue<DateTimeOffset>(4),reader.IsDBNull(5)?null:reader.GetFieldValue<DateTimeOffset>(5),reader.IsDBNull(6)?null:reader.GetFieldValue<DateTimeOffset>(6),reader.GetInt64(7));
}
