using Npgsql;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.PlatformDirectory.Application.Administration;

namespace NexaConnect.Services.PlatformDirectory.Infrastructure.Persistence;

public sealed class PostgresPlatformControlPlaneStore(NpgsqlDataSource dataSource, TimeProvider timeProvider) : IPlatformControlPlaneStore
{
    public async Task RecordAuditAsync(string action, string resourceType, string resourceId, string actorSubjectId, string outcome, CancellationToken cancellationToken)
    {
        const string sql = "INSERT INTO platform_audit_records (id, action, resource_type, resource_id, actor_subject_id, outcome, occurred_at_utc) VALUES ($1,$2,$3,$4,$5,$6,$7);";
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(Guid.NewGuid()); command.Parameters.AddWithValue(action); command.Parameters.AddWithValue(resourceType);
        command.Parameters.AddWithValue(resourceId); command.Parameters.AddWithValue(actorSubjectId); command.Parameters.AddWithValue(outcome);
        command.Parameters.AddWithValue(timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<PlatformAuditRecord>> QueryAuditAsync(PlatformAuditQuery query, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, action, resource_type, resource_id, actor_subject_id, outcome, occurred_at_utc
            FROM platform_audit_records
            WHERE ($1::timestamptz IS NULL OR occurred_at_utc >= $1)
              AND ($2::timestamptz IS NULL OR occurred_at_utc <= $2)
              AND ($3::text IS NULL OR actor_subject_id = $3)
              AND ($4::text IS NULL OR action = $4)
            ORDER BY occurred_at_utc DESC, id DESC LIMIT $5;
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(query.FromUtc is null ? DBNull.Value : query.FromUtc.Value);
        command.Parameters.AddWithValue(query.ToUtc is null ? DBNull.Value : query.ToUtc.Value);
        command.Parameters.AddWithValue(string.IsNullOrWhiteSpace(query.ActorSubjectId) ? DBNull.Value : query.ActorSubjectId.Trim());
        command.Parameters.AddWithValue(string.IsNullOrWhiteSpace(query.Action) ? DBNull.Value : query.Action.Trim());
        command.Parameters.AddWithValue(query.Limit);
        var records = new List<PlatformAuditRecord>();
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) records.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6)));
        return records;
    }

    public async Task<PlatformSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT (SELECT count(*) FROM organizations),
                   (SELECT count(*) FROM organizations WHERE status='active'),
                   (SELECT count(*) FROM organization_memberships WHERE status='active'),
                   (SELECT count(*) FROM applications WHERE status <> 'retired'),
                   (SELECT count(*) FROM organization_application_access WHERE status='enabled'),
                   (SELECT count(*) FROM support_elevations WHERE status='active' AND expires_at_utc > now());
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Platform summary could not be read.");
        return new(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5), timeProvider.GetUtcNow());
    }
}
