extern alias CUSTOMER;
extern alias MIGRATIONS;

using CustomerCommand = CUSTOMER::NexaConnect.Services.Customer.Application.Customers.CreateCustomer;
using CustomerContext = CUSTOMER::NexaConnect.Services.Customer.Application.Customers.CustomerRequestContext;
using CustomerService = CUSTOMER::NexaConnect.Services.Customer.Application.Customers.CustomerProfileService;
using CustomerRepository = CUSTOMER::NexaConnect.Services.Customer.Infrastructure.PostgresCustomers;
using MigrationApplication = MIGRATIONS::MigrationApplication;
using Npgsql;

namespace NexaConnect.IntegrationTests;

[Collection("Customer migration runner acceptance")]
public sealed class CustomerMigrationRunnerAcceptanceTests
{
    [CustomerMigrationFact]
    public async Task Empty_database_runs_0_to_2_to_1_to_2()
    {
        string admin = Environment.GetEnvironmentVariable("NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB")!;
        string database = $"nexaconnect_customer_clean_it_{Guid.NewGuid():N}";
        Validate(database);
        var adminBuilder = new NpgsqlConnectionStringBuilder(admin) { Database = "postgres" };
        await using var adminSource = NpgsqlDataSource.Create(adminBuilder.ConnectionString);
        await CreateAsync(adminSource, database);
        string? previous = Environment.GetEnvironmentVariable("NEXACONNECT_CUSTOMER_DB");
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(admin) { Database = database };
            Environment.SetEnvironmentVariable("NEXACONNECT_CUSTOMER_DB", builder.ConnectionString);
            string root = Path.Combine(RepositoryRoot(), "src", "Tools", "NexaConnect.DataMigration", "Scripts");
            Assert.Equal(0, await RunAsync(root, 2));
            await using var dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
            var repository = new CustomerService(new CustomerRepository(dataSource), new AllowCustomerTenantAuthorizer());
            Guid firstOrganization = Guid.NewGuid();
            var first = await repository.CreateAsync(new CustomerCommand(firstOrganization, "C-MIGRATION-1", "First", null),
                Context(firstOrganization, "customer-migration-1"), default);
            Assert.Equal(1L, await CountAsync(dataSource, "SELECT count(*) FROM customer_audit_records WHERE customer_id=$1", first.Id));
            Assert.Equal(2L, await CountAsync(dataSource, "SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1", first.Id));

            Assert.Equal(0, await RunAsync(root, 1, destructive: true));
            Assert.Null(await RelationAsync(dataSource, "customer_audit_records"));
            Assert.Equal("outbox_messages", await RelationAsync(dataSource, "outbox_messages"));
            Assert.Equal(1L, await CountAsync(dataSource, "SELECT count(*) FROM customers WHERE id=$1", first.Id));
            Assert.Equal(2L, await CountAsync(dataSource, "SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1", first.Id));

            Assert.Equal(0, await RunAsync(root, 2));
            Guid secondOrganization = Guid.NewGuid();
            var second = await repository.CreateAsync(new CustomerCommand(secondOrganization, "C-MIGRATION-2", "Second", null),
                Context(secondOrganization, "customer-migration-2"), default);
            Assert.Equal(1L, await CountAsync(dataSource, "SELECT count(*) FROM customer_audit_records WHERE customer_id=$1", second.Id));
        }
        finally
        {
            Environment.SetEnvironmentVariable("NEXACONNECT_CUSTOMER_DB", previous);
            await DropAsync(adminSource, database);
        }
    }

    private static Task<int> RunAsync(string root, int target, bool destructive = false)
    {
        var args = new List<string> { "--service", "Customer", "--scripts-root", root, "--target", target.ToString(), "--application-version", "0.9.0", "--confirm" };
        if (destructive) args.AddRange(["--allow-destructive", "--backup-verified"]);
        return MigrationApplication.RunAsync(args.ToArray());
    }

    private static CustomerContext Context(Guid organizationId, string correlation) =>
        new(organizationId, "nexa_connect", "Bearer customer", "migration-test", Guid.NewGuid(), correlation);

    private static async Task<long> CountAsync(NpgsqlDataSource source, string sql, Guid value)
    {
        await using NpgsqlConnection connection = await source.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(value);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<string?> RelationAsync(NpgsqlDataSource source, string relation)
    {
        await using NpgsqlConnection connection = await source.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("SELECT to_regclass($1)::text", connection);
        command.Parameters.AddWithValue($"public.{relation}");
        object? value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : Convert.ToString(value);
    }

    private static async Task CreateAsync(NpgsqlDataSource source, string database)
    {
        await using NpgsqlConnection connection = await source.OpenConnectionAsync();
        await new NpgsqlCommand($"CREATE DATABASE {new NpgsqlCommandBuilder().QuoteIdentifier(database)}", connection)
            .ExecuteNonQueryAsync();
    }

    private static async Task DropAsync(NpgsqlDataSource source, string database)
    {
        Validate(database);
        await using NpgsqlConnection connection = await source.OpenConnectionAsync();
        await new NpgsqlCommand($"DROP DATABASE IF EXISTS {new NpgsqlCommandBuilder().QuoteIdentifier(database)} WITH (FORCE)", connection)
            .ExecuteNonQueryAsync();
    }

    private static void Validate(string database)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(database, "^nexaconnect_customer_clean_it_[a-f0-9]{32}$"))
            throw new InvalidOperationException("Unsafe Customer acceptance database name.");
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NexaConnect.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}

[CollectionDefinition("Customer migration runner acceptance", DisableParallelization = true)]
public sealed class CustomerMigrationRunnerCollection;

public sealed class CustomerMigrationFactAttribute : FactAttribute
{
    public CustomerMigrationFactAttribute()
    {
        string? environment = Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (Environment.GetEnvironmentVariable("NEXACONNECT_CUSTOMER_CLEAN_INSTALL_ACCEPTANCE") != "1"
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB"))
            || environment is not ("Development" or "Test" or "Testing"))
            Skip = "Customer migration acceptance requires opt-in, administrator connection, and a safe environment.";
    }
}
