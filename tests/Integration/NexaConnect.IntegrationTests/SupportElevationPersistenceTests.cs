extern alias DIRECTORY;

using Npgsql;
using SupportElevation = DIRECTORY::NexaConnect.Services.PlatformDirectory.Domain.Support.SupportElevation;
using SupportRepository = DIRECTORY::NexaConnect.Services.PlatformDirectory.Infrastructure.Persistence.PostgresSupportElevationRepository;

namespace NexaConnect.IntegrationTests;

public sealed class SupportElevationPersistenceTests : IAsyncLifetime
{
    private readonly string? _configuredConnectionString =
        Environment.GetEnvironmentVariable("NEXACONNECT_PLATFORMDIRECTORY_INTEGRATION_DB");
    private NpgsqlDataSource? _dataSource;
    private string? _schema;

    [Fact]
    public async Task Approval_is_effective_only_until_revocation_and_all_transitions_are_audited()
    {
        if (!DatabaseConfigured()) return;
        Guid organizationId = Guid.NewGuid();
        await SeedOrganizationAndApplicationAsync(organizationId);
        var repository = new SupportRepository(_dataSource!);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SupportElevation elevation = SupportElevation.Request(
            Guid.NewGuid(), organizationId, "nexa_connect", "support-1",
            "Investigate failed tenant synchronization", 60, now);

        await repository.CreateAsync(elevation, CancellationToken.None);
        elevation.Approve("platform-admin-1", now.AddMinutes(1));
        Assert.True(await repository.TryApproveAsync(elevation, CancellationToken.None));
        Assert.NotNull(await repository.FindEffectiveAsync(
            organizationId, "nexa_connect", "support-1", now.AddMinutes(2), CancellationToken.None));

        elevation.Revoke("platform-admin-1", now.AddMinutes(3));
        Assert.True(await repository.TryRevokeAsync(elevation, CancellationToken.None));
        Assert.Null(await repository.FindEffectiveAsync(
            organizationId, "nexa_connect", "support-1", now.AddMinutes(4), CancellationToken.None));
        Assert.Equal(3, await CountAuditRowsAsync(elevation.Id));
        await Assert.ThrowsAsync<PostgresException>(() => MutateAuditAsync(elevation.Id));
    }

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(_configuredConnectionString) || !IsSafeEnvironment()) return;
        _schema = $"platform_directory_it_{Guid.NewGuid():N}";
        var builder = new NpgsqlConnectionStringBuilder(_configuredConnectionString) { SearchPath = _schema };
        _dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{_schema}\";", connection))
            await create.ExecuteNonQueryAsync();
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
        Console.WriteLine("Support-elevation PostgreSQL tests require NEXACONNECT_PLATFORMDIRECTORY_INTEGRATION_DB and a Development/Test/Testing environment.");
        return false;
    }

    private async Task SeedOrganizationAndApplicationAsync(Guid organizationId)
    {
        await using NpgsqlConnection connection = await _dataSource!.OpenConnectionAsync();
        Guid applicationId = Guid.NewGuid();
        await using var command = new NpgsqlCommand("""
            INSERT INTO organizations (id, status) VALUES ($1, 'active');
            INSERT INTO applications (id, code, status) VALUES ($2, 'nexa_connect', 'active');
            INSERT INTO organization_application_access (organization_id, application_id, status) VALUES ($1, $2, 'enabled');
            """, connection);
        command.Parameters.AddWithValue(organizationId);
        command.Parameters.AddWithValue(applicationId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> CountAuditRowsAsync(Guid elevationId)
    {
        await using NpgsqlConnection connection = await _dataSource!.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM support_elevation_audit WHERE support_elevation_id = $1;", connection);
        command.Parameters.AddWithValue(elevationId);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private async Task MutateAuditAsync(Guid elevationId)
    {
        await using NpgsqlConnection connection = await _dataSource!.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "DELETE FROM support_elevation_audit WHERE support_elevation_id = $1;", connection);
        command.Parameters.AddWithValue(elevationId);
        await command.ExecuteNonQueryAsync();
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
        CREATE TABLE organizations (id uuid PRIMARY KEY, status text NOT NULL);
        CREATE TABLE applications (id uuid PRIMARY KEY, code text UNIQUE NOT NULL, status text NOT NULL);
        CREATE TABLE organization_application_access
            (organization_id uuid NOT NULL, application_id uuid NOT NULL, status text NOT NULL,
             PRIMARY KEY (organization_id, application_id));
        CREATE TABLE support_elevations
        (
            id uuid PRIMARY KEY, organization_id uuid NOT NULL, application_code text NOT NULL,
            support_subject_id text NOT NULL, reason text NOT NULL, duration_minutes integer NOT NULL,
            status text NOT NULL, requested_at_utc timestamptz NOT NULL, approved_at_utc timestamptz NULL,
            expires_at_utc timestamptz NULL, revoked_at_utc timestamptz NULL,
            approved_by_subject_id text NULL, revoked_by_subject_id text NULL,
            created_at_utc timestamptz NOT NULL, updated_at_utc timestamptz NOT NULL
        );
        CREATE TABLE support_elevation_audit
        (
            id uuid PRIMARY KEY, support_elevation_id uuid NOT NULL REFERENCES support_elevations (id),
            action text NOT NULL, actor_subject_id text NOT NULL, occurred_at_utc timestamptz NOT NULL
        );
        CREATE FUNCTION prevent_support_elevation_audit_mutation()
        RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'support_elevation_audit is append-only'; END;
        $$;
        CREATE TRIGGER tr_support_elevation_audit_append_only
        BEFORE UPDATE OR DELETE ON support_elevation_audit
        FOR EACH ROW EXECUTE FUNCTION prevent_support_elevation_audit_mutation();
        """;
}
