extern alias CUSTOMER;

using CustomerCommand = CUSTOMER::NexaConnect.Services.Customer.Application.Customers.CreateCustomer;
using CustomerContext = CUSTOMER::NexaConnect.Services.Customer.Application.Customers.CustomerRequestContext;
using CustomerService = CUSTOMER::NexaConnect.Services.Customer.Application.Customers.CustomerProfileService;
using CustomerTenantAuthorizer = CUSTOMER::NexaConnect.Services.Customer.Application.Tenant.ICustomerTenantAuthorizer;
using CustomerConflict = CUSTOMER::NexaConnect.Services.Customer.Domain.CustomerIdempotencyConflictException;
using CustomerRepository = CUSTOMER::NexaConnect.Services.Customer.Infrastructure.PostgresCustomers;
using Microsoft.Extensions.Options;
using NexaConnect.Infrastructure.Messaging;
using Npgsql;
using RabbitMQ.Client;

namespace NexaConnect.IntegrationTests;

public sealed class CustomerPostgresIntegrationTests : IAsyncLifetime
{
    private readonly string? connectionString = Environment.GetEnvironmentVariable("NEXACONNECT_CUSTOMER_INTEGRATION_DB");
    private NpgsqlDataSource? dataSource;
    private string? schema;

    [CustomerDatabaseFact]
    public async Task Create_audit_and_outbox_are_atomic_idempotent_and_tenant_scoped()
    {
        Guid organizationId = Guid.NewGuid();
        var repository = new CustomerService(new CustomerRepository(dataSource!), new AllowCustomerTenantAuthorizer());
        var context = Context(organizationId, "customer-test", "customer-phase10-001");
        var command = new CustomerCommand(organizationId, "C-100", "Ada Lovelace", "subject-100");

        var created = await repository.CreateAsync(command, context, default);
        var replay = await repository.CreateAsync(command, context, default);

        Assert.Equal(created.Id, replay.Id);
        Guid otherOrganization = Guid.NewGuid();
        Assert.Null(await repository.GetAsync(otherOrganization, created.Id,
            Context(otherOrganization, "customer-test", "customer-phase10-cross-tenant"), default));
        await Assert.ThrowsAsync<CustomerConflict>(() => repository.CreateAsync(
            command with { DisplayName = "Grace Hopper" }, context, default));
        await using NpgsqlConnection connection = await dataSource!.OpenConnectionAsync();
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT count(*) FROM customers WHERE organization_id=$1 AND customer_number='C-100'", organizationId));
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT count(*) FROM customer_audit_records WHERE customer_id=$1", created.Id));
        Assert.Equal(2L, await ScalarAsync(connection,
            "SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1", created.Id));
        await using (var payload = new NpgsqlCommand(
            "SELECT string_agg(payload::text,' ') FROM outbox_messages WHERE aggregate_id=$1", connection))
        {
            payload.Parameters.AddWithValue(created.Id);
            string value = Convert.ToString(await payload.ExecuteScalarAsync())!;
            Assert.DoesNotContain("Ada Lovelace", value, StringComparison.Ordinal);
            Assert.DoesNotContain("subject-100", value, StringComparison.Ordinal);
        }
        Assert.Equal(2L, await ScalarAsync(connection,
            "SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1 AND correlation_id='customer-phase10-001'", created.Id));
        await using var mutate = new NpgsqlCommand("DELETE FROM customer_audit_records WHERE customer_id=$1", connection);
        mutate.Parameters.AddWithValue(created.Id);
        await Assert.ThrowsAsync<PostgresException>(() => mutate.ExecuteNonQueryAsync());
    }

    [CustomerDatabaseFact]
    public async Task Outbox_failure_rolls_back_profile_and_audit()
    {
        Guid organizationId = Guid.NewGuid();
        await using (NpgsqlConnection connection = await dataSource!.OpenConnectionAsync())
            await new NpgsqlCommand("ALTER TABLE outbox_messages RENAME TO unavailable_outbox_messages", connection)
                .ExecuteNonQueryAsync();
        try
        {
            var repository = new CustomerService(new CustomerRepository(dataSource!), new AllowCustomerTenantAuthorizer());
            await Assert.ThrowsAsync<PostgresException>(() => repository.CreateAsync(
                new CustomerCommand(organizationId, "C-ROLLBACK", "Rollback", null),
                Context(organizationId, "customer-test", "customer-phase10-rollback"), default));
        }
        finally
        {
            await using NpgsqlConnection connection = await dataSource!.OpenConnectionAsync();
            await new NpgsqlCommand("ALTER TABLE unavailable_outbox_messages RENAME TO outbox_messages", connection)
                .ExecuteNonQueryAsync();
        }

        await using NpgsqlConnection verify = await dataSource!.OpenConnectionAsync();
        Assert.Equal(0L, await ScalarAsync(verify,
            "SELECT count(*) FROM customers WHERE organization_id=$1 AND customer_number='C-ROLLBACK'", organizationId));
        Assert.Equal(0L, await ScalarAsync(verify,
            "SELECT count(*) FROM customer_audit_records WHERE organization_id=$1", organizationId));
    }

