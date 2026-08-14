extern alias AUTH;

using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Assignment = AUTH::NexaConnect.Services.Authorization.Application.Assignments;
using DecisionService = AUTH::NexaConnect.Services.Authorization.Infrastructure.Persistence.PostgresAuthorizationDecisionService;
using AssignmentRepository = AUTH::NexaConnect.Services.Authorization.Infrastructure.Persistence.PostgresAuthorizationAssignmentRepository;

namespace NexaConnect.IntegrationTests;

public sealed class AuthorizationAssignmentPersistenceTests : IAsyncLifetime
{
    private readonly string? _configuredConnectionString =
        Environment.GetEnvironmentVariable("NEXACONNECT_AUTHORIZATION_INTEGRATION_DB");
    private NpgsqlDataSource? _dataSource;
    private string? _schema;

    [Fact]
    public async Task Assignment_materializes_scoped_override_and_survives_a_fresh_decision_service()
    {
        if (!DatabaseConfigured()) return;

        Guid organizationId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        var command = new Assignment.AssignRoleCommand(
            "nexa_pos", organizationId, restaurantId, branchId, "cashier");
        var repository = new AssignmentRepository(_dataSource!);

        Assignment.RoleAssignmentResult result = await repository.AssignAsync(
            command, "integration-admin", CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.AssignmentId);
        Assert.Equal(2, await CountOverridesAsync("nexa_pos", organizationId, restaurantId, branchId));

        var firstDecision = new DecisionService(_dataSource!, NullLogger<DecisionService>.Instance);
        var secondDecision = new DecisionService(_dataSource!, NullLogger<DecisionService>.Instance);

        var decision = await firstDecision.DecideAsync(
            "nexa_pos", organizationId, restaurantId, branchId,
            "pos.shift.open", null, null, CancellationToken.None);
        var freshDecision = await secondDecision.DecideAsync(
            "nexa_pos", organizationId, restaurantId, branchId,
            "pos.shift.close", null, null, CancellationToken.None);

        Assert.True(decision.Granted);
        Assert.True(freshDecision.Granted);
    }

