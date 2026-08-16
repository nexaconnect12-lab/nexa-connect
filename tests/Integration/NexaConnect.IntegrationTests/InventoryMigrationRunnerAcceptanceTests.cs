extern alias INVENTORY;
extern alias MIGRATIONS;

using InventoryMutationContext = INVENTORY::NexaConnect.Services.Inventory.Application.Reservations.InventoryMutationContext;
using InventoryRepository = INVENTORY::NexaConnect.Services.Inventory.Infrastructure.PostgresInventoryReservations;
using ReservationLine = INVENTORY::NexaConnect.Services.Inventory.Application.Reservations.ReservationLine;
using ReserveStock = INVENTORY::NexaConnect.Services.Inventory.Application.Reservations.ReserveStock;
using MigrationApplication = MIGRATIONS::MigrationApplication;
using Npgsql;

namespace NexaConnect.IntegrationTests;

[Collection("Inventory migration runner acceptance")]
public sealed class InventoryMigrationRunnerAcceptanceTests
{
    [Fact]
    public async Task Empty_database_upgrades_to_5_downgrades_to_4_and_re_upgrades_to_5()
    {
        if(!Configured(out string adminConnectionString))return;
        string databaseName=$"nexaconnect_inventory_clean_it_{Guid.NewGuid():N}";
        ValidateDatabaseName(databaseName);
        var adminBuilder=new NpgsqlConnectionStringBuilder(adminConnectionString){Database="postgres"};
        await using NpgsqlDataSource adminDataSource=NpgsqlDataSource.Create(adminBuilder.ConnectionString);
        await CreateDatabaseAsync(adminDataSource,databaseName);
        string? previousInventoryConnection=Environment.GetEnvironmentVariable("NEXACONNECT_INVENTORY_DB");
        try
        {
            var inventoryBuilder=new NpgsqlConnectionStringBuilder(adminConnectionString){Database=databaseName};
            Environment.SetEnvironmentVariable("NEXACONNECT_INVENTORY_DB",inventoryBuilder.ConnectionString);
            string scriptsRoot=Path.Combine(FindRepositoryRoot(),"src","Tools","NexaConnect.DataMigration","Scripts");

            Assert.Equal(0,await RunMigrationAsync(scriptsRoot,5));
            await using NpgsqlDataSource inventoryDataSource=NpgsqlDataSource.Create(inventoryBuilder.ConnectionString);
            await AssertHistoryAsync(inventoryDataSource,[1,2,3,4,5]);
            await AssertSchema5Async(inventoryDataSource);
            await ExerciseRepositoryAsync(inventoryDataSource);

            Assert.Equal(0,await RunMigrationAsync(scriptsRoot,4,destructive:true));
            await AssertHistoryAsync(inventoryDataSource,[1,2,3,4]);
            await AssertSchema4Async(inventoryDataSource);

            Assert.Equal(0,await RunMigrationAsync(scriptsRoot,5));
            await AssertHistoryAsync(inventoryDataSource,[1,2,3,4,5]);
            await AssertSchema5Async(inventoryDataSource);
            await ExerciseRepositoryAsync(inventoryDataSource);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NEXACONNECT_INVENTORY_DB",previousInventoryConnection);
            await DropDatabaseAsync(adminDataSource,databaseName);
        }
    }

    private static Task<int> RunMigrationAsync(string scriptsRoot,int target,bool destructive=false)
    {
        var arguments=new List<string>{"--service","Inventory","--scripts-root",scriptsRoot,"--target",target.ToString(),"--application-version","0.6.0","--confirm"};
        if(destructive)arguments.AddRange(["--allow-destructive","--backup-verified"]);
        return MigrationApplication.RunAsync(arguments.ToArray());
    }

    private static async Task AssertHistoryAsync(NpgsqlDataSource dataSource,int[] expectedVersions)
    {
        await using NpgsqlConnection connection=await dataSource.OpenConnectionAsync();
        await using var command=new NpgsqlCommand("SELECT version,metadata_checksum_sha256,up_checksum_sha256,down_checksum_sha256 FROM public.nexaconnect_schema_migrations ORDER BY version",connection);
        await using NpgsqlDataReader reader=await command.ExecuteReaderAsync();
        var actual=new List<int>();
        while(await reader.ReadAsync())
        {
            actual.Add(reader.GetInt32(0));
            Assert.All([reader.GetString(1),reader.GetString(2),reader.GetString(3)],checksum=>Assert.Matches("^[0-9A-F]{64}$",checksum));
        }
        Assert.Equal(expectedVersions,actual);
    }

