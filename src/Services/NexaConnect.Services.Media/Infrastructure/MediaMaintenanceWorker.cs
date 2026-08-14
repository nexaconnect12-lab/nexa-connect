using System.Security.Cryptography;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Infrastructure.Messaging;
using NexaConnect.Services.Media.Application;
using Npgsql;
using SkiaSharp;

namespace NexaConnect.Services.Media.Infrastructure;

public sealed class MediaMaintenanceWorker(NpgsqlDataSource dataSource, IMediaObjectStorage storage, ILogger<MediaMaintenanceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        bool variantsFirst = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                variantsFirst = !variantsFirst;
                bool worked = await ProcessCycleAsync(variantsFirst, stoppingToken);
                if (!worked) await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogError(exception, "Media maintenance worker iteration failed");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    public async Task<bool> ProcessCycleAsync(bool variantsFirst, CancellationToken cancellationToken) => variantsFirst
        ? await ProcessVariantOnceAsync(cancellationToken) || await ProcessExpiredOnceAsync(cancellationToken)
        : await ProcessExpiredOnceAsync(cancellationToken) || await ProcessVariantOnceAsync(cancellationToken);

    public async Task<bool> ProcessExpiredOnceAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string sql = "SELECT id,organization_id,object_key FROM media_assets WHERE processing_status='pending' AND upload_expires_at_utc<now() ORDER BY upload_expires_at_utc FOR UPDATE SKIP LOCKED LIMIT 1;";
        Guid id, organizationId; string key;
        await using (var claim = new NpgsqlCommand(sql, connection, transaction))
        await using (var reader = await claim.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                await reader.DisposeAsync();
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
            id = reader.GetGuid(0); organizationId = reader.GetGuid(1); key = reader.GetString(2);
        }
        await using (var update = new NpgsqlCommand("UPDATE media_assets SET processing_status='failed',processed_at_utc=now(),updated_at_utc=now(),upload_expires_at_utc=NULL,concurrency_version=concurrency_version+1 WHERE id=$1;", connection, transaction)) { update.Parameters.AddWithValue(id); await update.ExecuteNonQueryAsync(cancellationToken); }
        await using (var deletion = new NpgsqlCommand("INSERT INTO media_object_deletions(asset_id,organization_id,object_key,next_attempt_at_utc) VALUES($1,$2,$3,now()) ON CONFLICT(object_key) DO NOTHING;", connection, transaction)) { deletion.Parameters.AddWithValue(id); deletion.Parameters.AddWithValue(organizationId); deletion.Parameters.AddWithValue(key); await deletion.ExecuteNonQueryAsync(cancellationToken); }
        await TransactionalAuditOutbox.EnqueueAuditAsync(connection, transaction, new(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, "media-expiry-worker", organizationId, "media.asset.upload-expired", "media-asset", id.ToString("D"), "succeeded"), "media.audit.v1", cancellationToken);
        await transaction.CommitAsync(cancellationToken); logger.LogInformation("Expired media upload queued for deletion for asset {AssetId}", id); return true;
    }

    public async Task<bool> ProcessVariantOnceAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string sql = "SELECT j.asset_id,j.organization_id,j.object_key,a.content_type,a.size_bytes FROM media_processing_jobs j JOIN media_assets a ON a.id=j.asset_id WHERE j.attempts<10 AND j.next_attempt_at_utc<=now() AND a.processing_status='ready' ORDER BY j.next_attempt_at_utc FOR UPDATE SKIP LOCKED LIMIT 1;";
        Guid id, organizationId; string key; long size;
        await using (var claim = new NpgsqlCommand(sql, connection, transaction))
        await using (var reader = await claim.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                await reader.DisposeAsync();
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
            id = reader.GetGuid(0); organizationId = reader.GetGuid(1); key = reader.GetString(2); size = reader.GetInt64(4);
        }
        await using (var lease = new NpgsqlCommand("UPDATE media_processing_jobs SET attempts=attempts+1,next_attempt_at_utc=now()+(LEAST(power(2,attempts),60)*interval '1 minute'),last_error=NULL WHERE asset_id=$1;", connection, transaction)) { lease.Parameters.AddWithValue(id); await lease.ExecuteNonQueryAsync(cancellationToken); }
        await transaction.CommitAsync(cancellationToken);
        try
        {
            byte[] original = await storage.ReadAsync(key, size, cancellationToken);
            using SKData sourceData = SKData.CreateCopy(original); using SKCodec codec = SKCodec.Create(sourceData) ?? throw new InvalidOperationException("Image decoder rejected uploaded content."); SKImageInfo sourceInfo = codec.Info;
            if (sourceInfo.Width is <1 or >12000 || sourceInfo.Height is <1 or >12000 || (long)sourceInfo.Width * sourceInfo.Height > 40_000_000) throw new InvalidOperationException("Image dimensions exceed the processing limit.");
            using var image = new SKBitmap(sourceInfo.Width, sourceInfo.Height, SKColorType.Rgba8888, SKAlphaType.Premul); if (codec.GetPixels(image.Info, image.GetPixels()) is not (SKCodecResult.Success or SKCodecResult.IncompleteInput)) throw new InvalidOperationException("Image decoder rejected uploaded content.");
            foreach ((string name, int maximum) in new[] { ("thumbnail", 320), ("display", 1280) })
            {
                double scale = Math.Min(1d, Math.Min((double)maximum / image.Width, (double)maximum / image.Height)); int width = Math.Max(1, (int)Math.Round(image.Width * scale)), height = Math.Max(1, (int)Math.Round(image.Height * scale));
                using var variant = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul); using (var canvas = new SKCanvas(variant)) { canvas.DrawBitmap(image, new SKRect(0, 0, width, height), new SKPaint { IsAntialias = true }); }
                using SKImage encodedImage = SKImage.FromBitmap(variant); using SKData encoded = encodedImage.Encode(SKEncodedImageFormat.Webp, 82); byte[] bytes = encoded.ToArray();
                string checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(); string variantKey = $"organizations/{organizationId:D}/assets/{id:D}/{name}.webp";
                await storage.PutAsync(variantKey, bytes, "image/webp", checksum, cancellationToken);
                if (!await TryUpsertVariantAsync(id, organizationId, name, variantKey, bytes.Length, checksum, width, height, cancellationToken))
                {
                    await using var cancelled = dataSource.CreateCommand("DELETE FROM media_processing_jobs WHERE asset_id=$1;"); cancelled.Parameters.AddWithValue(id); await cancelled.ExecuteNonQueryAsync(cancellationToken);
                    logger.LogInformation("Media variant generation cancelled after asset lifecycle changed for asset {AssetId}", id); return true;
                }
            }
            await using var complete = dataSource.CreateCommand("DELETE FROM media_processing_jobs WHERE asset_id=$1;"); complete.Parameters.AddWithValue(id); await complete.ExecuteNonQueryAsync(cancellationToken);
            logger.LogInformation("Media variants generated for asset {AssetId}", id);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            string category=exception is PostgresException postgresException?$"postgres-{postgresException.SqlState}":exception.GetType().Name; await using var failed = dataSource.CreateCommand("UPDATE media_processing_jobs SET last_error=$2 WHERE asset_id=$1;"); failed.Parameters.AddWithValue(id); failed.Parameters.AddWithValue(category); await failed.ExecuteNonQueryAsync(cancellationToken);
            logger.LogWarning("Media variant generation failed and will retry for asset {AssetId}, failure {FailureType}", id, category);
        }
        return true;
    }

    private async Task<bool> TryUpsertVariantAsync(Guid assetId, Guid organizationId, string name, string key, int size, string checksum, int width, int height, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken); await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var state = new NpgsqlCommand("SELECT processing_status FROM media_assets WHERE id=$1 FOR UPDATE;", connection, transaction); state.Parameters.AddWithValue(assetId); string? status = await state.ExecuteScalarAsync(cancellationToken) as string;
        if (status != "ready")
        {
            await using var deletion = new NpgsqlCommand("INSERT INTO media_object_deletions(asset_id,organization_id,object_key,next_attempt_at_utc) VALUES($1,$2,$3,now()) ON CONFLICT(object_key) DO NOTHING;", connection, transaction); deletion.Parameters.AddWithValue(assetId); deletion.Parameters.AddWithValue(organizationId); deletion.Parameters.AddWithValue(key); await deletion.ExecuteNonQueryAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return false;
        }
        const string sql = "INSERT INTO media_variants(id,media_asset_id,variant_name,object_key,content_type,size_bytes,checksum_sha256,width_pixels,height_pixels,status,created_at_utc) VALUES($1,$2,$3,$4,'image/webp',$5,$6,$7,$8,'ready',now()) ON CONFLICT(media_asset_id,variant_name) DO UPDATE SET object_key=excluded.object_key,content_type=excluded.content_type,size_bytes=excluded.size_bytes,checksum_sha256=excluded.checksum_sha256,width_pixels=excluded.width_pixels,height_pixels=excluded.height_pixels,status='ready';";
        await using var command = new NpgsqlCommand(sql, connection, transaction); command.Parameters.AddWithValue(Guid.NewGuid()); command.Parameters.AddWithValue(assetId); command.Parameters.AddWithValue(name); command.Parameters.AddWithValue(key); command.Parameters.AddWithValue(size); command.Parameters.AddWithValue(checksum); command.Parameters.AddWithValue(width); command.Parameters.AddWithValue(height); await command.ExecuteNonQueryAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return true;
    }
}
