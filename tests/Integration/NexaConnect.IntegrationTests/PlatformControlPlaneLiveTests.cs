extern alias DIRECTORY;

using Microsoft.Extensions.Configuration;
using Npgsql;
using NexaConnect.Contracts.Platform;
using System.Net.Http.Json;
using PlatformAdministrationService = DIRECTORY::NexaConnect.Services.PlatformDirectory.Application.Administration.PlatformAdministrationService;
using IPlatformControlPlaneStore = DIRECTORY::NexaConnect.Services.PlatformDirectory.Application.Administration.IPlatformControlPlaneStore;
using KeycloakPlatformIdentityAdministration = DIRECTORY::NexaConnect.Services.PlatformDirectory.Infrastructure.Identity.KeycloakPlatformIdentityAdministration;
using PostgresPlatformControlPlaneStore = DIRECTORY::NexaConnect.Services.PlatformDirectory.Infrastructure.Persistence.PostgresPlatformControlPlaneStore;

namespace NexaConnect.IntegrationTests;

public sealed class PlatformControlPlaneLiveTests : IAsyncLifetime
{
    private readonly string? _database = Environment.GetEnvironmentVariable("NEXACONNECT_PLATFORMDIRECTORY_INTEGRATION_DB");
    private readonly string? _keycloakBaseUrl = Environment.GetEnvironmentVariable("NEXACONNECT_KEYCLOAK_INTEGRATION_BASE_URL");
    private readonly string? _keycloakRealm = Environment.GetEnvironmentVariable("NEXACONNECT_KEYCLOAK_INTEGRATION_REALM");
    private readonly string? _keycloakClientId = Environment.GetEnvironmentVariable("NEXACONNECT_KEYCLOAK_INTEGRATION_CLIENT_ID");
    private readonly string? _keycloakClientSecret = Environment.GetEnvironmentVariable("NEXACONNECT_KEYCLOAK_INTEGRATION_CLIENT_SECRET");
    private readonly List<string> _generatedUsernames = [];
    private NpgsqlDataSource? _dataSource;
    private KeycloakPlatformIdentityAdministration? _identity;
    private string? _schema;

    [Fact]
    public async Task User_lifecycle_is_paged_role_mapped_and_audited()
    {
        if (!Configured()) return;
        var service = new PlatformAdministrationService(_identity!, new PostgresPlatformControlPlaneStore(_dataSource!, TimeProvider.System));
        string username = $"phase3-it-{Guid.NewGuid():N}";
        _generatedUsernames.Add(username);

        PlatformUserSummary created = await service.CreateUserAsync(
            new(username, null, true, ["platform-auditor"]), "integration-actor", CancellationToken.None);
        Assert.Equal(["platform-auditor"], created.Roles);

        IReadOnlyCollection<PlatformUserSummary> users = await service.ListUsersAsync(CancellationToken.None);
        Assert.Contains(users, user => user.SubjectId == created.SubjectId && user.Username == username);

        PlatformUserSummary changed = (await service.ChangeRolesAsync(
            created.SubjectId, new(["platform-support"]), "integration-actor", CancellationToken.None))!;
        Assert.Equal(["platform-support"], changed.Roles);

        PlatformUserSummary disabled = (await service.UpdateUserAsync(
            created.SubjectId, new(null, false), "integration-actor", CancellationToken.None))!;
        Assert.False(disabled.Enabled);

        IReadOnlyCollection<PlatformAuditRecord> audit = await service.QueryAuditAsync(
            new(null, null, "integration-actor", null, 10), CancellationToken.None);
        Assert.Equal(3, audit.Count);
        Assert.Contains(audit, record => record.Action == "platform-user.created" && record.ResourceId == created.SubjectId);
        Assert.Contains(audit, record => record.Action == "platform-user.roles-changed" && record.ResourceId == created.SubjectId);
        Assert.Contains(audit, record => record.Action == "platform-user.updated" && record.ResourceId == created.SubjectId);
        await Assert.ThrowsAsync<PostgresException>(() => MutateAuditAsync(created.SubjectId));
    }

    [Fact]
    public async Task Audit_failure_after_identity_creation_exposes_reconcilable_partial_state()
    {
        if (!Configured()) return;
        var service = new PlatformAdministrationService(_identity!, new FailingAuditStore());
        string username = $"phase3-partial-{Guid.NewGuid():N}";
        _generatedUsernames.Add(username);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateUserAsync(
            new(username, null, true, ["platform-auditor"]), "integration-actor", CancellationToken.None));