    [Fact]
    public async Task Organization_assignment_authorizes_organization_and_child_resources_only()
    {
        if (!DatabaseConfigured()) return;

        Guid organizationId = Guid.NewGuid();
        var repository = new AssignmentRepository(_dataSource!);
        await repository.AssignAsync(
            new Assignment.AssignRoleCommand("tenant-admin", organizationId, null, null, "tenant-admin"),
            "integration-admin", CancellationToken.None);

        var decisions = new DecisionService(_dataSource!, NullLogger<DecisionService>.Instance);
        Assert.True((await decisions.DecideAsync("tenant-admin", organizationId, null, null,
            "media.asset.read", null, null, CancellationToken.None)).Granted);
        Assert.True((await decisions.DecideAsync("tenant-admin", organizationId, Guid.NewGuid(), null,
            "restaurant.branch.manage", null, null, CancellationToken.None)).Granted);
        Assert.False((await decisions.DecideAsync("tenant-admin", Guid.NewGuid(), null, null,
            "media.asset.read", null, null, CancellationToken.None)).Granted);
    }

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(_configuredConnectionString) || !IsSafeEnvironment())
        {
            return;
        }

        _schema = $"authorization_it_{Guid.NewGuid():N}";
        var builder = new NpgsqlConnectionStringBuilder(_configuredConnectionString)
        {
            SearchPath = _schema
        };
        _dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();
        await using (var createSchema = new NpgsqlCommand($"CREATE SCHEMA \"{_schema}\";", connection))
        {
            await createSchema.ExecuteNonQueryAsync();
        }

        await using var schema = new NpgsqlCommand(SchemaSql, connection);
        await schema.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is null || _schema is null) return;

        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();
        await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE;", connection);
        await drop.ExecuteNonQueryAsync();
        await _dataSource.DisposeAsync();
    }

    private bool DatabaseConfigured()
    {
        if (_dataSource is not null && IsSafeEnvironment()) return true;

        Console.WriteLine(
            "Authorization PostgreSQL tests require NEXACONNECT_AUTHORIZATION_INTEGRATION_DB and a Development/Test/Testing environment.");
        return false;
    }

    private async Task<long> CountOverridesAsync(
        string subjectId, Guid organizationId, Guid restaurantId, Guid branchId)
    {
        await using NpgsqlConnection connection = await _dataSource!.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            SELECT count(*)
            FROM authorization_user_permission_overrides o
            JOIN authorization_resource_scopes s ON s.id = o.scope_id
            WHERE o.subject_id = $1 AND o.permission_code IN ('pos.shift.open', 'pos.shift.close')
              AND o.effect = 'allow' AND o.status = 'active'
              AND s.organization_id = $2 AND s.restaurant_id = $3 AND s.branch_id = $4
              AND s.status = 'active';
            """, connection);
        command.Parameters.AddWithValue(subjectId);
        command.Parameters.AddWithValue(organizationId);
        command.Parameters.AddWithValue(restaurantId);
        command.Parameters.AddWithValue(branchId);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static bool IsSafeEnvironment()
    {
        string? environment = Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        return string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environment, "Test", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environment, "Testing", StringComparison.OrdinalIgnoreCase);
    }

    private const string SchemaSql = """
        CREATE TABLE authorization_resource_scopes
        (
            id uuid PRIMARY KEY, organization_id uuid NOT NULL, restaurant_id uuid NULL,
            branch_id uuid NULL, status text NOT NULL, updated_at_utc timestamptz NOT NULL,
            CONSTRAINT uq_scopes_org_restaurant_branch UNIQUE NULLS NOT DISTINCT
                (organization_id, restaurant_id, branch_id)
        );
        CREATE TABLE authorization_roles
        (
            id uuid PRIMARY KEY, organization_id uuid NOT NULL, code text NOT NULL,
            name text NOT NULL, status text NOT NULL,
            CONSTRAINT uq_roles_org_code UNIQUE (organization_id, code)
        );
        CREATE TABLE authorization_role_permissions
        (
            role_id uuid NOT NULL REFERENCES authorization_roles (id),
            permission_code text NOT NULL, PRIMARY KEY (role_id, permission_code)
        );
        CREATE TABLE authorization_role_assignments
        (
            id uuid PRIMARY KEY, role_id uuid NOT NULL REFERENCES authorization_roles (id),
            subject_id text NOT NULL, scope_id uuid NOT NULL REFERENCES authorization_resource_scopes (id),
            status text NOT NULL, assigned_at_utc timestamptz NOT NULL, assigned_by_subject_id text NOT NULL,
            CONSTRAINT uq_assignments_role_subject_scope UNIQUE (role_id, subject_id, scope_id)
        );
        CREATE TABLE authorization_user_permission_overrides
        (
            id uuid PRIMARY KEY, subject_id text NOT NULL,
            scope_id uuid NOT NULL REFERENCES authorization_resource_scopes (id),
            permission_code text NOT NULL, effect text NOT NULL, status text NOT NULL,
            CONSTRAINT uq_overrides_subject_scope_permission UNIQUE (subject_id, scope_id, permission_code)
        );
        CREATE TABLE financial_approval_limits
        (
            id uuid PRIMARY KEY, restaurant_id uuid NOT NULL, principal_type text NOT NULL,
            principal_id text NOT NULL, action_code text NOT NULL, currency char(3) NOT NULL,
            maximum_amount numeric(19,4) NOT NULL, status text NOT NULL
        );
        CREATE TABLE authorization_decisions
        (
            id uuid PRIMARY KEY, subject_id text NOT NULL, organization_id uuid NOT NULL,
            restaurant_id uuid NULL, branch_id uuid NULL, action_code text NOT NULL,
            granted boolean NOT NULL, evaluated_limit numeric(19,4) NULL, currency char(3) NULL,
            decided_at_utc timestamptz NOT NULL, policy_version integer NOT NULL
        );
        """;
}