    private static async Task AssertSchema5Async(NpgsqlDataSource dataSource)
    {
        await using NpgsqlConnection connection=await dataSource.OpenConnectionAsync();
        foreach(string table in new[]{"warehouses","stock_items","stock_movements","stock_reservations","replenishment_requests","processed_messages","outbox_messages","inventory_stock","inventory_reservation_lines","inbox_messages","inventory_audit_records"})
            Assert.Equal(table,await ScalarTextAsync(connection,$"SELECT to_regclass('public.{table}')::text"));
        Assert.Equal("reservation_id",await ScalarTextAsync(connection,"SELECT column_name FROM information_schema.columns WHERE table_schema='public' AND table_name='inventory_reservation_lines' AND column_name='reservation_id' AND is_nullable='NO'"));
        Assert.Equal("organization_id",await ScalarTextAsync(connection,"SELECT column_name FROM information_schema.columns WHERE table_schema='public' AND table_name='inventory_stock' AND column_name='organization_id'"));
        Assert.Equal("organization_id",await ScalarTextAsync(connection,"SELECT column_name FROM information_schema.columns WHERE table_schema='public' AND table_name='inventory_reservation_lines' AND column_name='organization_id'"));
        Assert.Equal("ix_inventory_reservations_organization_order_active",await ScalarTextAsync(connection,"SELECT to_regclass('public.ix_inventory_reservations_organization_order_active')::text"));
        Assert.Equal("ix_inventory_reservation_id",await ScalarTextAsync(connection,"SELECT to_regclass('public.ix_inventory_reservation_id')::text"));
        Assert.Equal(1L,await ScalarLongAsync(connection,"SELECT count(*) FROM pg_trigger WHERE tgname='tr_inventory_audit_records_append_only' AND NOT tgisinternal"));
        Assert.Equal(1L,await ScalarLongAsync(connection,"SELECT count(*) FROM pg_proc WHERE proname='prevent_inventory_audit_mutation' AND pg_function_is_visible(oid)"));
        Assert.Contains("organization_id, branch_id, product_id",await ScalarTextAsync(connection,"SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname='pk_inventory_stock'")??string.Empty,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("organization_id, order_id, product_id",await ScalarTextAsync(connection,"SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname='pk_inventory_reservation_lines'")??string.Empty,StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AssertSchema4Async(NpgsqlDataSource dataSource)
    {
        await using NpgsqlConnection connection=await dataSource.OpenConnectionAsync();
        Assert.Equal("outbox_messages",await ScalarTextAsync(connection,"SELECT to_regclass('public.outbox_messages')::text"));
        Assert.Equal("inbox_messages",await ScalarTextAsync(connection,"SELECT to_regclass('public.inbox_messages')::text"));
        Assert.Equal("inventory_stock",await ScalarTextAsync(connection,"SELECT to_regclass('public.inventory_stock')::text"));
        Assert.Equal("inventory_reservation_lines",await ScalarTextAsync(connection,"SELECT to_regclass('public.inventory_reservation_lines')::text"));
        Assert.Null(await ScalarTextAsync(connection,"SELECT to_regclass('public.inventory_audit_records')::text"));
        Assert.Null(await ScalarTextAsync(connection,"SELECT column_name FROM information_schema.columns WHERE table_schema='public' AND table_name='inventory_reservation_lines' AND column_name='reservation_id'"));
        Assert.Null(await ScalarTextAsync(connection,"SELECT to_regclass('public.ix_inventory_reservation_id')::text"));
        Assert.Equal(0L,await ScalarLongAsync(connection,"SELECT count(*) FROM pg_trigger WHERE tgname='tr_inventory_audit_records_append_only' AND NOT tgisinternal"));
        Assert.Equal(0L,await ScalarLongAsync(connection,"SELECT count(*) FROM pg_proc WHERE proname='prevent_inventory_audit_mutation' AND pg_function_is_visible(oid)"));
        Assert.Equal(2L,await ScalarLongAsync(connection,"SELECT count(*) FROM outbox_messages"));
        Assert.Equal(1L,await ScalarLongAsync(connection,"SELECT count(*) FROM inventory_stock"));
        Assert.Equal(1L,await ScalarLongAsync(connection,"SELECT count(*) FROM inventory_reservation_lines"));
    }

    private static async Task ExerciseRepositoryAsync(NpgsqlDataSource dataSource)
    {
        Guid organizationId=Guid.NewGuid(), branchId=Guid.NewGuid(), productId=Guid.NewGuid(), orderId=Guid.NewGuid(), correlationId=Guid.NewGuid();
        var repository=new InventoryRepository(dataSource);
        var context=new InventoryMutationContext("migration-acceptance",correlationId);
        repository.SetStock(organizationId,branchId,productId,10,context);
        var reservation=repository.Reserve(organizationId,new ReserveStock(orderId,branchId,[new ReservationLine(productId,3)]),context);
        await using NpgsqlConnection connection=await dataSource.OpenConnectionAsync();
        Assert.Equal(7m,await ScalarDecimalAsync(connection,"SELECT available_quantity FROM inventory_stock WHERE organization_id=$1 AND branch_id=$2 AND product_id=$3",organizationId,branchId,productId));
        Assert.Equal(reservation.ReservationId,await ScalarGuidAsync(connection,"SELECT reservation_id FROM inventory_reservation_lines WHERE organization_id=$1 AND order_id=$2",organizationId,orderId));
        Assert.Equal(2L,await ScalarLongAsync(connection,"SELECT count(*) FROM inventory_audit_records WHERE organization_id=$1 AND action IN ('inventory.stock.set','inventory.reservation.created')",organizationId));
        Assert.Equal(2L,await ScalarLongAsync(connection,"SELECT count(*) FROM outbox_messages WHERE correlation_id=$1 AND event_type IN ('inventory.stock-set.v1','inventory.reservation-created.v1')",correlationId.ToString("D")));
    }

    private static async Task<string?> ScalarTextAsync(NpgsqlConnection connection,string sql){object? value=await new NpgsqlCommand(sql,connection).ExecuteScalarAsync();return value is null or DBNull?null:Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture);}
    private static async Task<long> ScalarLongAsync(NpgsqlConnection connection,string sql,params object[] values){await using var command=new NpgsqlCommand(sql,connection);for(int i=0;i<values.Length;i++)command.Parameters.AddWithValue(values[i]);return Convert.ToInt64(await command.ExecuteScalarAsync(),System.Globalization.CultureInfo.InvariantCulture);}
    private static async Task<decimal> ScalarDecimalAsync(NpgsqlConnection connection,string sql,params object[] values){await using var command=new NpgsqlCommand(sql,connection);for(int i=0;i<values.Length;i++)command.Parameters.AddWithValue(values[i]);return Convert.ToDecimal(await command.ExecuteScalarAsync(),System.Globalization.CultureInfo.InvariantCulture);}
    private static async Task<Guid> ScalarGuidAsync(NpgsqlConnection connection,string sql,params object[] values){await using var command=new NpgsqlCommand(sql,connection);for(int i=0;i<values.Length;i++)command.Parameters.AddWithValue(values[i]);return(Guid)(await command.ExecuteScalarAsync()??Guid.Empty);}

    private static async Task CreateDatabaseAsync(NpgsqlDataSource adminDataSource,string databaseName){await using NpgsqlConnection connection=await adminDataSource.OpenConnectionAsync();string quoted=new NpgsqlCommandBuilder().QuoteIdentifier(databaseName);await new NpgsqlCommand($"CREATE DATABASE {quoted}",connection).ExecuteNonQueryAsync();}
    private static async Task DropDatabaseAsync(NpgsqlDataSource adminDataSource,string databaseName){ValidateDatabaseName(databaseName);await using NpgsqlConnection connection=await adminDataSource.OpenConnectionAsync();string quoted=new NpgsqlCommandBuilder().QuoteIdentifier(databaseName);await new NpgsqlCommand($"DROP DATABASE IF EXISTS {quoted} WITH (FORCE)",connection).ExecuteNonQueryAsync();}
    private static void ValidateDatabaseName(string databaseName){if(!System.Text.RegularExpressions.Regex.IsMatch(databaseName,"^nexaconnect_inventory_clean_it_[a-f0-9]{32}$"))throw new InvalidOperationException("Refusing to manage a database outside the Inventory acceptance naming boundary.");}

    private static bool Configured(out string adminConnectionString)
    {
        adminConnectionString=Environment.GetEnvironmentVariable("NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB")??string.Empty;
        string? environment=Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT")??Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")??Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        bool validConnection=false;try{_=new NpgsqlConnectionStringBuilder(adminConnectionString);validConnection=!string.IsNullOrWhiteSpace(adminConnectionString);}catch(ArgumentException){}
        if(Environment.GetEnvironmentVariable("NEXACONNECT_INVENTORY_CLEAN_INSTALL_ACCEPTANCE")=="1"&&environment is "Development" or "Test" or "Testing"&&validConnection)return true;
        Console.WriteLine("Inventory clean-install acceptance requires NEXACONNECT_INVENTORY_CLEAN_INSTALL_ACCEPTANCE=1, NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB, and a Development/Test/Testing environment.");return false;
    }

    private static string FindRepositoryRoot(){DirectoryInfo? directory=new(AppContext.BaseDirectory);while(directory is not null&&!File.Exists(Path.Combine(directory.FullName,"NexaConnect.sln")))directory=directory.Parent;return directory?.FullName??throw new DirectoryNotFoundException("Could not locate the NexaConnect repository root.");}
}

[CollectionDefinition("Inventory migration runner acceptance",DisableParallelization=true)]
public sealed class InventoryMigrationRunnerAcceptanceCollection;
