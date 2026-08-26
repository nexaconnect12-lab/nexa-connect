extern alias MIGRATIONS;

using MigrationApplication = MIGRATIONS::MigrationApplication;
using Npgsql;

namespace NexaConnect.IntegrationTests;

[Collection("Order migration runner acceptance")]
public sealed class OrderMigrationRunnerAcceptanceTests
{
    [OrderMigrationAcceptanceFact]
    public async Task Empty_database_runs_0_to_2_to_1_to_2_and_guards_pending_financial_state()
    {
        string adminConnectionString=Environment.GetEnvironmentVariable("NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB")!;
        string databaseName=$"nexaconnect_order_clean_it_{Guid.NewGuid():N}";ValidateDatabaseName(databaseName);
        var adminBuilder=new NpgsqlConnectionStringBuilder(adminConnectionString){Database="postgres"};
        await using NpgsqlDataSource admin=NpgsqlDataSource.Create(adminBuilder.ConnectionString);
        await CreateDatabaseAsync(admin,databaseName);
        string? previous=Environment.GetEnvironmentVariable("NEXACONNECT_ORDER_DB");
        try
        {
            var databaseBuilder=new NpgsqlConnectionStringBuilder(adminConnectionString){Database=databaseName};
            Environment.SetEnvironmentVariable("NEXACONNECT_ORDER_DB",databaseBuilder.ConnectionString);
            string scriptsRoot=Path.Combine(FindRepositoryRoot(),"src","Tools","NexaConnect.DataMigration","Scripts");
            Assert.Equal(0,await RunAsync(scriptsRoot,2));
            await using NpgsqlDataSource dataSource=NpgsqlDataSource.Create(databaseBuilder.ConnectionString);
            await AssertVersion2Async(dataSource);
            Assert.Equal(0,await RunAsync(scriptsRoot,1,true));
            await AssertVersion1Async(dataSource);
            Assert.Equal(0,await RunAsync(scriptsRoot,2));
            await AssertVersion2Async(dataSource);
            await SeedPendingOrderAsync(dataSource);
            Assert.NotEqual(0,await RunAsync(scriptsRoot,1,true));
            await AssertVersion2Async(dataSource);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NEXACONNECT_ORDER_DB",previous);
            await DropDatabaseAsync(admin,databaseName);
        }
    }

    private static Task<int> RunAsync(string root,int target,bool destructive=false)
    {
        var args=new List<string>{"--service","Order","--scripts-root",root,"--target",target.ToString(),"--application-version","0.10.0","--confirm"};
        if(destructive)args.AddRange(["--allow-destructive","--backup-verified"]);
        return MigrationApplication.RunAsync(args.ToArray());
    }

    private static async Task AssertVersion2Async(NpgsqlDataSource source)
    {
        await using NpgsqlConnection connection=await source.OpenConnectionAsync();
        Assert.Equal("inbox_messages",Convert.ToString(await new NpgsqlCommand("SELECT to_regclass('public.inbox_messages')::text",connection).ExecuteScalarAsync()));
        Assert.Equal(2L,Convert.ToInt64(await new NpgsqlCommand("SELECT max(version) FROM nexaconnect_schema_migrations",connection).ExecuteScalarAsync()));
        string definition=Convert.ToString(await new NpgsqlCommand("SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname='ck_orders_status'",connection).ExecuteScalarAsync())!;
        Assert.Contains("payment_pending",definition,StringComparison.Ordinal);
    }

    private static async Task AssertVersion1Async(NpgsqlDataSource source)
    {
        await using NpgsqlConnection connection=await source.OpenConnectionAsync();
        object? inbox=await new NpgsqlCommand("SELECT to_regclass('public.inbox_messages')::text",connection).ExecuteScalarAsync();
        Assert.True(inbox is null or DBNull || string.IsNullOrEmpty(Convert.ToString(inbox)));
        Assert.Equal(1L,Convert.ToInt64(await new NpgsqlCommand("SELECT max(version) FROM nexaconnect_schema_migrations",connection).ExecuteScalarAsync()));
    }

    private static async Task SeedPendingOrderAsync(NpgsqlDataSource source)
    {
        await using NpgsqlConnection connection=await source.OpenConnectionAsync();
        await using var command=new NpgsqlCommand("INSERT INTO orders(id,restaurant_id,branch_id,order_number,currency,channel,service_type,subtotal_amount,total_amount,status,created_at_utc,created_by,updated_at_utc,updated_by) VALUES($1,$2,$3,$4,'USD','pos','takeaway',10,10,'payment_pending',now(),'acceptance',now(),'acceptance')",connection);
        command.Parameters.AddWithValue(Guid.NewGuid());command.Parameters.AddWithValue(Guid.NewGuid());command.Parameters.AddWithValue(Guid.NewGuid());command.Parameters.AddWithValue($"ACC-{Guid.NewGuid():N}"[..20]);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateDatabaseAsync(NpgsqlDataSource source,string name){await using NpgsqlConnection c=await source.OpenConnectionAsync();string quoted=new NpgsqlCommandBuilder().QuoteIdentifier(name);await new NpgsqlCommand($"CREATE DATABASE {quoted}",c).ExecuteNonQueryAsync();}
    private static async Task DropDatabaseAsync(NpgsqlDataSource source,string name){ValidateDatabaseName(name);await using NpgsqlConnection c=await source.OpenConnectionAsync();string quoted=new NpgsqlCommandBuilder().QuoteIdentifier(name);await new NpgsqlCommand($"DROP DATABASE IF EXISTS {quoted} WITH (FORCE)",c).ExecuteNonQueryAsync();}
    private static void ValidateDatabaseName(string name){if(!System.Text.RegularExpressions.Regex.IsMatch(name,"^nexaconnect_order_clean_it_[a-f0-9]{32}$"))throw new InvalidOperationException("Refusing to manage a database outside the Order acceptance boundary.");}
    private static string FindRepositoryRoot(){DirectoryInfo? d=new(AppContext.BaseDirectory);while(d is not null&&!File.Exists(Path.Combine(d.FullName,"NexaConnect.sln")))d=d.Parent;return d?.FullName??throw new DirectoryNotFoundException();}
}

[CollectionDefinition("Order migration runner acceptance",DisableParallelization=true)]
public sealed class OrderMigrationRunnerAcceptanceCollection;

public sealed class OrderMigrationAcceptanceFactAttribute : FactAttribute
{
    public OrderMigrationAcceptanceFactAttribute()
    {
        string? environment=Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT")??Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")??Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        string connection=Environment.GetEnvironmentVariable("NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB")??string.Empty;
        if(Environment.GetEnvironmentVariable("NEXACONNECT_ORDER_CLEAN_INSTALL_ACCEPTANCE")!="1"||environment is not ("Development" or "Test" or "Testing")||string.IsNullOrWhiteSpace(connection))
            Skip="Order clean-install acceptance requires its opt-in flag, administrator connection, and a safe environment.";
    }
}
