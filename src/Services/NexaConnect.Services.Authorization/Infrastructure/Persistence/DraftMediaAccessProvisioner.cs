using Npgsql;

namespace NexaConnect.Services.Authorization.Infrastructure.Persistence;

// Local reference tooling only. Two explicit Media permissions; no role/financial grants.
public sealed class DraftMediaAccessProvisioner(NpgsqlDataSource source)
{
    public async Task EnsureAsync(Guid organization, string subject, CancellationToken ct)
    {
        if (organization == Guid.Empty || !Guid.TryParse(subject, out _)) throw new ArgumentException("Draft identity required.");
        await using var connection = await source.OpenConnectionAsync(ct);
        if (!System.Text.RegularExpressions.Regex.IsMatch(connection.Database, "^nexa_review_it_[a-f0-9]{32}_authorization$"))
            throw new InvalidOperationException("Draft database required.");
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var manager = new NpgsqlCommand("""
            SELECT EXISTS(SELECT 1 FROM authorization_role_assignments a
            JOIN authorization_roles r ON r.id=a.role_id JOIN authorization_resource_scopes s ON s.id=a.scope_id
            WHERE a.subject_id=$1 AND r.organization_id=$2 AND r.code='store-manager'
              AND a.status='active' AND r.status='active' AND s.status='active' AND s.organization_id=$2);
            """, connection, transaction);
        manager.Parameters.AddWithValue(subject); manager.Parameters.AddWithValue(organization);
        if (await manager.ExecuteScalarAsync(ct) is not true) throw new InvalidOperationException("Active local manager required.");
        // Never reactivate a revoked scope or overwrite an explicit deny/revocation.
        await using var scope = new NpgsqlCommand("""
            INSERT INTO authorization_resource_scopes(id,organization_id,restaurant_id,branch_id,status,updated_at_utc)
            VALUES($1,$2,NULL,NULL,'active',now()) ON CONFLICT DO NOTHING;
            """, connection, transaction);
        scope.Parameters.AddWithValue(Guid.NewGuid()); scope.Parameters.AddWithValue(organization);
        await scope.ExecuteNonQueryAsync(ct);
        foreach (string permission in new[] { "media.asset.read", "media.asset.manage" })
        {
            await using var grant = new NpgsqlCommand("""
                INSERT INTO authorization_user_permission_overrides(id,subject_id,scope_id,permission_code,effect,status)
                SELECT $1,$2,id,$3,'allow','active' FROM authorization_resource_scopes
                WHERE organization_id=$4 AND restaurant_id IS NULL AND branch_id IS NULL AND status='active'
                ON CONFLICT DO NOTHING;
                """, connection, transaction);
            grant.Parameters.AddWithValue(Guid.NewGuid()); grant.Parameters.AddWithValue(subject);
            grant.Parameters.AddWithValue(permission); grant.Parameters.AddWithValue(organization);
            await grant.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }
}
