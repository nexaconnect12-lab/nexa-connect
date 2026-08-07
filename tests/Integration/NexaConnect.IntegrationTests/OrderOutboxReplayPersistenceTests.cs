using NexaConnect.Infrastructure.Messaging;
using Npgsql;

namespace NexaConnect.IntegrationTests;

public sealed class OrderOutboxReplayPersistenceTests : IAsyncLifetime
{
    private readonly string? _configuredConnectionString =
        Environment.GetEnvironmentVariable("NEXACONNECT_ORDER_INTEGRATION_DB");
    private NpgsqlDataSource? _dataSource;
    private string? _schema;

    [Fact]
    public async Task Failed_event_is_claimed_again_and_marked_published()
    {
        if (!DatabaseConfigured()) return;

        Guid messageId = Guid.NewGuid();
        var message = new OutboxMessage(
            messageId, "OrderSubmittedV1", 1, "order", Guid.NewGuid(),
            "{\"orderId\":\"00000000-0000-0000-0000-000000000001\"}",
            Guid.NewGuid().ToString(), DateTimeOffset.UtcNow.AddSeconds(-1));
        var store = new PostgresOutboxStore(_dataSource!);

        await store.EnqueueAsync(message, CancellationToken.None);
        IReadOnlyList<OutboxMessage> firstClaim = await store.ClaimBatchAsync(10, CancellationToken.None);
        Assert.Single(firstClaim);
        Assert.Equal(messageId, firstClaim[0].Id);

        await store.MarkFailedAsync(messageId, "test-transport-failure", CancellationToken.None);
        await MakeImmediatelyRetryableAsync(messageId);

        IReadOnlyList<OutboxMessage> replayClaim = await store.ClaimBatchAsync(10, CancellationToken.None);
        Assert.Single(replayClaim);
        Assert.Equal(messageId, replayClaim[0].Id);
        await store.MarkPublishedAsync(messageId, CancellationToken.None);

        Assert.Empty(await store.ClaimBatchAsync(10, CancellationToken.None));
    }

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(_configuredConnectionString) || !IsSafeEnvironment()) return;

        _schema = $"order_outbox_it_{Guid.NewGuid():N}";
        var builder = new NpgsqlConnectionStringBuilder(_configuredConnectionString) { SearchPath = _schema };
        _dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();
        await using (var createSchema = new NpgsqlCommand($"CREATE SCHEMA \"{_schema}\";", connection))
        {
            await createSchema.ExecuteNonQueryAsync();
        }

        await using var schema = new NpgsqlCommand(SchemaSql, connection);
        await schema.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is null || _schema is null) return;

        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();
        await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE;", connection);
        await drop.ExecuteNonQueryAsync();
        await _dataSource.DisposeAsync();
    }

    private async Task MakeImmediatelyRetryableAsync(Guid messageId)
    {
        await using NpgsqlConnection connection = await _dataSource!.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE outbox_messages SET next_attempt_at_utc = now() WHERE id = $1;", connection);
        command.Parameters.AddWithValue(messageId);
        await command.ExecuteNonQueryAsync();
    }

    private bool DatabaseConfigured()
    {
        if (_dataSource is not null && IsSafeEnvironment()) return true;
        Console.WriteLine(
            "Order outbox PostgreSQL tests require NEXACONNECT_ORDER_INTEGRATION_DB and a Development/Test/Testing environment.");
        return false;
    }

    private static bool IsSafeEnvironment()
    {
        string? environment = Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        return environment is "Development" or "Test" or "Testing";
    }

    private const string SchemaSql = """
        CREATE TABLE outbox_messages
        (
            id uuid PRIMARY KEY, event_type text NOT NULL, contract_version integer NOT NULL,
            aggregate_type text NOT NULL, aggregate_id uuid NOT NULL, payload jsonb NOT NULL,
            correlation_id text NULL, occurred_at_utc timestamptz NOT NULL,
            published_at_utc timestamptz NULL, retry_count integer NOT NULL DEFAULT 0,
            next_attempt_at_utc timestamptz NULL, last_error_category text NULL
        );
        """;
}