    [CustomerDatabaseFact]
    public async Task Concurrent_matching_retries_publish_once_and_conflicting_retries_fail_deterministically()
    {
        var profiles = new CustomerService(new CustomerRepository(dataSource!), new AllowCustomerTenantAuthorizer());
        Guid matchingOrganization = Guid.NewGuid();
        var context = Context(matchingOrganization, "customer-concurrency", "customer-phase10-concurrency");
        var matching = new CustomerCommand(matchingOrganization, "C-CONCURRENT", "Concurrent", "subject-concurrent");

        var tasks = Enumerable.Range(0, 8)
            .Select(_ => profiles.CreateAsync(matching, context, default))
            .ToArray();
        var results = await Task.WhenAll(tasks);
        Assert.Single(results.Select(result => result.Id).Distinct());

        Guid conflictingOrganization = Guid.NewGuid();
        var conflictingContext = Context(conflictingOrganization, "customer-concurrency", "customer-phase10-conflict");
        var first = CaptureAsync(new CustomerCommand(conflictingOrganization, "C-CONFLICT", "First", null));
        var second = CaptureAsync(new CustomerCommand(conflictingOrganization, "C-CONFLICT", "Second", null));
        var outcomes = await Task.WhenAll(first, second);
        Assert.Single(outcomes, outcome => outcome.Profile is not null);
        Assert.Single(outcomes, outcome => outcome.Error is CustomerConflict);

        await using NpgsqlConnection connection = await dataSource!.OpenConnectionAsync();
        Guid matchingId = results[0].Id;
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT count(*) FROM customer_audit_records WHERE customer_id=$1", matchingId));
        Assert.Equal(2L, await ScalarAsync(connection,
            "SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1", matchingId));
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT count(*) FROM customers WHERE organization_id=$1 AND customer_number='C-CONFLICT'", conflictingOrganization));
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT count(*) FROM customer_audit_records WHERE organization_id=$1", conflictingOrganization));
        Assert.Equal(2L, await ScalarAsync(connection,
            "SELECT count(*) FROM outbox_messages WHERE aggregate_id=(SELECT id FROM customers WHERE organization_id=$1 AND customer_number='C-CONFLICT')",
            conflictingOrganization));
        return;

        async Task<(CUSTOMER::NexaConnect.Services.Customer.Application.Customers.CustomerProfile? Profile, Exception? Error)> CaptureAsync(CustomerCommand command)
        {
            try { return (await profiles.CreateAsync(command, conflictingContext, default), null); }
            catch (Exception exception) { return (null, exception); }
        }
    }

