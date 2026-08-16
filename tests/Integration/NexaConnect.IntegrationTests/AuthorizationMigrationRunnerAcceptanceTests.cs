extern alias MIGRATIONS;

using MigrationApplication = MIGRATIONS::MigrationApplication;
using Npgsql;

namespace NexaConnect.IntegrationTests;

[Collection("Authorization migration runner acceptance")]
public sealed class AuthorizationMigrationRunnerAcceptanceTests
{
    [AuthorizationMigrationFact]
    public async Task Existing_operational_roles_are_backfilled_and_downgraded()
    {
        string admin = Environment.GetEnvironmentVariable("NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB")!;
        string database = $"nexaconnect_authorization_clean_it_{Guid.NewGuid():N}";
        Validate(database);
        var adminBuilder = new NpgsqlConnectionStringBuilder(admin) { Database = "postgres" };
        await using var adminSource = NpgsqlDataSource.Create(adminBuilder.ConnectionString);
        await CreateAsync(adminSource, database);
        string? previous = Environment.GetEnvironmentVariable("NEXACONNECT_AUTHORIZATION_DB");
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(admin) { Database = database };
            Environment.SetEnvironmentVariable("NEXACONNECT_AUTHORIZATION_DB", builder.ConnectionString);
            string root = Path.Combine(RepositoryRoot(), "src", "Tools", "NexaConnect.DataMigration", "Scripts");
            Assert.Equal(0, await RunAsync(root, 2));
            await using var dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
            await SeedRolesAsync(dataSource);
            Assert.Equal(0, await RunAsync(root, 3));
            Assert.Equal(4L, await PermissionCountAsync(dataSource));
            Assert.Equal(0, await RunAsync(root, 2, destructive: true));
            Assert.Equal(0L, await PermissionCountAsync(dataSource));
        }
        finally
        {
            Environment.SetEnvironmentVariable("NEXACONNECT_AUTHORIZATION_DB", previous);
            await DropAsync(adminSource, database);
        }
    }

    private static Task<int> RunAsync(string root, int target, bool destructive = false)
    {
        var args = new List<string> { "--service", "Authorization", "--scripts-root", root, "--target", target.ToString(), "--application-version", "0.8.0", "--confirm" };
        if (destructive) args.AddRange(["--allow-destructive", "--backup-verified"]);
        return MigrationApplication.RunAsync(args.ToArray());
    }

    private static async Task SeedRolesAsync(NpgsqlDataSource dataSource)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        foreach (string code in new[] { "tenant-admin", "store-manager" })
        {
            await using var command = new NpgsqlCommand("INSERT INTO authorization_roles(id,organization_id,code,name,status) VALUES($1,$2,$3,$4,'active')", connection);
            command.Parameters.AddWithValue(Guid.NewGuid());
            command.Parameters.AddWithValue(Guid.NewGuid());
            command.Parameters.AddWithValue(code);
            command.Parameters.AddWithValue(code);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task<long> PermissionCountAsync(NpgsqlDataSource dataSource)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return Convert.ToInt64(await new NpgsqlCommand("SELECT count(*) FROM authorization_role_permissions WHERE permission_code IN ('kitchen.ticket.read','kitchen.ticket.transition')", connection).ExecuteScalarAsync());
    }

    private static async Task CreateAsync(NpgsqlDataSource source, string database)
    {
        await using var connection = await source.OpenConnectionAsync();
        await new NpgsqlCommand($"CREATE DATABASE {new NpgsqlCommandBuilder().QuoteIdentifier(database)}", connection).ExecuteNonQueryAsync();
    }

    private static async Task DropAsync(NpgsqlDataSource source, string database)
    {
        Validate(database);
        await using var connection = await source.OpenConnectionAsync();
        await new NpgsqlCommand($"DROP DATABASE IF EXISTS {new NpgsqlCommandBuilder().QuoteIdentifier(database)} WITH (FORCE)", connection).ExecuteNonQueryAsync();
    }

    private static void Validate(string database)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(database, "^nexaconnect_authorization_clean_it_[a-f0-9]{32}$"))
            throw new InvalidOperationException("Unsafe Authorization acceptance database name.");
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NexaConnect.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}

[CollectionDefinition("Authorization migration runner acceptance", DisableParallelization = true)]
public sealed class AuthorizationMigrationRunnerCollection;

public sealed class AuthorizationMigrationFactAttribute : FactAttribute
{
    public AuthorizationMigrationFactAttribute()
    {
        string? environment = Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (Environment.GetEnvironmentVariable("NEXACONNECT_AUTHORIZATION_CLEAN_INSTALL_ACCEPTANCE") != "1"
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB"))
            || environment is not ("Development" or "Test" or "Testing"))
            Skip = "Authorization migration acceptance requires opt-in, admin connection, and safe environment.";
    }
}
