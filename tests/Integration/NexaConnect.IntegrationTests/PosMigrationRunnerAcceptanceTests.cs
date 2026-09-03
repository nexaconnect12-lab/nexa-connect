extern alias MIGRATIONS;
extern alias POS;

using MigrationApplication = MIGRATIONS::MigrationApplication;
using CashStore = POS::NexaConnect.Services.POS.Infrastructure.Persistence.PostgresCashSessionStore;
using Shift = POS::NexaConnect.Services.POS.Domain.Shifts.Shift;
using ShiftStore = POS::NexaConnect.Services.POS.Infrastructure.Persistence.PostgresShiftStore;
using Npgsql;

namespace NexaConnect.IntegrationTests;

[Collection("POS migration runner acceptance")]
public sealed class PosMigrationRunnerAcceptanceTests
{
    [PosMigrationAcceptanceFact]
    public async Task Empty_database_runs_0_to_4_to_3_to_4()
    {
        string adminConnectionString = Environment.GetEnvironmentVariable(
            "NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB")!;
        string databaseName = $"nexaconnect_pos_clean_it_{Guid.NewGuid():N}";
        ValidateDatabaseName(databaseName);

        var adminBuilder = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = "postgres" };
        await using NpgsqlDataSource adminDataSource = NpgsqlDataSource.Create(adminBuilder.ConnectionString);
        await CreateDatabaseAsync(adminDataSource, databaseName);
        string? previousConnection = Environment.GetEnvironmentVariable("NEXACONNECT_POS_DB");

        try
        {
            var posBuilder = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = databaseName };
            Environment.SetEnvironmentVariable("NEXACONNECT_POS_DB", posBuilder.ConnectionString);
            string scriptsRoot = Path.Combine(
                FindRepositoryRoot(), "src", "Tools", "NexaConnect.DataMigration", "Scripts");

            Assert.Equal(0, await RunMigrationAsync(scriptsRoot, 4));
            await using NpgsqlDataSource posDataSource = NpgsqlDataSource.Create(posBuilder.ConnectionString);
            await AssertHistoryAsync(posDataSource, [1, 2, 3, 4]);
            await AssertSchema3Async(posDataSource);
            Assert.Equal("pos_order_settlements",await RelationAsync(posDataSource,"pos_order_settlements"));
            await ExerciseRepositoriesAsync(posDataSource, "FIRST");

            Assert.Equal(0, await RunMigrationAsync(scriptsRoot, 3));
            await AssertHistoryAsync(posDataSource, [1, 2, 3]);
            Assert.Null(await RelationAsync(posDataSource,"pos_order_settlements"));
            Assert.Equal(1L, await ScalarLongAsync(posDataSource, "SELECT count(*) FROM cash_movements"));
            Assert.Equal(1L, await ScalarLongAsync(posDataSource, "SELECT count(*) FROM sync_operations"));

            Assert.Equal(0, await RunMigrationAsync(scriptsRoot, 4));
            await AssertHistoryAsync(posDataSource, [1, 2, 3, 4]);
            await AssertSchema3Async(posDataSource);
            await ExerciseRepositoriesAsync(posDataSource, "SECOND");
            Assert.Equal(2L, await ScalarLongAsync(posDataSource, "SELECT count(*) FROM cash_movements"));
            Assert.Equal(2L, await ScalarLongAsync(posDataSource, "SELECT count(*) FROM sync_operations"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("NEXACONNECT_POS_DB", previousConnection);
            await DropDatabaseAsync(adminDataSource, databaseName);
        }
    }

    private static Task<int> RunMigrationAsync(string scriptsRoot, int target)
    {
        var arguments = new List<string>([
            "--service", "POS",
            "--scripts-root", scriptsRoot,
            "--target", target.ToString(),
            "--application-version", "0.13.0",
            "--confirm"
        ]);
        if(target<4)arguments.AddRange(["--allow-destructive","--backup-verified"]);
        return MigrationApplication.RunAsync(arguments.ToArray());
    }

