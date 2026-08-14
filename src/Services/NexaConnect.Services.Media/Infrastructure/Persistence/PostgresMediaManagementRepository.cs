using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Infrastructure.Messaging;
using NexaConnect.Services.Media.Application;
using Npgsql;

namespace NexaConnect.Services.Media.Infrastructure.Persistence;

public sealed class PostgresMediaManagementRepository(NpgsqlDataSource dataSource) : IMediaManagementRepository
{
    private const string Columns = "id,owner_service,owner_type,owner_id,original_file_name,content_type,size_bytes,processing_status,uploaded_at_utc,processed_at_utc,concurrency_version";

    public async Task<MediaAssetSummary> StartAsync(Guid org, Guid id, string key, StartMediaUploadCommand value, string actor, DateTimeOffset expires, MediaQuota quota, CancellationToken cancellationToken)
    {
        string sql = $"INSERT INTO media_assets(id,organization_id,owner_service,owner_type,owner_id,object_key,original_file_name,content_type,size_bytes,checksum_sha256,processing_status,uploaded_at_utc,created_by,updated_at_utc,upload_expires_at_utc) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,'pending',now(),$11,now(),$12) RETURNING {Columns};";
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var quotaLock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended($1,0));", connection, transaction))
        {
            quotaLock.Parameters.AddWithValue(org.ToString("D")); await quotaLock.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var usage = new NpgsqlCommand("SELECT count(*) FILTER (WHERE processing_status='pending'),COALESCE(sum(size_bytes) FILTER (WHERE processing_status IN ('pending','ready','processing')),0)::bigint FROM media_assets WHERE organization_id=$1 AND deleted_at_utc IS NULL;", connection, transaction))
        {
            usage.Parameters.AddWithValue(org); await using var quotaReader = await usage.ExecuteReaderAsync(cancellationToken); await quotaReader.ReadAsync(cancellationToken);
            if (quotaReader.GetInt64(0) >= quota.MaximumPendingUploads || quotaReader.GetInt64(1) + value.SizeBytes > quota.MaximumStoredBytes)
                throw new MediaLifecycleConflictException("Organization media upload quota was exceeded.");
        }
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        object[] parameters = [id, org, value.OwnerService, value.OwnerType, value.OwnerId, key, value.FileName, value.ContentType, value.SizeBytes, value.ChecksumSha256, actor, expires];
        foreach (object parameter in parameters) command.Parameters.AddWithValue(parameter);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        MediaAssetSummary result = Read(reader); await reader.DisposeAsync(); await transaction.CommitAsync(cancellationToken); return result;
    }

    public async Task<(MediaAssetSummary Asset, string Key, string Checksum)?> FindAsync(Guid org, Guid id, CancellationToken cancellationToken)
    {
        string sql = $"SELECT {Columns},object_key,checksum_sha256 FROM media_assets WHERE organization_id=$1 AND id=$2 AND deleted_at_utc IS NULL;";
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(org); command.Parameters.AddWithValue(id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? (Read(reader), reader.GetString(11), reader.GetString(12)) : null;
    }

    public async Task<string?> FindVariantKeyAsync(Guid org, Guid id, string name, CancellationToken cancellationToken)
    {
        const string sql = "SELECT v.object_key FROM media_variants v JOIN media_assets a ON a.id=v.media_asset_id WHERE a.organization_id=$1 AND a.id=$2 AND a.deleted_at_utc IS NULL AND v.variant_name=$3 AND v.status='ready';";
        await using var command = dataSource.CreateCommand(sql); command.Parameters.AddWithValue(org); command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(name); return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public Task<MediaAssetSummary?> CompleteAsync(Guid org, Guid id, long version, string actor, CancellationToken cancellationToken) =>
        Change(org, id, version, actor, "processing_status='ready',processed_at_utc=now(),upload_expires_at_utc=NULL", "media.asset.created", "processing_status='pending' AND upload_expires_at_utc >= now()", false, true, cancellationToken);

    public Task<MediaAssetSummary?> QuarantineAsync(Guid org, Guid id, long version, string actor, string category, CancellationToken cancellationToken) =>
        Change(org, id, version, actor, "processing_status='quarantined',processed_at_utc=now(),upload_expires_at_utc=NULL", "media.asset.quarantined", "processing_status='pending' AND upload_expires_at_utc >= now()", true, false, cancellationToken);

    public Task<MediaAssetSummary?> DeleteAsync(Guid org, Guid id, long version, string actor, CancellationToken cancellationToken) =>
        Change(org, id, version, actor, "processing_status='deleted',deleted_at_utc=now()", "media.asset.deleted", "TRUE", true, false, cancellationToken);

    private async Task<MediaAssetSummary?> Change(Guid org, Guid id, long version, string actor, string set, string action, string predicate, bool enqueueDeletion, bool enqueueProcessing, CancellationToken cancellationToken)
    {
        string sql = $"UPDATE media_assets SET {set},updated_at_utc=now(),concurrency_version=concurrency_version+1 WHERE organization_id=$1 AND id=$2 AND concurrency_version=$3 AND deleted_at_utc IS NULL AND {predicate} RETURNING {Columns},object_key;";
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(org); command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(version);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) { await transaction.RollbackAsync(cancellationToken); return null; }
        MediaAssetSummary result = Read(reader); string objectKey = reader.GetString(11); await reader.DisposeAsync();
        if (enqueueDeletion)
        {
            await using var deletion = new NpgsqlCommand("INSERT INTO media_object_deletions(asset_id,organization_id,object_key,next_attempt_at_utc) VALUES($1,$2,$3,now()) ON CONFLICT(object_key) DO NOTHING;", connection, transaction);
            deletion.Parameters.AddWithValue(id); deletion.Parameters.AddWithValue(org); deletion.Parameters.AddWithValue(objectKey);
            await deletion.ExecuteNonQueryAsync(cancellationToken);
            await using var variantDeletion = new NpgsqlCommand("INSERT INTO media_object_deletions(asset_id,organization_id,object_key,next_attempt_at_utc) SELECT $1,$2,object_key,now() FROM media_variants WHERE media_asset_id=$1 AND status='ready' ON CONFLICT(object_key) DO NOTHING;", connection, transaction); variantDeletion.Parameters.AddWithValue(id); variantDeletion.Parameters.AddWithValue(org); await variantDeletion.ExecuteNonQueryAsync(cancellationToken);
            await using var variantState = new NpgsqlCommand("UPDATE media_variants SET status='deleted' WHERE media_asset_id=$1 AND status<>'deleted';", connection, transaction); variantState.Parameters.AddWithValue(id); await variantState.ExecuteNonQueryAsync(cancellationToken);
        }
        if (enqueueProcessing)
        {
            await using var processing = new NpgsqlCommand("INSERT INTO media_processing_jobs(asset_id,organization_id,object_key,next_attempt_at_utc) VALUES($1,$2,$3,now()) ON CONFLICT(asset_id) DO NOTHING;", connection, transaction);
            processing.Parameters.AddWithValue(id); processing.Parameters.AddWithValue(org); processing.Parameters.AddWithValue(objectKey); await processing.ExecuteNonQueryAsync(cancellationToken);
        }
        await TransactionalAuditOutbox.EnqueueAuditAsync(connection, transaction, new(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, actor, org, action, "media-asset", id.ToString("D"), "succeeded"), "media.audit.v1", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static MediaAssetSummary Read(NpgsqlDataReader reader) => new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetGuid(3), reader.GetString(4), reader.GetString(5), reader.GetInt64(6), reader.GetString(7), reader.GetFieldValue<DateTimeOffset>(8), reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9), reader.GetInt64(10));
}