    [CustomerRabbitFact]
    public async Task Broker_recovery_publishes_confirmed_profile_messages()
    {
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            var unavailable = new ConnectionFactory
            {
                Uri = new Uri("amqp://guest:guest@127.0.0.1:1"),
                RequestedConnectionTimeout = TimeSpan.FromSeconds(1)
            };
            await using IConnection ignored = await unavailable.CreateConnectionAsync();
        });
        var repository = new CustomerService(new CustomerRepository(dataSource!), new AllowCustomerTenantAuthorizer());
        Guid profileOrganization = Guid.NewGuid();
        var profile = await repository.CreateAsync(
            new CustomerCommand(profileOrganization, "C-BROKER", "Broker Test", null),
            Context(profileOrganization, "customer-recovery", "customer-phase10-broker"), default);
        string exchange = $"nexaconnect.customer.phase10.{Guid.NewGuid():N}";
        string queue = $"nexaconnect.customer.phase10.{Guid.NewGuid():N}";
        await using IConnection connection = await new ConnectionFactory
        {
            Uri = new Uri(Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_INTEGRATION_URI")!)
        }.CreateConnectionAsync();
        await using IChannel channel = await connection.CreateChannelAsync();
        try
        {
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false);
            await channel.QueueDeclareAsync(queue, durable: false, exclusive: true, autoDelete: true);
            await channel.QueueBindAsync(queue, exchange, "customer.#");
            var transport = new RabbitMqOutboxTransport(connection, Options.Create(new OutboxOptions { Exchange = exchange }));
            var store = new PostgresOutboxStore(dataSource!);
            var pending = (await store.ClaimBatchAsync(20, default)).Where(message => message.AggregateId == profile.Id).ToArray();
            Assert.Equal(2, pending.Length);
            foreach (OutboxMessage message in pending)
            {
                await transport.PublishAsync(message, default);
                await store.MarkPublishedAsync(message.Id, default);
            }

            var deliveries = new List<BasicGetResult>();
            for (int attempt = 0; attempt < 30 && deliveries.Count < 2; attempt++)
            {
                BasicGetResult? delivery = await channel.BasicGetAsync(queue, autoAck: true);
                if (delivery is null) await Task.Delay(100);
                else deliveries.Add(delivery);
            }
            Assert.Equal(["customer.audit.v1", "customer.profile-created.v1"],
                deliveries.Select(delivery => delivery.RoutingKey).Order().ToArray());
            Assert.All(deliveries, delivery => Assert.True(delivery.BasicProperties.Persistent));
        }
        finally
        {
            await channel.ExchangeDeleteAsync(exchange);
        }
    }

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(connectionString) || !SafeEnvironment()) return;
        schema = $"customer_phase10_it_{Guid.NewGuid():N}";
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema };
        dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        await new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection).ExecuteNonQueryAsync();
        await new NpgsqlCommand(SchemaSql, connection).ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (dataSource is null || schema is null) return;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        await new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", connection).ExecuteNonQueryAsync();
        await dataSource.DisposeAsync();
    }

    private static async Task<long> ScalarAsync(NpgsqlConnection connection, string sql, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        for (int index = 0; index < values.Length; index++) command.Parameters.AddWithValue(values[index]);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static CustomerContext Context(Guid organizationId, string actor, string correlation) =>
        new(organizationId, "nexa_connect", "Bearer customer", actor, Guid.NewGuid(), correlation);

    private static bool SafeEnvironment()
    {
        string? environment = Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        return environment is "Development" or "Test" or "Testing";
    }

    private const string SchemaSql = """
        CREATE TABLE customers(id uuid PRIMARY KEY,organization_id uuid NOT NULL,customer_number text NOT NULL,identity_subject_id text NULL,display_name text NOT NULL,status text NOT NULL,created_at_utc timestamptz NOT NULL,created_by text NOT NULL,updated_at_utc timestamptz NOT NULL,updated_by text NOT NULL,concurrency_version bigint NOT NULL DEFAULT 1,UNIQUE(organization_id,customer_number));
        CREATE UNIQUE INDEX uq_customers_organization_identity ON customers(organization_id,identity_subject_id) WHERE identity_subject_id IS NOT NULL;
        CREATE TABLE customer_audit_records(id uuid PRIMARY KEY,organization_id uuid NOT NULL,customer_id uuid NOT NULL REFERENCES customers(id),action text NOT NULL,actor_subject_id text NOT NULL,occurred_at_utc timestamptz NOT NULL);
        CREATE FUNCTION prevent_customer_audit_mutation() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'append-only'; END; $$;
        CREATE TRIGGER tr_customer_audit_append_only BEFORE UPDATE OR DELETE ON customer_audit_records FOR EACH ROW EXECUTE FUNCTION prevent_customer_audit_mutation();
        CREATE TABLE outbox_messages(id uuid PRIMARY KEY,event_type text NOT NULL,contract_version integer NOT NULL,aggregate_type text NOT NULL,aggregate_id uuid NOT NULL,payload jsonb NOT NULL,correlation_id text NULL,causation_id text NULL,occurred_at_utc timestamptz NOT NULL,published_at_utc timestamptz NULL,retry_count integer NOT NULL DEFAULT 0,next_attempt_at_utc timestamptz NULL,last_error_category text NULL);
        """;
}

internal sealed class AllowCustomerTenantAuthorizer : CustomerTenantAuthorizer
{
    public Task<bool> HasOrganizationAccessAsync(Guid organizationId, string permission,
        string authorizationHeader, CancellationToken cancellationToken) => Task.FromResult(true);
}

public class CustomerDatabaseFactAttribute : FactAttribute
{
    public CustomerDatabaseFactAttribute()
    {
        string? environment = Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NEXACONNECT_CUSTOMER_INTEGRATION_DB"))
            || environment is not ("Development" or "Test" or "Testing"))
            Skip = "Customer PostgreSQL acceptance requires its connection and a safe environment.";
    }
}

public sealed class CustomerRabbitFactAttribute : CustomerDatabaseFactAttribute
{
    public CustomerRabbitFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_ACCEPTANCE") != "1"
            || !Uri.TryCreate(Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_INTEGRATION_URI"),
                UriKind.Absolute, out _))
            Skip = "Customer RabbitMQ acceptance requires its opt-in and URI.";
    }
}
