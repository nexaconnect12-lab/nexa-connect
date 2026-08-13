using Npgsql;
using NexaConnect.Services.Authorization.Application.Assignments;

namespace NexaConnect.Services.Authorization.Infrastructure.Persistence;

public sealed class PostgresAuthorizationAssignmentRepository(NpgsqlDataSource dataSource) : IAuthorizationAssignmentRepository
{
    public async Task<RoleAssignmentResult> AssignAsync(AssignRoleCommand command, string assignedBy, CancellationToken cancellationToken)
    {
        const string sql = """
            WITH scope AS (
                INSERT INTO authorization_resource_scopes (id, organization_id, restaurant_id, branch_id, status, updated_at_utc)
                VALUES ($8, $1, $2, $3, 'active', now())
                ON CONFLICT (organization_id, restaurant_id, branch_id) DO UPDATE SET status = 'active', updated_at_utc = now()
                RETURNING id
            ), role AS (
                INSERT INTO authorization_roles (id, organization_id, code, name, status)
                VALUES ($9, $1, $4, $4, 'active')
                ON CONFLICT (organization_id, code) DO UPDATE SET status = 'active'
                RETURNING id
            )
            INSERT INTO authorization_role_assignments
                (id, role_id, subject_id, scope_id, status, assigned_at_utc, assigned_by_subject_id)
            SELECT $5, role.id, $6, scope.id, 'active', now(), $7 FROM role CROSS JOIN scope
            ON CONFLICT (role_id, subject_id, scope_id) DO UPDATE
                SET status = 'active', assigned_at_utc = now(), assigned_by_subject_id = EXCLUDED.assigned_by_subject_id
            RETURNING id;
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var db = new NpgsqlCommand(sql, connection);
        db.Parameters.AddWithValue(command.OrganizationId);
        db.Parameters.AddWithValue(command.RestaurantId);
        db.Parameters.AddWithValue(command.BranchId);
        db.Parameters.AddWithValue(command.RoleCode);
        Guid assignmentId = Guid.NewGuid();
        db.Parameters.AddWithValue(assignmentId);
        db.Parameters.AddWithValue(command.SubjectId);
        db.Parameters.AddWithValue(assignedBy);
        db.Parameters.AddWithValue(Guid.NewGuid());
        db.Parameters.AddWithValue(Guid.NewGuid());
        object? result = await db.ExecuteScalarAsync(cancellationToken);
        if (result is null) throw new InvalidOperationException("No active role, permission, or branch scope matched the assignment.");
        Guid roleId;
        const string roleSql = "SELECT role_id FROM authorization_role_assignments WHERE id = $1 AND subject_id = $2 AND status = 'active';";
        await using (var roleCommand = new NpgsqlCommand(roleSql, connection))
        {
            roleCommand.Parameters.AddWithValue((Guid)result);
            roleCommand.Parameters.AddWithValue(command.SubjectId);
            roleId = (Guid)(await roleCommand.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("The role assignment was not persisted with an active role."));
        }
        string[] permissions = PermissionsFor(command.RoleCode);
        foreach (string permission in permissions)
        {
            await using var permissionCommand = new NpgsqlCommand(
                "INSERT INTO authorization_role_permissions (role_id, permission_code) VALUES ($1, $2) ON CONFLICT DO NOTHING;",
                connection);
            permissionCommand.Parameters.AddWithValue(roleId);
            permissionCommand.Parameters.AddWithValue(permission);
            await permissionCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        const string scopeSql = "SELECT scope_id FROM authorization_role_assignments WHERE id = $1 AND subject_id = $2 AND status = 'active';";
        await using var scopeCommand = new NpgsqlCommand(scopeSql, connection);
        scopeCommand.Parameters.AddWithValue((Guid)result);
        scopeCommand.Parameters.AddWithValue(command.SubjectId);
        object? scopeResult = await scopeCommand.ExecuteScalarAsync(cancellationToken);
        if (scopeResult is null) throw new InvalidOperationException("The role assignment was not persisted with an active scope.");
        const string overrideSql = """
            INSERT INTO authorization_user_permission_overrides
                (id, subject_id, scope_id, permission_code, effect, status)
            VALUES ($1, $2, $3, $4, 'allow', 'active')
            ON CONFLICT (subject_id, scope_id, permission_code) DO UPDATE SET effect = 'allow', status = 'active';
            """;
        foreach (string permission in permissions)
        {
            await using var overrideCommand = new NpgsqlCommand(overrideSql, connection);
            overrideCommand.Parameters.AddWithValue(Guid.NewGuid());
            overrideCommand.Parameters.AddWithValue(command.SubjectId);
            overrideCommand.Parameters.AddWithValue((Guid)scopeResult);
            overrideCommand.Parameters.AddWithValue(permission);
            await overrideCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        return new RoleAssignmentResult((Guid)result);
    }

    private static string[] PermissionsFor(string roleCode) => roleCode switch
    {
        "tenant-admin" or "store-manager" =>
        [
            "catalog.menu.read", "catalog.menu.write", "inventory.stock.read", "inventory.stock.write",
            "inventory.reservation.create", "inventory.reservation.release", "order.create", "order.read",
            "order.place", "payment.intent.create", "payment.intent.read", "customer.profile.create",
            "customer.profile.read", "restaurant.branch.read", "restaurant.branch.manage",
            "restaurant.configuration.read", "restaurant.configuration.manage", "reporting.dashboard.read", "reporting.sales.read", "media.asset.read",
            "pos.shift.open", "pos.shift.close"
        ],
        "cashier" => ["catalog.menu.read", "inventory.stock.read", "inventory.reservation.create", "order.create", "order.read", "order.place", "payment.intent.create", "payment.intent.read", "customer.profile.read", "pos.shift.open", "pos.shift.close"],
        "inventory-controller" => ["inventory.stock.read", "inventory.stock.write", "inventory.reservation.create", "inventory.reservation.release"],
        "accountant" => ["order.read", "payment.intent.read", "reporting.dashboard.read", "reporting.sales.read"],
        "report-viewer" => ["catalog.menu.read", "inventory.stock.read", "order.read", "payment.intent.read", "customer.profile.read", "reporting.dashboard.read", "reporting.sales.read", "media.asset.read"],
        _ => throw new ArgumentException($"Unsupported product role '{roleCode}'.")
    };
}
