extern alias AUTH;

using DecisionStore = AUTH::NexaConnect.Services.Authorization.Infrastructure.Persistence.PostgresAuthorizationDecisionStore;
using Npgsql;
using Assignment = AUTH::NexaConnect.Services.Authorization.Application.Assignments;
using DecisionService = AUTH::NexaConnect.Services.Authorization.Application.Decisions.AuthorizationDecisionService;
using AssignmentRepository = AUTH::NexaConnect.Services.Authorization.Infrastructure.Persistence.PostgresAuthorizationAssignmentRepository;

namespace NexaConnect.IntegrationTests;

public sealed class AuthorizationAssignmentPersistenceTests : IAsyncLifetime
{
    private readonly string? _configuredConnectionString =
        Environment.GetEnvironmentVariable("NEXACONNECT_AUTHORIZATION_INTEGRATION_DB");
    private NpgsqlDataSource? _dataSource;
    private string? _schema;

    [AuthorizationDatabaseFact]
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

        var firstDecision = new DecisionService(new DecisionStore(_dataSource!));
        var secondDecision = new DecisionService(new DecisionStore(_dataSource!));

        var decision = await firstDecision.DecideAsync(
            "nexa_pos", organizationId, restaurantId, branchId,
            "pos.shift.open", null, null, CancellationToken.None);
        var freshDecision = await secondDecision.DecideAsync(
            "nexa_pos", organizationId, restaurantId, branchId,
            "pos.shift.close", null, null, CancellationToken.None);

