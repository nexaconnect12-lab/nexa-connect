using Npgsql;
using NexaConnect.Services.Authorization.Application.Decisions;
using ApplicationDecision = NexaConnect.Services.Authorization.Application.Decisions.AuthorizationDecision;

namespace NexaConnect.Services.Authorization.Infrastructure.Persistence;

public sealed class PostgresAuthorizationDecisionService(NpgsqlDataSource dataSource, ILogger<PostgresAuthorizationDecisionService> logger) : IAuthorizationDecisionService
{
    public async Task<ApplicationDecision> DecideAsync(
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
            SELECT (EXISTS (SELECT 1 FROM override_decision WHERE effect = 'allow') OR EXISTS
            (
                SELECT 1 FROM authorization_role_assignments a
                JOIN authorization_role_permissions p ON p.role_id = a.role_id
                JOIN authorization_roles r ON r.id = a.role_id
                JOIN scopes s ON s.id = a.scope_id
                WHERE a.subject_id = $1 AND a.status = 'active' AND r.status = 'active' AND p.permission_code = $5
            ));
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        const string explicitSql = """
            SELECT EXISTS (
                SELECT 1 FROM authorization_user_permission_overrides o
                JOIN authorization_resource_scopes s ON s.id = o.scope_id
                WHERE o.subject_id = $1 AND o.permission_code = $2 AND o.effect = 'allow' AND o.status = 'active'
                  AND s.organization_id = $3 AND s.restaurant_id = $4 AND s.branch_id = $5 AND s.status = 'active');
            """;
        await using var explicitCommand = new NpgsqlCommand(explicitSql, connection);
        explicitCommand.Parameters.AddWithValue(subjectId);
        explicitCommand.Parameters.AddWithValue(permission);
        explicitCommand.Parameters.AddWithValue(organizationId);
        explicitCommand.Parameters.AddWithValue((object?)restaurantId ?? DBNull.Value);
        explicitCommand.Parameters.AddWithValue((object?)branchId ?? DBNull.Value);
        bool explicitGrant = (bool)(await explicitCommand.ExecuteScalarAsync(cancellationToken) ?? false);
        const string diagnosticSql = "SELECT COUNT(*) FROM authorization_user_permission_overrides o JOIN authorization_resource_scopes s ON s.id = o.scope_id WHERE o.subject_id = $1 AND o.permission_code = $2 AND o.effect = 'allow' AND o.status = 'active' AND s.organization_id = $3 AND s.restaurant_id = $4 AND s.branch_id = $5 AND s.status = 'active';";
        await using var diagnosticCommand = new NpgsqlCommand(diagnosticSql, connection);
        diagnosticCommand.Parameters.AddWithValue(subjectId);
        diagnosticCommand.Parameters.AddWithValue(permission);
        diagnosticCommand.Parameters.AddWithValue(organizationId);
        diagnosticCommand.Parameters.AddWithValue((object?)restaurantId ?? DBNull.Value);
        diagnosticCommand.Parameters.AddWithValue((object?)branchId ?? DBNull.Value);
        long matchingRows = (long)(await diagnosticCommand.ExecuteScalarAsync(cancellationToken) ?? 0L);
        logger.LogInformation("Authorization evaluation host={Host}:{Port}, user={User}, database={Database}, subject={Subject}, organization={Organization}, restaurant={Restaurant}, branch={Branch}, permission={Permission}, explicitGrant={ExplicitGrant}",
            connection.Host, connection.Port, connection.UserName, connection.Database, subjectId, organizationId, restaurantId, branchId, permission, explicitGrant);
        logger.LogInformation("Authorization override diagnostic matchingRows={MatchingRows}", matchingRows);
        bool permissionGranted = explicitGrant;
        if (!permissionGranted)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue(subjectId);
            command.Parameters.AddWithValue(organizationId);
            command.Parameters.AddWithValue((object?)restaurantId ?? DBNull.Value);
            command.Parameters.AddWithValue((object?)branchId ?? DBNull.Value);
            command.Parameters.AddWithValue(permission);
            permissionGranted = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
        }
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
        return new ApplicationDecision(decisionId, granted, limit);
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
