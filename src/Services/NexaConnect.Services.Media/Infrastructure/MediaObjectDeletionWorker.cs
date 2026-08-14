using NexaConnect.Services.Media.Application;
using Npgsql;

namespace NexaConnect.Services.Media.Infrastructure;

public sealed class MediaObjectDeletionWorker(NpgsqlDataSource dataSource, IMediaObjectStorage storage, ILogger<MediaObjectDeletionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { if (!await ProcessOne(stoppingToken)) await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception) { logger.LogError(exception, "Media object deletion worker iteration failed"); await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
        }
    }

    private async Task<bool> ProcessOne(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string claimSql = "SELECT asset_id,object_key FROM media_object_deletions WHERE attempts<10 AND next_attempt_at_utc<=now() ORDER BY next_attempt_at_utc FOR UPDATE SKIP LOCKED LIMIT 1;";
        await using var claim = new NpgsqlCommand(claimSql, connection, transaction);
        await using var reader = await claim.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) { await transaction.RollbackAsync(cancellationToken); return false; }
        Guid assetId = reader.GetGuid(0); string key = reader.GetString(1); await reader.DisposeAsync();
        await using var lease = new NpgsqlCommand("UPDATE media_object_deletions SET attempts=attempts+1,next_attempt_at_utc=now()+(LEAST(power(2,attempts),60)*interval '1 minute'),last_error=NULL WHERE asset_id=$1;", connection, transaction);
        lease.Parameters.AddWithValue(assetId); await lease.ExecuteNonQueryAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        try
        {
            await storage.DeleteAsync(key, cancellationToken);
            await using var complete = dataSource.CreateCommand("DELETE FROM media_object_deletions WHERE asset_id=$1;"); complete.Parameters.AddWithValue(assetId); await complete.ExecuteNonQueryAsync(cancellationToken);
            logger.LogInformation("Media object deletion completed for asset {AssetId}", assetId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await using var failed = dataSource.CreateCommand("UPDATE media_object_deletions SET last_error=$2 WHERE asset_id=$1;"); failed.Parameters.AddWithValue(assetId); failed.Parameters.AddWithValue(exception.GetType().Name); await failed.ExecuteNonQueryAsync(cancellationToken);
            logger.LogWarning("Media object deletion failed and will retry or await manual review for asset {AssetId}, failure {FailureType}", assetId, exception.GetType().Name);
        }
        return true;
    }
}
