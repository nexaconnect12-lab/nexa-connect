extern alias INVENTORY;

using InventoryMutationContext = INVENTORY::NexaConnect.Services.Inventory.Application.Reservations.InventoryMutationContext;
using InventoryRepository = INVENTORY::NexaConnect.Services.Inventory.Infrastructure.PostgresInventoryReservations;
using ReservationLine = INVENTORY::NexaConnect.Services.Inventory.Application.Reservations.ReservationLine;
using ReserveStock = INVENTORY::NexaConnect.Services.Inventory.Application.Reservations.ReserveStock;
using Npgsql;

namespace NexaConnect.IntegrationTests;

public sealed class InventoryPostgresIntegrationTests : IAsyncLifetime
{
    private readonly string? configuredConnectionString = Environment.GetEnvironmentVariable("NEXACONNECT_INVENTORY_INTEGRATION_DB");
    private NpgsqlDataSource? dataSource;
    private string? schema;

    [Fact]
    public async Task Concurrent_same_order_retries_return_stable_identity_and_decrement_once()
    {
        if (!DatabaseConfigured()) return;
        Guid organizationId=Guid.NewGuid(), branchId=Guid.NewGuid(), productId=Guid.NewGuid(), orderId=Guid.NewGuid();
        var repository=new InventoryRepository(dataSource!);
        repository.SetStock(organizationId,branchId,productId,10,new("inventory-test",Guid.NewGuid()));
        var command=new ReserveStock(orderId,branchId,[new ReservationLine(productId,3)]);

        var results=await Task.WhenAll(Task.Run(()=>repository.Reserve(organizationId,command,new InventoryMutationContext("inventory-test",Guid.NewGuid()))),Task.Run(()=>repository.Reserve(organizationId,command,new InventoryMutationContext("inventory-test",Guid.NewGuid()))));

        Assert.Equal(results[0].ReservationId,results[1].ReservationId);
        await using NpgsqlConnection connection=await dataSource!.OpenConnectionAsync();
        Assert.Equal(7m,await DecimalAsync(connection,"SELECT available_quantity FROM inventory_stock WHERE organization_id=$1 AND branch_id=$2 AND product_id=$3",organizationId,branchId,productId));
        Assert.Equal(1L,await ScalarAsync(connection,"SELECT count(*) FROM inventory_reservation_lines WHERE organization_id=$1 AND order_id=$2",organizationId,orderId));
        Assert.Equal(1L,await ScalarAsync(connection,"SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1 AND event_type='inventory.reservation-created.v1'",orderId));
        Assert.Equal(1L,await ScalarAsync(connection,"SELECT count(*) FROM inventory_audit_records WHERE resource_id=$1 AND action='inventory.reservation.created'",orderId));
    }

    [Fact]
    public async Task Competing_orders_are_atomic_and_tenant_scoped()
    {
        if (!DatabaseConfigured()) return;
        Guid organizationA=Guid.NewGuid(), organizationB=Guid.NewGuid(), branchId=Guid.NewGuid(), productId=Guid.NewGuid();
        var repository=new InventoryRepository(dataSource!);
        repository.SetStock(organizationA,branchId,productId,5,new("inventory-test",Guid.NewGuid()));
        repository.SetStock(organizationB,branchId,productId,9,new("inventory-test",Guid.NewGuid()));
        Task[] attempts=[Task.Run(()=>repository.Reserve(organizationA,new(Guid.NewGuid(),branchId,[new(productId,4)]),new("inventory-test",Guid.NewGuid()))),Task.Run(()=>repository.Reserve(organizationA,new(Guid.NewGuid(),branchId,[new(productId,4)]),new("inventory-test",Guid.NewGuid())))];
        Exception? failure=await Record.ExceptionAsync(()=>Task.WhenAll(attempts));

        Assert.NotNull(failure);
        Assert.Equal(1,attempts.Count(task=>task.IsCompletedSuccessfully));
        await using NpgsqlConnection connection=await dataSource!.OpenConnectionAsync();
        Assert.Equal(1m,await DecimalAsync(connection,"SELECT available_quantity FROM inventory_stock WHERE organization_id=$1 AND branch_id=$2 AND product_id=$3",organizationA,branchId,productId));
        Assert.Equal(9m,await DecimalAsync(connection,"SELECT available_quantity FROM inventory_stock WHERE organization_id=$1 AND branch_id=$2 AND product_id=$3",organizationB,branchId,productId));
        Assert.Equal(1L,await ScalarAsync(connection,"SELECT count(*) FROM inventory_reservation_lines WHERE organization_id=$1",organizationA));
    }

