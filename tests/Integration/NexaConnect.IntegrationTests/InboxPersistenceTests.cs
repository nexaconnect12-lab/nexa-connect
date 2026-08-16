using NexaConnect.Infrastructure.Messaging;
using Npgsql;

namespace NexaConnect.IntegrationTests;

public sealed class InboxPersistenceTests : IAsyncLifetime
{
    private readonly string? configuredConnectionString =
        Environment.GetEnvironmentVariable("NEXACONNECT_INBOX_INTEGRATION_DB");
    private NpgsqlDataSource? dataSource;
    private string? schema;

    [InboxDatabaseFact]
    public async Task Duplicate_is_suppressed_and_failed_claim_is_retryable()
    {
        var store = new PostgresInboxStore(dataSource!);
        Guid messageId = Guid.NewGuid();

        Assert.True(await store.TryClaimAsync(messageId, "reporting.projection", TimeSpan.FromMinutes(1), CancellationToken.None));
        Assert.False(await store.TryClaimAsync(messageId, "reporting.projection", TimeSpan.FromMinutes(1), CancellationToken.None));

        await store.ReleaseAsync(messageId, "reporting.projection", "transient-test-failure", CancellationToken.None);
        Assert.True(await store.TryClaimAsync(messageId, "reporting.projection", TimeSpan.FromMinutes(1), CancellationToken.None));

        await store.MarkCompletedAsync(messageId, "reporting.projection", CancellationToken.None);
        Assert.False(await store.TryClaimAsync(messageId, "reporting.projection", TimeSpan.FromMinutes(1), CancellationToken.None));
    }

    [InboxDatabaseFact]
    public async Task Concurrent_claimers_allow_exactly_one_processing_lease()
    {
        var store = new PostgresInboxStore(dataSource!);
        Guid messageId = Guid.NewGuid();

        InboxClaimResult[] results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            store.ClaimAsync(messageId, "reporting.concurrent", TimeSpan.FromMinutes(1), CancellationToken.None)));

        Assert.Equal(1, results.Count(result => result == InboxClaimResult.Claimed));
        Assert.Equal(9, results.Count(result => result == InboxClaimResult.Busy));
    }

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(configuredConnectionString) || !IsSafeEnvironment()) return;

        schema = $"inbox_it_{Guid.NewGuid():N}";
        var builder = new NpgsqlConnectionStringBuilder(configuredConnectionString) { SearchPath = schema };
        dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        await using (var createSchema = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\";", connection))
            await createSchema.ExecuteNonQueryAsync();
        await using var createTable = new NpgsqlCommand(SchemaSql, connection);
        await createTable.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (dataSource is null || schema is null) return;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE;", connection);
        await drop.ExecuteNonQueryAsync();
        await dataSource.DisposeAsync();
    }

    private static bool IsSafeEnvironment()
    {
        string? environment = Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        return environment is "Development" or "Test" or "Testing";
    }

    private const string SchemaSql = """
        CREATE TABLE inbox_messages
        (
            message_id uuid NOT NULL,
            consumer_name text NOT NULL,
            status text NOT NULL DEFAULT 'queued',
            attempts integer NOT NULL DEFAULT 0,
            locked_until_utc timestamptz NULL,
            processed_at_utc timestamptz NULL,
            last_error_category text NULL,
            CONSTRAINT pk_inbox_messages PRIMARY KEY (message_id, consumer_name)
        );
        """;
}

public sealed class InboxDatabaseFactAttribute : FactAttribute
{
    public InboxDatabaseFactAttribute()
    {
        string? environment = Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NEXACONNECT_INBOX_INTEGRATION_DB"))
            || environment is not ("Development" or "Test" or "Testing"))
            Skip = "NEXACONNECT_INBOX_INTEGRATION_DB and a Development/Test/Testing environment are required.";
    }
}
