extern alias CATALOG;
extern alias MIGRATIONS;

using CatalogCreateMenuItem = CATALOG::NexaConnect.Services.Catalog.Application.Menu.CreateMenuItem;
using CatalogMenuMutationContext = CATALOG::NexaConnect.Services.Catalog.Application.Menu.MenuMutationContext;
using CatalogRepository = CATALOG::NexaConnect.Services.Catalog.Infrastructure.PostgresMenuCatalog;
using MigrationApplication = MIGRATIONS::MigrationApplication;
using Npgsql;

namespace NexaConnect.IntegrationTests;

[Collection("Catalog migration runner acceptance")]
public sealed class CatalogMigrationRunnerAcceptanceTests
{
    [Fact]
    public async Task Empty_database_upgrades_to_4_downgrades_to_3_and_re_upgrades_to_4()
    {
        if (!Configured(out string adminConnectionString)) return;
        string databaseName = $"nexaconnect_catalog_clean_it_{Guid.NewGuid():N}";
        ValidateDatabaseName(databaseName);
        var adminBuilder = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = "postgres" };
        await using NpgsqlDataSource adminDataSource = NpgsqlDataSource.Create(adminBuilder.ConnectionString);
        await CreateDatabaseAsync(adminDataSource, databaseName);
        string? previousCatalogConnection = Environment.GetEnvironmentVariable("NEXACONNECT_CATALOG_DB");
        try
        {
            var catalogBuilder = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = databaseName };
            Environment.SetEnvironmentVariable("NEXACONNECT_CATALOG_DB", catalogBuilder.ConnectionString);
            string scriptsRoot = Path.Combine(FindRepositoryRoot(), "src", "Tools", "NexaConnect.DataMigration", "Scripts");

            Assert.Equal(0, await RunMigrationAsync(scriptsRoot, 4));
            await using NpgsqlDataSource catalogDataSource = NpgsqlDataSource.Create(catalogBuilder.ConnectionString);
            await AssertHistoryAsync(catalogDataSource, [1, 2, 3, 4]);
            await AssertSchema4Async(catalogDataSource);
            await ExerciseRepositoryAsync(catalogDataSource, "clean-install");

            Assert.Equal(0, await RunMigrationAsync(scriptsRoot, 3, destructive: true));
            await AssertHistoryAsync(catalogDataSource, [1, 2, 3]);
            await using (NpgsqlConnection connection = await catalogDataSource.OpenConnectionAsync())
            {
                Assert.Equal("outbox_messages", await ScalarTextAsync(connection, "SELECT to_regclass('public.outbox_messages')::text"));
                Assert.Null(await ScalarTextAsync(connection, "SELECT to_regclass('public.catalog_audit_records')::text"));
            }