    [Fact]
    public async Task Release_is_idempotent_and_audit_is_append_only()
    {
        if (!DatabaseConfigured()) return;
        Guid organizationId=Guid.NewGuid(), branchId=Guid.NewGuid(), productId=Guid.NewGuid(), orderId=Guid.NewGuid();
        var repository=new InventoryRepository(dataSource!);
        repository.SetStock(organizationId,branchId,productId,8,new("inventory-test",Guid.NewGuid()));
        repository.Reserve(organizationId,new(orderId,branchId,[new(productId,3)]),new("inventory-test",Guid.NewGuid()));
        repository.Release(organizationId,orderId,new("inventory-test",Guid.NewGuid()));
        repository.Release(organizationId,orderId,new("inventory-test",Guid.NewGuid()));

        await using NpgsqlConnection connection=await dataSource!.OpenConnectionAsync();
        Assert.Equal(8m,await DecimalAsync(connection,"SELECT available_quantity FROM inventory_stock WHERE organization_id=$1 AND branch_id=$2 AND product_id=$3",organizationId,branchId,productId));
        Assert.Equal(1L,await ScalarAsync(connection,"SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1 AND event_type='inventory.reservation-released.v1'",orderId));
        await using var mutate=new NpgsqlCommand("DELETE FROM inventory_audit_records WHERE resource_id=$1",connection);mutate.Parameters.AddWithValue(orderId);
        await Assert.ThrowsAsync<PostgresException>(()=>mutate.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task Outbox_failure_rolls_back_stock_and_audit()
    {
        if (!DatabaseConfigured()) return;
        Guid organizationId=Guid.NewGuid(), branchId=Guid.NewGuid(), productId=Guid.NewGuid();
        await using(NpgsqlConnection connection=await dataSource!.OpenConnectionAsync())await new NpgsqlCommand("ALTER TABLE outbox_messages RENAME TO unavailable_outbox_messages",connection).ExecuteNonQueryAsync();
        try
        {
            var repository=new InventoryRepository(dataSource!);
            Assert.Throws<PostgresException>(()=>repository.SetStock(organizationId,branchId,productId,6,new("inventory-test",Guid.NewGuid())));
        }
        finally
        {
            await using NpgsqlConnection connection=await dataSource!.OpenConnectionAsync();await new NpgsqlCommand("ALTER TABLE unavailable_outbox_messages RENAME TO outbox_messages",connection).ExecuteNonQueryAsync();
        }
        await using NpgsqlConnection verify=await dataSource!.OpenConnectionAsync();
        Assert.Equal(0L,await ScalarAsync(verify,"SELECT count(*) FROM inventory_stock WHERE organization_id=$1 AND product_id=$2",organizationId,productId));
        Assert.Equal(0L,await ScalarAsync(verify,"SELECT count(*) FROM inventory_audit_records WHERE organization_id=$1 AND resource_id=$2",organizationId,productId));
    }

    [Fact]
    public async Task Migration_5_downgrades_and_re_upgrades_preserving_outbox()
    {
        if (!DatabaseConfigured()) return;
        string cycleSchema=$"inventory_migration5_it_{Guid.NewGuid():N}";
        var builder=new NpgsqlConnectionStringBuilder(configuredConnectionString!){SearchPath=cycleSchema};
        await using NpgsqlDataSource cycleDataSource=NpgsqlDataSource.Create(builder.ConnectionString);
        await using NpgsqlConnection connection=await cycleDataSource.OpenConnectionAsync();
        await new NpgsqlCommand($"CREATE SCHEMA \"{cycleSchema}\"",connection).ExecuteNonQueryAsync();
        try
        {
            await new NpgsqlCommand("CREATE TABLE inventory_reservation_lines(organization_id uuid NOT NULL,order_id uuid NOT NULL,branch_id uuid NOT NULL,product_id uuid NOT NULL,quantity numeric NOT NULL,released_at_utc timestamptz NULL,PRIMARY KEY(organization_id,order_id,product_id)); CREATE TABLE outbox_messages(id uuid PRIMARY KEY)",connection).ExecuteNonQueryAsync();
            string migration=Path.Combine(FindRepositoryRoot(),"src","Tools","NexaConnect.DataMigration","Scripts","Inventory","0005_product_integration");
            await ExecuteScriptAsync(connection,Path.Combine(migration,"up.sql"));
            Assert.NotNull(await new NpgsqlCommand("SELECT column_name FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='inventory_reservation_lines' AND column_name='reservation_id'",connection).ExecuteScalarAsync());
            await ExecuteScriptAsync(connection,Path.Combine(migration,"down.sql"));
            Assert.NotNull(await new NpgsqlCommand("SELECT to_regclass('outbox_messages')::text",connection).ExecuteScalarAsync());
            Assert.Equal(DBNull.Value,await new NpgsqlCommand("SELECT to_regclass('inventory_audit_records')::text",connection).ExecuteScalarAsync());
            await ExecuteScriptAsync(connection,Path.Combine(migration,"up.sql"));
            Assert.NotNull(await new NpgsqlCommand("SELECT to_regclass('inventory_audit_records')::text",connection).ExecuteScalarAsync());
        }
        finally { await new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{cycleSchema}\" CASCADE",connection).ExecuteNonQueryAsync(); }
    }

    public async Task InitializeAsync()
    {
        if(string.IsNullOrWhiteSpace(configuredConnectionString)||!IsSafeEnvironment())return;
        schema=$"inventory_phase11_it_{Guid.NewGuid():N}";var builder=new NpgsqlConnectionStringBuilder(configuredConnectionString){SearchPath=schema};dataSource=NpgsqlDataSource.Create(builder.ConnectionString);
        await using NpgsqlConnection connection=await dataSource.OpenConnectionAsync();await new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"",connection).ExecuteNonQueryAsync();await new NpgsqlCommand(SchemaSql,connection).ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if(dataSource is null||schema is null)return;await using NpgsqlConnection connection=await dataSource.OpenConnectionAsync();await new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE",connection).ExecuteNonQueryAsync();await dataSource.DisposeAsync();
    }

