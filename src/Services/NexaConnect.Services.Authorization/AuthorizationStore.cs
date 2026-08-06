using Npgsql;

public sealed class AuthorizationStore(NpgsqlDataSource dataSource)
{
    public async Task<AuthorizationDecision> DecideAsync(
        string subjectId, Guid organizationId, Guid? restaurantId, Guid? branchId,
        string permission, decimal? amount, string? currency, CancellationToken cancellationToken)
    {
        const string sql = """
            WITH scopes AS
            (
                SELECT id, CASE WHEN branch_id IS NOT NULL THEN 3 WHEN restaurant_id IS NOT NULL THEN 2 ELSE 1 END AS specificity
                FROM authorization_resource_scopes
                WHERE organization_id = $2 AND status = 'active'
                  AND (restaurant_id IS NULL OR restaurant_id = $3)
                  AND (branch_id IS NULL OR branch_id = $4)
            ), override_decision AS
            (
                SELECT effect FROM authorization_user_permission_overrides o JOIN scopes s ON s.id = o.scope_id
                WHERE o.subject_id = $1 AND o.permission_code = $5 AND o.status = 'active'
                ORDER BY s.specificity DESC LIMIT 1
            )
            SELECT COALESCE((SELECT effect = 'allow' FROM override_decision), EXISTS
            (
                SELECT 1 FROM authorization_role_assignments a
                JOIN authorization_role_permissions p ON p.role_id = a.role_id
                JOIN authorization_roles r ON r.id = a.role_id
                JOIN scopes s ON s.id = a.scope_id
                WHERE a.subject_id = $1 AND a.status = 'active' AND r.status = 'active' AND p.permission_code = $5
            ), false);
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(subjectId);
        command.Parameters.AddWithValue(organizationId);
        command.Parameters.AddWithValue((object?)restaurantId ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)branchId ?? DBNull.Value);
        command.Parameters.AddWithValue(permission);
        bool permissionGranted = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
        decimal? limit = amount is null ? null : await ReadLimitAsync(
            connection, subjectId, restaurantId, permission, currency, cancellationToken);
        bool granted = permissionGranted && (amount is null || (limit is not null && amount <= limit));
        Guid decisionId = Guid.NewGuid();
        const string recordSql = """
            INSERT INTO authorization_decisions
                (id, subject_id, organization_id, restaurant_id, branch_id, action_code, granted,
                 evaluated_limit, currency, decided_at_utc, policy_version)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, now(), 1);
            """;
        await using var record = new NpgsqlCommand(recordSql, connection);
        record.Parameters.AddWithValue(decisionId);
        record.Parameters.AddWithValue(subjectId);
        record.Parameters.AddWithValue(organizationId);
        record.Parameters.AddWithValue((object?)restaurantId ?? DBNull.Value);
        record.Parameters.AddWithValue((object?)branchId ?? DBNull.Value);
        record.Parameters.AddWithValue(permission);
        record.Parameters.AddWithValue(granted);
        record.Parameters.AddWithValue((object?)limit ?? DBNull.Value);
        record.Parameters.AddWithValue((object?)currency ?? DBNull.Value);
        await record.ExecuteNonQueryAsync(cancellationToken);
        return new AuthorizationDecision(decisionId, granted, limit);
    }

    private static async Task<decimal?> ReadLimitAsync(
        NpgsqlConnection connection, string subjectId, Guid? restaurantId, string actionCode,
        string? currency, CancellationToken cancellationToken)
    {
        if (restaurantId is null || string.IsNullOrWhiteSpace(currency)) return null;
        const string sql = """
            SELECT max(limit.maximum_amount)
            FROM financial_approval_limits limit
            WHERE limit.restaurant_id = $1 AND limit.action_code = $2 AND limit.currency = $3
              AND limit.status = 'active'
              AND ((limit.principal_type = 'subject' AND limit.principal_id = $4)
                OR (limit.principal_type = 'role' AND limit.principal_id IN
                    (SELECT role_id::text FROM authorization_role_assignments
                     WHERE subject_id = $4 AND status = 'active')));
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(restaurantId.Value);
        command.Parameters.AddWithValue(actionCode);
        command.Parameters.AddWithValue(currency);
        command.Parameters.AddWithValue(subjectId);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is DBNull or null ? null : (decimal)result;
    }
}

public sealed record AuthorizationDecision(Guid Id, bool Granted, decimal? EvaluatedLimit);
