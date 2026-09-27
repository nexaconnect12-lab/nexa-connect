using Npgsql;
using NexaConnect.Services.Authorization.Application.Decisions;
using NexaConnect.Services.Authorization.Domain;

namespace NexaConnect.Services.Authorization.Infrastructure.Persistence;

public sealed class PostgresAuthorizationDecisionStore(NpgsqlDataSource dataSource) : IAuthorizationDecisionStore
{
    public async Task<AuthorizationEvidence> ReadAsync(AuthorizationQuery query, CancellationToken cancellationToken)
    {
        // One MVCC statement snapshot for overrides, matching active roles and financial limits.
        const string sql = """
            WITH scopes AS
            (
                SELECT id, CASE WHEN branch_id IS NOT NULL THEN 3 WHEN restaurant_id IS NOT NULL THEN 2 ELSE 1 END AS specificity
                FROM authorization_resource_scopes
                WHERE organization_id = $2 AND status = 'active'
                  AND (restaurant_id IS NULL OR restaurant_id = $3)
                  AND (branch_id IS NULL OR branch_id = $4)
            ), matching_roles AS
            (
                SELECT a.role_id FROM authorization_role_assignments a
                JOIN authorization_roles r ON r.id = a.role_id AND r.organization_id = $2
                JOIN scopes s ON s.id = a.scope_id
                WHERE a.subject_id = $1 AND a.status = 'active' AND r.status = 'active'
            )
            SELECT
                (SELECT o.effect FROM authorization_user_permission_overrides o JOIN scopes s ON s.id = o.scope_id
                 WHERE o.subject_id = $1 AND o.permission_code = $5 AND o.status = 'active'
                 ORDER BY s.specificity DESC, (o.effect = 'deny') DESC, o.id LIMIT 1),
                EXISTS(SELECT 1 FROM matching_roles r JOIN authorization_role_permissions p ON p.role_id = r.role_id
                       WHERE p.permission_code = $5),
                (SELECT max(l.maximum_amount) FROM financial_approval_limits l
                 WHERE $7::boolean AND l.restaurant_id = $3 AND l.action_code = $5 AND l.currency = $6
                   AND l.status = 'active'
                   AND ((l.principal_type = 'subject' AND l.principal_id = $1)
                     OR (l.principal_type = 'role' AND l.principal_id IN (SELECT role_id::text FROM matching_roles))));
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(query.SubjectId);
        command.Parameters.AddWithValue(query.OrganizationId);
        command.Parameters.AddWithValue((object?)query.RestaurantId ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)query.BranchId ?? DBNull.Value);
        command.Parameters.AddWithValue(query.Permission);
        command.Parameters.AddWithValue((object?)query.Currency ?? DBNull.Value);
        command.Parameters.AddWithValue(query.Amount is not null);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new(reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetBoolean(1), reader.IsDBNull(2) ? null : reader.GetDecimal(2));
    }

    public async Task RecordAsync(AuthorizationQuery query, AuthorizationDecision decision, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO authorization_decisions
                (id, subject_id, organization_id, restaurant_id, branch_id, action_code, granted,
                 evaluated_limit, currency, decided_at_utc, policy_version)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, now(), 2);
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(decision.Id);
        command.Parameters.AddWithValue(query.SubjectId);
        command.Parameters.AddWithValue(query.OrganizationId);
        command.Parameters.AddWithValue((object?)query.RestaurantId ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)query.BranchId ?? DBNull.Value);
        command.Parameters.AddWithValue(query.Permission);
        command.Parameters.AddWithValue(decision.Granted);
        command.Parameters.AddWithValue((object?)decision.EvaluatedLimit ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)query.Currency ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