    private bool DatabaseConfigured(){if(dataSource is not null&&IsSafeEnvironment())return true;Console.WriteLine("Inventory PostgreSQL tests require NEXACONNECT_INVENTORY_INTEGRATION_DB and a Development/Test/Testing environment.");return false;}
    private static bool IsSafeEnvironment(){string? environment=Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT")??Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")??Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");return environment is "Development" or "Test" or "Testing";}
    private static async Task<long> ScalarAsync(NpgsqlConnection connection,string sql,params object[] values){await using var command=new NpgsqlCommand(sql,connection);for(int i=0;i<values.Length;i++)command.Parameters.AddWithValue(values[i]);return(long)(await command.ExecuteScalarAsync()??0L);}
    private static async Task<decimal> DecimalAsync(NpgsqlConnection connection,string sql,params object[] values){await using var command=new NpgsqlCommand(sql,connection);for(int i=0;i<values.Length;i++)command.Parameters.AddWithValue(values[i]);return(decimal)(await command.ExecuteScalarAsync()??0m);}
    private static async Task ExecuteScriptAsync(NpgsqlConnection connection,string path){await using var command=new NpgsqlCommand(await File.ReadAllTextAsync(path),connection);await command.ExecuteNonQueryAsync();}
    private static string FindRepositoryRoot(){DirectoryInfo? directory=new(AppContext.BaseDirectory);while(directory is not null&&!File.Exists(Path.Combine(directory.FullName,"NexaConnect.sln")))directory=directory.Parent;return directory?.FullName??throw new DirectoryNotFoundException("Could not locate repository root.");}

    private const string SchemaSql="""
        CREATE TABLE inventory_stock(organization_id uuid NOT NULL,branch_id uuid NOT NULL,product_id uuid NOT NULL,available_quantity numeric(19,4) NOT NULL CHECK(available_quantity>=0),PRIMARY KEY(organization_id,branch_id,product_id));
        CREATE TABLE inventory_reservation_lines(organization_id uuid NOT NULL,order_id uuid NOT NULL,branch_id uuid NOT NULL,product_id uuid NOT NULL,quantity numeric(19,4) NOT NULL CHECK(quantity>0),released_at_utc timestamptz NULL,reservation_id uuid NOT NULL,PRIMARY KEY(organization_id,order_id,product_id));
        CREATE TABLE inventory_audit_records(id uuid PRIMARY KEY,organization_id uuid NOT NULL,branch_id uuid NOT NULL,resource_id uuid NOT NULL,action text NOT NULL,actor_subject_id text NOT NULL,occurred_at_utc timestamptz NOT NULL);
        CREATE FUNCTION prevent_inventory_audit_mutation() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'inventory_audit_records is append-only'; END; $$;
        CREATE TRIGGER tr_inventory_audit_records_append_only BEFORE UPDATE OR DELETE ON inventory_audit_records FOR EACH ROW EXECUTE FUNCTION prevent_inventory_audit_mutation();
        CREATE TABLE outbox_messages(id uuid PRIMARY KEY,event_type text NOT NULL,contract_version integer NOT NULL,aggregate_type text NOT NULL,aggregate_id uuid NOT NULL,payload jsonb NOT NULL,correlation_id text NULL,causation_id text NULL,occurred_at_utc timestamptz NOT NULL,published_at_utc timestamptz NULL,retry_count integer NOT NULL DEFAULT 0,next_attempt_at_utc timestamptz NULL,last_error_category text NULL);
        """;
}