    private static async Task AssertHistoryAsync(NpgsqlDataSource dataSource, int[] expectedVersions)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            SELECT version, metadata_checksum_sha256, up_checksum_sha256, down_checksum_sha256
            FROM public.nexaconnect_schema_migrations
            ORDER BY version;
            """, connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        var actualVersions = new List<int>();
        while (await reader.ReadAsync())
        {
            actualVersions.Add(reader.GetInt32(0));
            Assert.All(
                [reader.GetString(1), reader.GetString(2), reader.GetString(3)],
                checksum => Assert.Matches("^[0-9A-F]{64}$", checksum));
        }

        Assert.Equal(expectedVersions, actualVersions);
    }

    private static async Task AssertSchema3Async(NpgsqlDataSource dataSource)
    {
        foreach (string table in new[]
        {
            "stores", "terminals", "shifts", "cash_sessions", "cash_movements",
            "sync_operations", "sync_checkpoints", "outbox_messages"
        })
        {
            Assert.Equal(table, await RelationAsync(dataSource, table));
        }

        Assert.Equal("authorization_decision_id", await ColumnAsync(dataSource, "authorization_decision_id"));
        Assert.Equal("close_authorization_decision_id", await ColumnAsync(dataSource, "close_authorization_decision_id"));
        Assert.Equal("uq_shifts_authorization_decision_id", await RelationAsync(dataSource, "uq_shifts_authorization_decision_id"));
        Assert.Equal("uq_shifts_close_authorization_decision_id", await RelationAsync(dataSource, "uq_shifts_close_authorization_decision_id"));
    }

    private static async Task AssertSchema2Async(NpgsqlDataSource dataSource)
    {
        Assert.Equal("shifts", await RelationAsync(dataSource, "shifts"));
        Assert.Equal("sync_operations", await RelationAsync(dataSource, "sync_operations"));
        Assert.Equal("authorization_decision_id", await ColumnAsync(dataSource, "authorization_decision_id"));
        Assert.Null(await ColumnAsync(dataSource, "close_authorization_decision_id"));
        Assert.Equal("uq_shifts_authorization_decision_id", await RelationAsync(dataSource, "uq_shifts_authorization_decision_id"));
        Assert.Null(await RelationAsync(dataSource, "uq_shifts_close_authorization_decision_id"));
    }

    private static async Task ExerciseRepositoriesAsync(NpgsqlDataSource dataSource, string suffix)
    {
        Guid restaurantId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();
        await InsertStoreAndTerminalAsync(dataSource, restaurantId, branchId, storeId, terminalId, suffix);

        var shiftStore = new ShiftStore(dataSource);
        Shift shift = Shift.Open(
            Guid.NewGuid(), storeId, terminalId, "migration-cashier", $"SHIFT-{suffix}",
            Guid.NewGuid(), DateTimeOffset.UtcNow);
        await shiftStore.CreateAsync(shift, CancellationToken.None);
        Assert.NotNull(await shiftStore.FindOpenAsync(shift.Id, CancellationToken.None));

        var cashStore = new CashStore(dataSource);
        Guid cashSessionId = await cashStore.OpenAsync(
            shift.Id, storeId, "USD", 10m, CancellationToken.None);
        Guid operationId = Guid.NewGuid();
        Assert.True(await cashStore.RecordMovementAsync(
            cashSessionId, "sale", 5m, "migration-cashier", "migration",
            operationId, terminalId, $"hash-{suffix}", CancellationToken.None));
        Assert.False(await cashStore.RecordMovementAsync(
            cashSessionId, "sale", 5m, "migration-cashier", "migration",
            operationId, terminalId, $"hash-{suffix}", CancellationToken.None));
        await cashStore.CloseAsync(cashSessionId, 15m, CancellationToken.None);

        shift.Close("migration-cashier", Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.True(await shiftStore.TryCloseAsync(shift, CancellationToken.None));
    }

    private static async Task InsertStoreAndTerminalAsync(
        NpgsqlDataSource dataSource,
        Guid restaurantId,
        Guid branchId,
        Guid storeId,
        Guid terminalId,
        string suffix)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        await using (var store = new NpgsqlCommand("""
            INSERT INTO stores
                (id, restaurant_id, branch_id, code, name, operational_status,
                 created_at_utc, created_by, updated_at_utc, updated_by)
            VALUES ($1, $2, $3, $4, $5, 'active', now(), 'migration', now(), 'migration');
            """, connection))
        {
            store.Parameters.AddWithValue(storeId);
            store.Parameters.AddWithValue(restaurantId);
            store.Parameters.AddWithValue(branchId);
            store.Parameters.AddWithValue($"store-{suffix.ToLowerInvariant()}-{storeId:N}");
            store.Parameters.AddWithValue($"Migration Store {suffix}");
            await store.ExecuteNonQueryAsync();
        }

        await using var terminal = new NpgsqlCommand("""
            INSERT INTO terminals
                (id, restaurant_id, store_id, code, device_type, registration_status,
                 registered_at_utc, created_at_utc, updated_at_utc)
            VALUES ($1, $2, $3, $4, 'pos', 'active', now(), now(), now());
            """, connection);
        terminal.Parameters.AddWithValue(terminalId);
        terminal.Parameters.AddWithValue(restaurantId);
        terminal.Parameters.AddWithValue(storeId);
        terminal.Parameters.AddWithValue($"terminal-{suffix.ToLowerInvariant()}-{terminalId:N}");
        await terminal.ExecuteNonQueryAsync();
    }

    private static async Task<string?> RelationAsync(NpgsqlDataSource dataSource, string relation)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("SELECT to_regclass($1)::text;", connection);
        command.Parameters.AddWithValue($"public.{relation}");
        object? value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : Convert.ToString(value);
    }

    private static async Task<string?> ColumnAsync(NpgsqlDataSource dataSource, string column)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'shifts' AND column_name = $1;
            """, connection);
        command.Parameters.AddWithValue(column);
        object? value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : Convert.ToString(value);
    }

    private static async Task<long> ScalarLongAsync(NpgsqlDataSource dataSource, string sql)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        return Convert.ToInt64(await new NpgsqlCommand(sql, connection).ExecuteScalarAsync());
    }

    private static async Task CreateDatabaseAsync(NpgsqlDataSource dataSource, string databaseName)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        string quoted = new NpgsqlCommandBuilder().QuoteIdentifier(databaseName);
        await new NpgsqlCommand($"CREATE DATABASE {quoted}", connection).ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(NpgsqlDataSource dataSource, string databaseName)
    {
        ValidateDatabaseName(databaseName);
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        string quoted = new NpgsqlCommandBuilder().QuoteIdentifier(databaseName);
        await new NpgsqlCommand($"DROP DATABASE IF EXISTS {quoted} WITH (FORCE)", connection)
            .ExecuteNonQueryAsync();
    }

    private static void ValidateDatabaseName(string databaseName)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(
                databaseName,
                "^nexaconnect_pos_clean_it_[a-f0-9]{32}$"))
        {
            throw new InvalidOperationException(
                "Refusing to manage a database outside the POS acceptance naming boundary.");
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NexaConnect.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the NexaConnect repository root.");
    }
}

[CollectionDefinition("POS migration runner acceptance", DisableParallelization = true)]
public sealed class PosMigrationRunnerAcceptanceCollection;

public sealed class PosMigrationAcceptanceFactAttribute : FactAttribute
{
    public PosMigrationAcceptanceFactAttribute()
    {
        string connection = Environment.GetEnvironmentVariable(
            "NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB") ?? string.Empty;
        string? environment = Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        bool validConnection = false;
        try
        {
            _ = new NpgsqlConnectionStringBuilder(connection);
            validConnection = !string.IsNullOrWhiteSpace(connection);
        }
        catch (ArgumentException)
        {
        }

        if (Environment.GetEnvironmentVariable("NEXACONNECT_POS_CLEAN_INSTALL_ACCEPTANCE") != "1"
            || environment is not ("Development" or "Test" or "Testing")
            || !validConnection)
        {
            Skip = "POS clean-install acceptance requires its opt-in flag, administrator connection, and a safe environment.";
        }
    }
}