        Assert.True(decision.Granted);
        Assert.True(freshDecision.Granted);
    }

    [AuthorizationDatabaseFact]
    public async Task Organization_assignment_authorizes_organization_and_child_resources_only()
    {
        if (!DatabaseConfigured()) return;

        Guid organizationId = Guid.NewGuid();
        var repository = new AssignmentRepository(_dataSource!);
        await repository.AssignAsync(
            new Assignment.AssignRoleCommand("tenant-admin", organizationId, null, null, "tenant-admin"),
            "integration-admin", CancellationToken.None);

        var decisions = new DecisionService(new DecisionStore(_dataSource!));
        Assert.True((await decisions.DecideAsync("tenant-admin", organizationId, null, null,
            "media.asset.read", null, null, CancellationToken.None)).Granted);
        Assert.True((await decisions.DecideAsync("tenant-admin", organizationId, Guid.NewGuid(), null,
            "restaurant.branch.manage", null, null, CancellationToken.None)).Granted);
        Assert.False((await decisions.DecideAsync("tenant-admin", Guid.NewGuid(), null, null,
            "media.asset.read", null, null, CancellationToken.None)).Granted);
    }

    [AuthorizationDatabaseFact]
    public async Task Financial_review_roles_created_after_migration_preserve_reader_resolver_separation()
    {
        if (!DatabaseConfigured()) return;

        Guid organizationId=Guid.NewGuid(),restaurantId=Guid.NewGuid(),branchId=Guid.NewGuid();
        var repository=new AssignmentRepository(_dataSource!);
        await repository.AssignAsync(new Assignment.AssignRoleCommand("review-reader",organizationId,restaurantId,branchId,"accountant"),"integration-admin",default);
        await repository.AssignAsync(new Assignment.AssignRoleCommand("review-resolver",organizationId,restaurantId,null,"store-manager"),"integration-admin",default);
        var decisions=new DecisionService(new DecisionStore(_dataSource!));

        Assert.True((await decisions.DecideAsync("review-reader",organizationId,restaurantId,branchId,"order.payment-review.read",null,null,default)).Granted);
        Assert.False((await decisions.DecideAsync("review-reader",organizationId,restaurantId,branchId,"order.payment-review.resolve",null,null,default)).Granted);
        Assert.True((await decisions.DecideAsync("review-resolver",organizationId,restaurantId,branchId,"order.payment-review.read",null,null,default)).Granted);
        Assert.True((await decisions.DecideAsync("review-resolver",organizationId,restaurantId,branchId,"order.payment-review.resolve",null,null,default)).Granted);
        Assert.True((await decisions.DecideAsync("review-reader",organizationId,restaurantId,branchId,"pos.cash-review.read",null,null,default)).Granted);
        Assert.False((await decisions.DecideAsync("review-reader",organizationId,restaurantId,branchId,"pos.cash-review.resolve",null,null,default)).Granted);
        Assert.True((await decisions.DecideAsync("review-resolver",organizationId,restaurantId,branchId,"pos.cash-review.read",null,null,default)).Granted);
        Assert.True((await decisions.DecideAsync("review-resolver",organizationId,restaurantId,branchId,"pos.cash-review.resolve",null,null,default)).Granted);
    }

    [AuthorizationDatabaseFact]
    public async Task Explicit_deny_beats_role_and_each_request_records_current_policy()
    {
        if (!DatabaseConfigured()) return;
        Guid org=Guid.NewGuid(), restaurant=Guid.NewGuid(), branch=Guid.NewGuid();
        await new AssignmentRepository(_dataSource!).AssignAsync(new("reader",org,restaurant,branch,"accountant"),"test",default);
        var service=new DecisionService(new DecisionStore(_dataSource!));
        Assert.True((await service.DecideAsync("reader",org,restaurant,branch,"pos.cash-review.read",null,null,default)).Granted);
        await Effect("reader",org,restaurant,branch,"pos.cash-review.read","deny");
        var denied=await service.DecideAsync("reader",org,restaurant,branch,"pos.cash-review.read",null,null,default);
        Assert.False(denied.Granted);
        await using var audit=_dataSource!.CreateCommand("SELECT count(*) FROM authorization_decisions WHERE id=$1 AND NOT granted AND policy_version=2");
        audit.Parameters.AddWithValue(denied.Id); Assert.Equal(1L,await audit.ExecuteScalarAsync());
        await using var revoke=_dataSource.CreateCommand("UPDATE authorization_user_permission_overrides SET status='revoked' WHERE subject_id='reader'");
        await revoke.ExecuteNonQueryAsync();
        Assert.True((await service.DecideAsync("reader",org,restaurant,branch,"pos.cash-review.read",null,null,default)).Granted);
    }

    [AuthorizationDatabaseFact]
    public async Task Most_specific_active_override_wins_without_cross_tenant_leakage()
    {
        if (!DatabaseConfigured()) return;
        Guid org=Guid.NewGuid(), restaurant=Guid.NewGuid(), branch=Guid.NewGuid();
        var repository=new AssignmentRepository(_dataSource!);
        await repository.AssignAsync(new("operator",org,null,null,"tenant-admin"),"test",default);
        await repository.AssignAsync(new("operator",org,restaurant,branch,"cashier"),"test",default);
        var service=new DecisionService(new DecisionStore(_dataSource!));
        await Effect("operator",org,null,null,"pos.shift.open","deny");
        Assert.True((await service.DecideAsync("operator",org,restaurant,branch,"pos.shift.open",null,null,default)).Granted);
        Assert.False((await service.DecideAsync("operator",org,restaurant,Guid.NewGuid(),"pos.shift.open",null,null,default)).Granted);
        await Effect("operator",org,null,null,"pos.shift.open","allow");
        await Effect("operator",org,restaurant,branch,"pos.shift.open","deny");
        Assert.False((await service.DecideAsync("operator",org,restaurant,branch,"pos.shift.open",null,null,default)).Granted);
        Assert.False((await service.DecideAsync("operator",Guid.NewGuid(),restaurant,branch,"pos.shift.open",null,null,default)).Granted);
    }

    [AuthorizationDatabaseFact]
    public async Task Financial_limit_uses_only_active_roles_in_the_requested_scope()
    {
        if (!DatabaseConfigured()) return;
        Guid org=Guid.NewGuid(), other=Guid.NewGuid(), restaurant=Guid.NewGuid(), branch=Guid.NewGuid();
        var repository=new AssignmentRepository(_dataSource!);
        await repository.AssignAsync(new("reader",org,restaurant,branch,"accountant"),"test",default);
        await repository.AssignAsync(new("reader",other,Guid.NewGuid(),Guid.NewGuid(),"accountant"),"test",default);
        async Task Limit(Guid organization)
        {
            await using var command=_dataSource!.CreateCommand("INSERT INTO financial_approval_limits(id,restaurant_id,principal_type,principal_id,action_code,currency,maximum_amount,status) SELECT $1,$2,'role',id::text,'pos.cash-review.read','THB',100,'active' FROM authorization_roles WHERE organization_id=$3");
            command.Parameters.AddWithValue(Guid.NewGuid()); command.Parameters.AddWithValue(restaurant);command.Parameters.AddWithValue(organization);await command.ExecuteNonQueryAsync();
        }
        var service=new DecisionService(new DecisionStore(_dataSource!));
        await Limit(other);
        Assert.False((await service.DecideAsync("reader",org,restaurant,branch,"pos.cash-review.read",10,"THB",default)).Granted);
        await Limit(org);
        Assert.True((await service.DecideAsync("reader",org,restaurant,branch,"pos.cash-review.read",100,"THB",default)).Granted);
        Assert.False((await service.DecideAsync("reader",org,restaurant,branch,"pos.cash-review.read",101,"THB",default)).Granted);
    }

    private async Task Effect(string subject,Guid org,Guid? restaurant,Guid? branch,string permission,string effect)
    {
        await using var command=_dataSource!.CreateCommand("UPDATE authorization_user_permission_overrides o SET effect=$1 FROM authorization_resource_scopes s WHERE s.id=o.scope_id AND o.subject_id=$2 AND s.organization_id=$3 AND s.restaurant_id IS NOT DISTINCT FROM $4 AND s.branch_id IS NOT DISTINCT FROM $5 AND o.permission_code=$6");
        command.Parameters.AddWithValue(effect);command.Parameters.AddWithValue(subject);command.Parameters.AddWithValue(org);
        command.Parameters.AddWithValue((object?)restaurant??DBNull.Value);command.Parameters.AddWithValue((object?)branch??DBNull.Value);command.Parameters.AddWithValue(permission);
        Assert.Equal(1,await command.ExecuteNonQueryAsync());
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

    internal static bool IsSafeEnvironment()
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

public sealed class AuthorizationDatabaseFactAttribute : FactAttribute
{
    public AuthorizationDatabaseFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NEXACONNECT_AUTHORIZATION_INTEGRATION_DB")) ||
            !AuthorizationAssignmentPersistenceTests.IsSafeEnvironment())
            Skip = "Requires an explicit disposable PostgreSQL connection and safe test environment.";
    }
}