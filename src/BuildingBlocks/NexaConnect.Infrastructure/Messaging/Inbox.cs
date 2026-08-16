using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace NexaConnect.Infrastructure.Messaging;

public interface IInboxStore
{
    Task<bool> TryClaimAsync(Guid messageId, string consumerName, TimeSpan lease, CancellationToken cancellationToken);
    Task MarkCompletedAsync(Guid messageId, string consumerName, CancellationToken cancellationToken);
    Task ReleaseAsync(Guid messageId, string consumerName, string errorCategory, CancellationToken cancellationToken);
}
public enum InboxClaimResult{Claimed,Completed,Busy}
public interface IDurableInboxStore:IInboxStore{Task<InboxClaimResult> ClaimAsync(Guid messageId,string consumerName,TimeSpan lease,CancellationToken cancellationToken);}

public static class InboxConsumer
{
    public static async Task<bool> ExecuteOnceAsync(
        this IInboxStore inbox,
        Guid messageId,
        string consumerName,
        Func<CancellationToken, Task> handler,
        TimeSpan? lease = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!await inbox.TryClaimAsync(messageId, consumerName, lease ?? TimeSpan.FromMinutes(2), cancellationToken))
            return false;

        try
        {
            await handler(cancellationToken);
            await inbox.MarkCompletedAsync(messageId, consumerName, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await inbox.ReleaseAsync(messageId, consumerName, exception.GetType().Name, cancellationToken);
            throw;
        }
    }
}

public sealed class InMemoryInboxStore : IDurableInboxStore
{
    private sealed record Entry(string Status, int Attempts, DateTimeOffset? LockedUntilUtc);
    private readonly Dictionary<(Guid MessageId, string ConsumerName), Entry> entries = new();
    private readonly object gate = new();

    public async Task<bool> TryClaimAsync(Guid messageId, string consumerName, TimeSpan lease, CancellationToken cancellationToken)
        => await ClaimAsync(messageId, consumerName, lease, cancellationToken) == InboxClaimResult.Claimed;

    public Task<InboxClaimResult> ClaimAsync(Guid messageId, string consumerName, TimeSpan lease, CancellationToken cancellationToken)
    {
        Validate(messageId, consumerName, lease);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var key = (messageId, consumerName.Trim());
        lock (gate)
        {
            if (entries.TryGetValue(key, out Entry? current)
                && (current.Status == "completed" || (current.Status == "processing" && current.LockedUntilUtc > now)))
                return Task.FromResult(current.Status == "completed" ? InboxClaimResult.Completed : InboxClaimResult.Busy);

            entries[key] = new Entry("processing", current?.Attempts + 1 ?? 1, now.Add(lease));
            return Task.FromResult(InboxClaimResult.Claimed);
        }
    }

    public Task MarkCompletedAsync(Guid messageId, string consumerName, CancellationToken cancellationToken)
    {
        lock (gate)
            entries[(messageId, consumerName.Trim())] = new Entry("completed", 0, null);
        return Task.CompletedTask;
    }

    public Task ReleaseAsync(Guid messageId, string consumerName, string errorCategory, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (entries.TryGetValue((messageId, consumerName.Trim()), out Entry? current))
                entries[(messageId, consumerName.Trim())] = current with { Status = "queued", LockedUntilUtc = null };
        }
        return Task.CompletedTask;
    }

    private static void Validate(Guid messageId, string consumerName, TimeSpan lease)
    {
        if (messageId == Guid.Empty) throw new ArgumentException("Inbox message id is required.");
        if (string.IsNullOrWhiteSpace(consumerName)) throw new ArgumentException("Inbox consumer name is required.");
        if (lease <= TimeSpan.Zero) throw new ArgumentException("Inbox lease must be positive.");
    }
}