            Assert.Equal(0, await RunMigrationAsync(scriptsRoot, 4));
            await AssertHistoryAsync(catalogDataSource, [1, 2, 3, 4]);
            await AssertSchema4Async(catalogDataSource);
            await ExerciseRepositoryAsync(catalogDataSource, "re-upgrade");
        }
        finally
        {
            Environment.SetEnvironmentVariable("NEXACONNECT_CATALOG_DB", previousCatalogConnection);
            await DropDatabaseAsync(adminDataSource, databaseName);
        }
    }

    private static Task<int> RunMigrationAsync(string scriptsRoot, int target, bool destructive = false)
    {
        var arguments = new List<string> { "--service", "Catalog", "--scripts-root", scriptsRoot, "--target", target.ToString(), "--application-version", "0.6.0", "--confirm" };
        if (destructive) arguments.AddRange(["--allow-destructive", "--backup-verified"]);
        return MigrationApplication.RunAsync(arguments.ToArray());
    }

    private static async Task AssertHistoryAsync(NpgsqlDataSource dataSource, int[] expectedVersions)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("SELECT version,metadata_checksum_sha256,up_checksum_sha256,down_checksum_sha256 FROM public.nexaconnect_schema_migrations ORDER BY version", connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        var actual = new List<int>();
        while (await reader.ReadAsync())
        {
            actual.Add(reader.GetInt32(0));
            Assert.All([reader.GetString(1), reader.GetString(2), reader.GetString(3)], checksum => Assert.Matches("^[0-9A-F]{64}$", checksum));
        }
        Assert.Equal(expectedVersions, actual);
    }

    private static async Task AssertSchema4Async(NpgsqlDataSource dataSource)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        Assert.Equal("outbox_messages", await ScalarTextAsync(connection, "SELECT to_regclass('public.outbox_messages')::text"));
        Assert.Equal("catalog_audit_records", await ScalarTextAsync(connection, "SELECT to_regclass('public.catalog_audit_records')::text"));
        await using var trigger = new NpgsqlCommand("SELECT count(*) FROM pg_trigger WHERE tgname='tr_catalog_audit_records_append_only' AND NOT tgisinternal", connection);
        Assert.Equal(1L, Convert.ToInt64(await trigger.ExecuteScalarAsync()));
    }

    private static async Task ExerciseRepositoryAsync(NpgsqlDataSource dataSource, string name)
    {
        Guid organizationId = Guid.NewGuid(); Guid branchId = Guid.NewGuid(); Guid productId = Guid.NewGuid();
        var repository = new CatalogRepository(dataSource);
        repository.AddForOrganizationBranch(organizationId, branchId,
            new CatalogCreateMenuItem(productId, name, 10m, "USD", "grill"), new CatalogMenuMutationContext("migration-acceptance", Guid.NewGuid()));
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("SELECT (SELECT count(*) FROM catalog_menu_items WHERE product_id=$1),(SELECT count(*) FROM catalog_audit_records WHERE product_id=$1),(SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1)", connection);
        command.Parameters.AddWithValue(productId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(); await reader.ReadAsync();
        Assert.Equal(1L, reader.GetInt64(0)); Assert.Equal(1L, reader.GetInt64(1)); Assert.Equal(2L, reader.GetInt64(2));
    }

    private static async Task<string?> ScalarTextAsync(NpgsqlConnection connection, string sql)
    {
        object? value = await new NpgsqlCommand(sql, connection).ExecuteScalarAsync();
        return value is null or DBNull ? null : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task CreateDatabaseAsync(NpgsqlDataSource adminDataSource, string databaseName)
    {
        await using NpgsqlConnection connection = await adminDataSource.OpenConnectionAsync();
        string quoted = new NpgsqlCommandBuilder().QuoteIdentifier(databaseName);
        await using var command = new NpgsqlCommand($"CREATE DATABASE {quoted}", connection); await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(NpgsqlDataSource adminDataSource, string databaseName)
    {
        ValidateDatabaseName(databaseName);
        await using NpgsqlConnection connection = await adminDataSource.OpenConnectionAsync();
        string quoted = new NpgsqlCommandBuilder().QuoteIdentifier(databaseName);
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS {quoted} WITH (FORCE)", connection); await command.ExecuteNonQueryAsync();
    }

    private static void ValidateDatabaseName(string databaseName)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(databaseName, "^nexaconnect_catalog_clean_it_[a-f0-9]{32}$"))
            throw new InvalidOperationException("Refusing to manage a database outside the Catalog acceptance naming boundary.");
    }

    private static bool Configured(out string adminConnectionString)
    {
        adminConnectionString = Environment.GetEnvironmentVariable("NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB") ?? string.Empty;
        string? environment = Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        bool safeEnvironment = environment is "Development" or "Test" or "Testing";
        bool validConnection = false;
        try { _ = new NpgsqlConnectionStringBuilder(adminConnectionString); validConnection = !string.IsNullOrWhiteSpace(adminConnectionString); }
        catch (ArgumentException) { }
        if (Environment.GetEnvironmentVariable("NEXACONNECT_CATALOG_CLEAN_INSTALL_ACCEPTANCE") == "1" && safeEnvironment && validConnection) return true;
        Console.WriteLine("Catalog clean-install acceptance requires NEXACONNECT_CATALOG_CLEAN_INSTALL_ACCEPTANCE=1, NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB, and a Development/Test/Testing environment."); return false;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NexaConnect.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the NexaConnect repository root.");
    }
}

[CollectionDefinition("Catalog migration runner acceptance", DisableParallelization = true)]
public sealed class CatalogMigrationRunnerAcceptanceCollection;