        PlatformUserSummary created = Assert.Single(
            await _identity!.ListUsersAsync(CancellationToken.None), user => user.Username == username);
        Assert.Equal(["platform-auditor"], created.Roles);
    }

    public async Task InitializeAsync()
    {
        if (!HasConfiguration() || !IsSafeEnvironment()) return;
        _schema = $"platform_control_plane_it_{Guid.NewGuid():N}";
        var builder = new NpgsqlConnectionStringBuilder(_database) { SearchPath = _schema };
        _dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{_schema}\";", connection))
            await create.ExecuteNonQueryAsync();
        await using (var schema = new NpgsqlCommand(SchemaSql, connection))
            await schema.ExecuteNonQueryAsync();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["KeycloakAdmin:Realm"] = _keycloakRealm,
            ["KeycloakAdmin:ClientId"] = _keycloakClientId,
            ["KeycloakAdmin:ClientSecret"] = _keycloakClientSecret
        }).Build();
        _identity = new KeycloakPlatformIdentityAdministration(
            new HttpClient { BaseAddress = new Uri(_keycloakBaseUrl!) }, configuration);
    }

    public async Task DisposeAsync()
    {
        List<Exception> failures = [];
        try
        {
            foreach (string username in _generatedUsernames)
                try { await DeleteUsersByUsernameAsync(username); }
                catch (Exception exception) { failures.Add(exception); }
        }
        finally
        {
            if (_dataSource is not null && _schema is not null)
            {
                try
                {
                    await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();
                    await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE;", connection);
                    await drop.ExecuteNonQueryAsync();
                }
                catch (Exception exception) { failures.Add(exception); }
                await _dataSource.DisposeAsync();
            }
        }
        if (failures.Count > 0) throw new AggregateException("Live Phase 3 test cleanup failed.", failures);
    }

    private bool Configured()
    {
        if (_identity is not null && _dataSource is not null && IsSafeEnvironment()) return true;
        Console.WriteLine("Live Phase 3 tests require Platform Directory integration DB and Keycloak integration settings in Development/Test/Testing.");
        return false;
    }

    private bool HasConfiguration() => !string.IsNullOrWhiteSpace(_database)
        && Uri.TryCreate(_keycloakBaseUrl, UriKind.Absolute, out _)
        && !string.IsNullOrWhiteSpace(_keycloakRealm)
        && !string.IsNullOrWhiteSpace(_keycloakClientId)
        && !string.IsNullOrWhiteSpace(_keycloakClientSecret);

    private async Task DeleteUsersByUsernameAsync(string username)
    {
        using var client = new HttpClient { BaseAddress = new Uri(_keycloakBaseUrl!) };
        using HttpResponseMessage tokenResponse = await client.PostAsync(
            $"realms/{Uri.EscapeDataString(_keycloakRealm!)}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials", ["client_id"] = _keycloakClientId!, ["client_secret"] = _keycloakClientSecret!
            }));
        tokenResponse.EnsureSuccessStatusCode();
        System.Text.Json.JsonElement token = await tokenResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        string accessToken = token.GetProperty("access_token").GetString()!;
        using var listRequest = new HttpRequestMessage(HttpMethod.Get,
            $"admin/realms/{Uri.EscapeDataString(_keycloakRealm!)}/users?username={Uri.EscapeDataString(username)}&exact=true");
        listRequest.Headers.Authorization = new("Bearer", accessToken);
        using HttpResponseMessage listResponse = await client.SendAsync(listRequest);
        listResponse.EnsureSuccessStatusCode();
        System.Text.Json.JsonElement users = await listResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        foreach (System.Text.Json.JsonElement user in users.EnumerateArray())
        {
            using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete,
                $"admin/realms/{Uri.EscapeDataString(_keycloakRealm!)}/users/{Uri.EscapeDataString(user.GetProperty("id").GetString()!)}");
            deleteRequest.Headers.Authorization = new("Bearer", accessToken);
            using HttpResponseMessage deleteResponse = await client.SendAsync(deleteRequest);
            deleteResponse.EnsureSuccessStatusCode();
        }
    }

    private async Task MutateAuditAsync(string resourceId)
    {
        await using NpgsqlConnection connection = await _dataSource!.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE platform_audit_records SET outcome = outcome WHERE resource_id = $1;", connection);
        command.Parameters.AddWithValue(resourceId);
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

    private sealed class FailingAuditStore : IPlatformControlPlaneStore
    {
        public Task RecordAuditAsync(string action, string resourceType, string resourceId, string actorSubjectId, string outcome, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated audit outage.");
        public Task<IReadOnlyCollection<PlatformAuditRecord>> QueryAuditAsync(PlatformAuditQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<PlatformAuditRecord>>([]);
        public Task<PlatformSummary> GetSummaryAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private const string SchemaSql = """
        CREATE TABLE platform_audit_records
        (
            id uuid PRIMARY KEY, action text NOT NULL, resource_type text NOT NULL,
            resource_id text NOT NULL, actor_subject_id text NOT NULL, outcome text NOT NULL,
            occurred_at_utc timestamptz NOT NULL
        );
        CREATE FUNCTION prevent_platform_audit_record_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'platform_audit_records is append-only'; END;
        $$;
        CREATE TRIGGER tr_platform_audit_records_append_only BEFORE UPDATE OR DELETE ON platform_audit_records
        FOR EACH ROW EXECUTE FUNCTION prevent_platform_audit_record_mutation();
        """;
}