public sealed class PostgresInboxStore(NpgsqlDataSource dataSource) : IDurableInboxStore
{
    public async Task<bool> TryClaimAsync(Guid messageId, string consumerName, TimeSpan lease, CancellationToken cancellationToken)
        =>await ClaimAsync(messageId,consumerName,lease,cancellationToken)==InboxClaimResult.Claimed;
    public async Task<InboxClaimResult> ClaimAsync(Guid messageId, string consumerName, TimeSpan lease, CancellationToken cancellationToken)
    {
        if (messageId == Guid.Empty) throw new ArgumentException("Inbox message id is required.");
        if (string.IsNullOrWhiteSpace(consumerName)) throw new ArgumentException("Inbox consumer name is required.");
        if (lease <= TimeSpan.Zero) throw new ArgumentException("Inbox lease must be positive.");

        const string insertSql = """
            INSERT INTO inbox_messages (message_id, consumer_name, status, attempts, locked_until_utc)
            VALUES ($1, $2, 'queued', 0, NULL)
            ON CONFLICT (message_id, consumer_name) DO NOTHING;
            """;
        const string claimSql = """
            UPDATE inbox_messages
            SET status = 'processing', attempts = attempts + 1,
                locked_until_utc = now() + ($3 * interval '1 second'), last_error_category = NULL
            WHERE message_id = $1 AND consumer_name = $2
              AND status <> 'completed'
              AND (status <> 'processing' OR locked_until_utc IS NULL OR locked_until_utc <= now())
            RETURNING message_id;
            """;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var insert = new NpgsqlCommand(insertSql, connection, transaction))
        {
            insert.Parameters.AddWithValue(messageId);
            insert.Parameters.AddWithValue(consumerName.Trim());
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var claim = new NpgsqlCommand(claimSql, connection, transaction))
        {
            claim.Parameters.AddWithValue(messageId);
            claim.Parameters.AddWithValue(consumerName.Trim());
            claim.Parameters.AddWithValue(lease.TotalSeconds);
            if (await claim.ExecuteScalarAsync(cancellationToken) is Guid)
            {
                await transaction.CommitAsync(cancellationToken);
                return InboxClaimResult.Claimed;
            }
        }
        await using var status = new NpgsqlCommand("SELECT status FROM inbox_messages WHERE message_id=$1 AND consumer_name=$2;", connection, transaction);
        status.Parameters.AddWithValue(messageId);
        status.Parameters.AddWithValue(consumerName.Trim());
        InboxClaimResult result = (string?)await status.ExecuteScalarAsync(cancellationToken) == "completed"
            ? InboxClaimResult.Completed
            : InboxClaimResult.Busy;
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public Task MarkCompletedAsync(Guid messageId, string consumerName, CancellationToken cancellationToken) => ExecuteAsync(
        "UPDATE inbox_messages SET status='completed', locked_until_utc=NULL, processed_at_utc=now(), last_error_category=NULL WHERE message_id=$1 AND consumer_name=$2;",
        messageId, consumerName, null, cancellationToken);

    public Task ReleaseAsync(Guid messageId, string consumerName, string errorCategory, CancellationToken cancellationToken) => ExecuteAsync(
        "UPDATE inbox_messages SET status='queued', locked_until_utc=NULL, last_error_category=$3 WHERE message_id=$1 AND consumer_name=$2;",
        messageId, consumerName, errorCategory, cancellationToken);

    private async Task ExecuteAsync(string sql, Guid messageId, string consumerName, string? errorCategory, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(messageId);
        command.Parameters.AddWithValue(consumerName.Trim());
        if (errorCategory is not null) command.Parameters.AddWithValue(errorCategory);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public static class InboxServiceCollectionExtensions
{
    public static IServiceCollection AddPostgresInbox(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName)
    {
        string connectionString = configuration.GetConnectionString(connectionStringName)
            ?? throw new InvalidOperationException($"ConnectionStrings:{connectionStringName} is required for PostgreSQL inbox persistence.");
        services.TryAddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddSingleton<PostgresInboxStore>();
        services.AddSingleton<IInboxStore>(provider => provider.GetRequiredService<PostgresInboxStore>());
        services.AddSingleton<IDurableInboxStore>(provider => provider.GetRequiredService<PostgresInboxStore>());
        return services;
    }
}
