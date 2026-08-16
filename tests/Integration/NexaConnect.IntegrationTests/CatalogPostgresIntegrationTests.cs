extern alias CATALOG;

using CatalogCreateMenuItem = CATALOG::NexaConnect.Services.Catalog.Application.Menu.CreateMenuItem;
using CatalogMenuMutationContext = CATALOG::NexaConnect.Services.Catalog.Application.Menu.MenuMutationContext;
using CatalogRepository = CATALOG::NexaConnect.Services.Catalog.Infrastructure.PostgresMenuCatalog;
using NexaConnect.Infrastructure.Messaging;
using Microsoft.Extensions.Options;
using Npgsql;
using RabbitMQ.Client;

namespace NexaConnect.IntegrationTests;

public sealed class CatalogPostgresIntegrationTests : IAsyncLifetime
{
    private readonly string? configuredConnectionString = Environment.GetEnvironmentVariable("NEXACONNECT_CATALOG_INTEGRATION_DB");
    private NpgsqlDataSource? dataSource;
    private string? schema;

    [Fact]
    public async Task Menu_audit_and_outbox_commit_atomically_and_audit_is_append_only()
    {
        if (!DatabaseConfigured()) return;
        Guid organizationId = Guid.NewGuid(); Guid branchId = Guid.NewGuid(); Guid productId = Guid.NewGuid(); Guid correlationId = Guid.NewGuid();
        var repository = new CatalogRepository(dataSource!);

        repository.AddForOrganizationBranch(organizationId, branchId,
            new CatalogCreateMenuItem(productId, "Phase 11 Burger", 12.50m, "usd", "grill"),
            new CatalogMenuMutationContext("phase11-user", correlationId));

        await using NpgsqlConnection connection = await dataSource!.OpenConnectionAsync();
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM catalog_menu_items WHERE organization_id=$1 AND branch_id=$2 AND product_id=$3", organizationId, branchId, productId));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM catalog_audit_records WHERE organization_id=$1 AND branch_id=$2 AND product_id=$3", organizationId, branchId, productId));
        Assert.Equal(2L, await ScalarAsync(connection, "SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1 AND correlation_id=$2", productId, correlationId.ToString("D")));
        await using var mutate = new NpgsqlCommand("UPDATE catalog_audit_records SET action='tampered' WHERE product_id=$1", connection);
        mutate.Parameters.AddWithValue(productId);
        await Assert.ThrowsAsync<PostgresException>(() => mutate.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task Outbox_failure_rolls_back_menu_and_audit_writes()
    {
        if (!DatabaseConfigured()) return;
        Guid organizationId = Guid.NewGuid(); Guid branchId = Guid.NewGuid(); Guid productId = Guid.NewGuid();
        await using (NpgsqlConnection connection = await dataSource!.OpenConnectionAsync())
        await using (var breakOutbox = new NpgsqlCommand("ALTER TABLE outbox_messages RENAME TO unavailable_outbox_messages", connection))
            await breakOutbox.ExecuteNonQueryAsync();
        try
        {
            var repository = new CatalogRepository(dataSource!);
            Assert.Throws<PostgresException>(() => repository.AddForOrganizationBranch(organizationId, branchId,
                new CatalogCreateMenuItem(productId, "Rollback Burger", 9m, "USD", "grill"),
                new CatalogMenuMutationContext("phase11-user", Guid.NewGuid())));
        }
        finally
        {
            await using NpgsqlConnection connection = await dataSource!.OpenConnectionAsync();
            await using var restore = new NpgsqlCommand("ALTER TABLE unavailable_outbox_messages RENAME TO outbox_messages", connection);
            await restore.ExecuteNonQueryAsync();
        }

        await using NpgsqlConnection verify = await dataSource!.OpenConnectionAsync();
        Assert.Equal(0L, await ScalarAsync(verify, "SELECT count(*) FROM catalog_menu_items WHERE product_id=$1", productId));
        Assert.Equal(0L, await ScalarAsync(verify, "SELECT count(*) FROM catalog_audit_records WHERE product_id=$1", productId));
    }

    [Fact]
    public async Task Failed_outbox_claim_is_retryable_and_can_be_marked_published()
    {
        if (!DatabaseConfigured()) return;
        Guid productId = Guid.NewGuid(); Guid messageId = Guid.NewGuid();
        var store = new PostgresOutboxStore(dataSource!);
        await store.EnqueueAsync(new OutboxMessage(messageId, "catalog.menu-item.changed.v1", 1, "catalog-menu-item", productId,
            "{\"productId\":\"00000000-0000-0000-0000-000000000001\"}", Guid.NewGuid().ToString("D"), DateTimeOffset.UtcNow.AddSeconds(-1)), CancellationToken.None);
        Assert.Single(await store.ClaimBatchAsync(10, CancellationToken.None));
        await store.MarkFailedAsync(messageId, "test-broker-failure", CancellationToken.None);
        await using (NpgsqlConnection connection = await dataSource!.OpenConnectionAsync())
        await using (var retry = new NpgsqlCommand("UPDATE outbox_messages SET next_attempt_at_utc=now() WHERE id=$1", connection))
        { retry.Parameters.AddWithValue(messageId); await retry.ExecuteNonQueryAsync(); }
        Assert.Single(await store.ClaimBatchAsync(10, CancellationToken.None));
        await store.MarkPublishedAsync(messageId, CancellationToken.None);
        Assert.Empty(await store.ClaimBatchAsync(10, CancellationToken.None));
    }

    [Fact]
    public async Task Migration_4_downgrades_and_re_upgrades_using_repository_scripts()
    {
        if (!DatabaseConfigured()) return;
        string cycleSchema = $"catalog_migration4_it_{Guid.NewGuid():N}";
        var builder = new NpgsqlConnectionStringBuilder(configuredConnectionString!) { SearchPath = cycleSchema };
        await using NpgsqlDataSource cycleDataSource = NpgsqlDataSource.Create(builder.ConnectionString);
        await using NpgsqlConnection connection = await cycleDataSource.OpenConnectionAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{cycleSchema}\"", connection)) await create.ExecuteNonQueryAsync();
        try
        {
            string root = FindRepositoryRoot();
            await using (var baselineOutbox = new NpgsqlCommand("CREATE TABLE outbox_messages(id uuid PRIMARY KEY,event_type text NOT NULL,contract_version integer NOT NULL,aggregate_type text NOT NULL,aggregate_id uuid NOT NULL,payload jsonb NOT NULL,correlation_id text NULL,causation_id text NULL,occurred_at_utc timestamptz NOT NULL,published_at_utc timestamptz NULL,retry_count integer NOT NULL DEFAULT 0,next_attempt_at_utc timestamptz NULL,last_error_category text NULL)", connection))
                await baselineOutbox.ExecuteNonQueryAsync();
            await ExecuteScriptAsync(connection, Path.Combine(root, "src", "Tools", "NexaConnect.DataMigration", "Scripts", "Catalog", "0002_service_menu_items", "up.sql"));
            await ExecuteScriptAsync(connection, Path.Combine(root, "src", "Tools", "NexaConnect.DataMigration", "Scripts", "Catalog", "0003_tenant_boundaries", "up.sql"));
            string migration4 = Path.Combine(root, "src", "Tools", "NexaConnect.DataMigration", "Scripts", "Catalog", "0004_product_integration");
            await ExecuteScriptAsync(connection, Path.Combine(migration4, "up.sql"));
            Assert.NotNull(await new NpgsqlCommand("SELECT to_regclass('outbox_messages')::text", connection).ExecuteScalarAsync());
            await ExecuteScriptAsync(connection, Path.Combine(migration4, "down.sql"));
            Assert.NotNull(await new NpgsqlCommand("SELECT to_regclass('outbox_messages')::text", connection).ExecuteScalarAsync());
            Assert.Equal(DBNull.Value, await new NpgsqlCommand("SELECT to_regclass('catalog_audit_records')::text", connection).ExecuteScalarAsync());
            await ExecuteScriptAsync(connection, Path.Combine(migration4, "up.sql"));
            Assert.NotNull(await new NpgsqlCommand("SELECT to_regclass('catalog_audit_records')::text", connection).ExecuteScalarAsync());
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{cycleSchema}\" CASCADE", connection); await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Broker_outage_retains_rows_and_recovery_publishes_with_confirmations()
    {
        if (!DatabaseConfigured() || !RabbitMqConfigured(out string rabbitMqUri)) return;
        Guid organizationId = Guid.NewGuid(); Guid branchId = Guid.NewGuid(); Guid productId = Guid.NewGuid(); Guid correlationId = Guid.NewGuid();
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            var unavailable = new ConnectionFactory { Uri = new Uri("amqp://guest:guest@127.0.0.1:1"), RequestedConnectionTimeout = TimeSpan.FromSeconds(1) };
            await using IConnection ignored = await unavailable.CreateConnectionAsync();
        });

        var repository = new CatalogRepository(dataSource!);
        repository.AddForOrganizationBranch(organizationId, branchId,
            new CatalogCreateMenuItem(productId, "Recovery Burger", 14m, "USD", "grill"),
            new CatalogMenuMutationContext("phase11-recovery-user", correlationId));
        await using (NpgsqlConnection verify = await dataSource!.OpenConnectionAsync())
            Assert.Equal(2L, await ScalarAsync(verify, "SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1 AND published_at_utc IS NULL", productId));

        string exchange = $"nexaconnect.catalog.phase11.{Guid.NewGuid():N}";
        string queue = $"nexaconnect.catalog.phase11.{Guid.NewGuid():N}";
        var factory = new ConnectionFactory { Uri = new Uri(rabbitMqUri) };
        await using IConnection connection = await factory.CreateConnectionAsync();
        await using IChannel channel = await connection.CreateChannelAsync();
        try
        {
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false);
            await channel.QueueDeclareAsync(queue, durable: false, exclusive: true, autoDelete: true);
            await channel.QueueBindAsync(queue, exchange, "catalog.#");
            var transport = new RabbitMqOutboxTransport(connection, Options.Create(new OutboxOptions { Exchange = exchange }));
            var store = new PostgresOutboxStore(dataSource!);
            IReadOnlyList<OutboxMessage> pending = await store.ClaimBatchAsync(10, CancellationToken.None);
            Assert.Equal(2, pending.Count(message => message.AggregateId == productId));
            foreach (OutboxMessage message in pending.Where(message => message.AggregateId == productId))
            {
                await transport.PublishAsync(message, CancellationToken.None);
                await store.MarkPublishedAsync(message.Id, CancellationToken.None);
            }

            var deliveries = new List<BasicGetResult>();
            for (int attempt = 0; attempt < 20 && deliveries.Count < 2; attempt++)
            {
                BasicGetResult? delivery = await channel.BasicGetAsync(queue, autoAck: true);
                if (delivery is not null) deliveries.Add(delivery); else await Task.Delay(100);
            }
            Assert.Equal(["catalog.audit.v1", "catalog.menu-item.changed.v1"], deliveries.Select(item => item.RoutingKey).Order().ToArray());
            Assert.All(deliveries, delivery => Assert.True(delivery.BasicProperties.Persistent));
            Assert.All(deliveries, delivery => Assert.Contains(correlationId.ToString("D"), System.Text.Encoding.UTF8.GetString(delivery.Body.Span), StringComparison.OrdinalIgnoreCase));
            await using NpgsqlConnection published = await dataSource!.OpenConnectionAsync();
            Assert.Equal(2L, await ScalarAsync(published, "SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1 AND published_at_utc IS NOT NULL", productId));
        }
        finally
        {
            await channel.ExchangeDeleteAsync(exchange, ifUnused: false);
        }
    }

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(configuredConnectionString) || !IsSafeEnvironment()) return;
        schema = $"catalog_phase11_it_{Guid.NewGuid():N}";
        var builder = new NpgsqlConnectionStringBuilder(configuredConnectionString) { SearchPath = schema };
        dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection)) await create.ExecuteNonQueryAsync();
        await using var setup = new NpgsqlCommand(SchemaSql, connection); await setup.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (dataSource is null || schema is null) return;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", connection); await drop.ExecuteNonQueryAsync();
        await dataSource.DisposeAsync();
    }

    private bool DatabaseConfigured()
    {
        if (dataSource is not null && IsSafeEnvironment()) return true;
        Console.WriteLine("Catalog PostgreSQL tests require NEXACONNECT_CATALOG_INTEGRATION_DB and a Development/Test/Testing environment."); return false;
    }

    private static bool RabbitMqConfigured(out string connectionString)
    {
        connectionString = Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_INTEGRATION_URI") ?? string.Empty;
        if (Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_ACCEPTANCE") == "1" && Uri.TryCreate(connectionString, UriKind.Absolute, out _)) return true;
        Console.WriteLine("Catalog RabbitMQ recovery acceptance requires NEXACONNECT_RABBITMQ_ACCEPTANCE=1 and NEXACONNECT_RABBITMQ_INTEGRATION_URI."); return false;
    }

    private static bool IsSafeEnvironment()
    {
        string? environment = Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        return environment is "Development" or "Test" or "Testing";
    }

    private static async Task<long> ScalarAsync(NpgsqlConnection connection, string sql, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        for (int index = 0; index < values.Length; index++) command.Parameters.AddWithValue(values[index]);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static async Task ExecuteScriptAsync(NpgsqlConnection connection, string path)
    {
        await using var command = new NpgsqlCommand(await File.ReadAllTextAsync(path), connection);
        await command.ExecuteNonQueryAsync();
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NexaConnect.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the NexaConnect repository root.");
    }

    private const string SchemaSql = """
        CREATE TABLE catalog_menu_items(organization_id uuid NOT NULL,branch_id uuid NOT NULL,product_id uuid NOT NULL,name text NOT NULL,unit_price numeric(19,4) NOT NULL,currency char(3) NOT NULL,preparation_station text NOT NULL,available boolean NOT NULL,PRIMARY KEY(organization_id,branch_id,product_id));
        CREATE TABLE catalog_audit_records(id uuid PRIMARY KEY,organization_id uuid NOT NULL,branch_id uuid NOT NULL,product_id uuid NOT NULL,action text NOT NULL,actor_subject_id text NOT NULL,occurred_at_utc timestamptz NOT NULL);
        CREATE FUNCTION prevent_catalog_audit_mutation() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'catalog_audit_records is append-only'; END; $$;
        CREATE TRIGGER tr_catalog_audit_records_append_only BEFORE UPDATE OR DELETE ON catalog_audit_records FOR EACH ROW EXECUTE FUNCTION prevent_catalog_audit_mutation();
        CREATE TABLE outbox_messages(id uuid PRIMARY KEY,event_type text NOT NULL,contract_version integer NOT NULL,aggregate_type text NOT NULL,aggregate_id uuid NOT NULL,payload jsonb NOT NULL,correlation_id text NULL,causation_id text NULL,occurred_at_utc timestamptz NOT NULL,published_at_utc timestamptz NULL,retry_count integer NOT NULL DEFAULT 0,next_attempt_at_utc timestamptz NULL,last_error_category text NULL);
        """;
}
