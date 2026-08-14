extern alias MEDIA;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using SkiaSharp;
using MediaQuota = MEDIA::NexaConnect.Services.Media.Application.MediaQuota;
using StartMediaUploadCommand = MEDIA::NexaConnect.Services.Media.Application.StartMediaUploadCommand;
using IMediaObjectStorage = MEDIA::NexaConnect.Services.Media.Application.IMediaObjectStorage;
using StoredObjectInfo = MEDIA::NexaConnect.Services.Media.Application.StoredObjectInfo;
using MediaLifecycleConflictException = MEDIA::NexaConnect.Services.Media.Application.MediaLifecycleConflictException;
using MediaMaintenanceWorker = MEDIA::NexaConnect.Services.Media.Infrastructure.MediaMaintenanceWorker;
using PostgresMediaManagementRepository = MEDIA::NexaConnect.Services.Media.Infrastructure.Persistence.PostgresMediaManagementRepository;

namespace NexaConnect.IntegrationTests;

public sealed class MediaPostgresAcceptanceTests
{
    [ConfiguredEnvironmentFact("NEXACONNECT_MEDIA_INTEGRATION_DB")]
    public async Task Quota_expiry_and_variant_jobs_are_tenant_scoped_and_durable()
    {
        string baseConnection = Environment.GetEnvironmentVariable("NEXACONNECT_MEDIA_INTEGRATION_DB")!;
        string schema = "media_acceptance_" + Guid.NewGuid().ToString("N");
        var builder = new NpgsqlConnectionStringBuilder(baseConnection) { SearchPath = schema };
        await using var admin = new NpgsqlConnection(baseConnection); await admin.OpenAsync();
        try
        {
            await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\";", admin)) await create.ExecuteNonQueryAsync();
            await using var dataSource = NpgsqlDataSource.Create(builder.ConnectionString); await ApplyMigrationsAsync(dataSource);
            var repository = new PostgresMediaManagementRepository(dataSource); Guid organizationId = Guid.NewGuid(); string actor = "acceptance-user";
            StartMediaUploadCommand command = new("catalog", "product", Guid.NewGuid(), "one.png", "image/png", 68, new string('a', 64));
            var first = await repository.StartAsync(organizationId, Guid.NewGuid(), $"organizations/{organizationId:D}/assets/one/original", command, actor, DateTimeOffset.UtcNow.AddMinutes(10), new MediaQuota(100, 1), default);
            await Assert.ThrowsAsync<MediaLifecycleConflictException>(() => repository.StartAsync(organizationId, Guid.NewGuid(), $"organizations/{organizationId:D}/assets/two/original", command, actor, DateTimeOffset.UtcNow.AddMinutes(10), new MediaQuota(100, 1), default));
            Guid concurrentOrganization = Guid.NewGuid(); Task<bool>[] starts = Enumerable.Range(0, 4).Select(index => TryStartAsync(repository, concurrentOrganization, command, actor, index)).ToArray();
            Assert.Equal(1, (await Task.WhenAll(starts)).Count(value => value));

            await using (var expire = dataSource.CreateCommand("UPDATE media_assets SET upload_expires_at_utc=now()-interval '1 minute' WHERE id=$1;")) { expire.Parameters.AddWithValue(first.Id); await expire.ExecuteNonQueryAsync(); }
            var storage = new AcceptanceStorage(); var worker = new MediaMaintenanceWorker(dataSource, storage, NullLogger<MediaMaintenanceWorker>.Instance);
            Assert.True(await worker.ProcessExpiredOnceAsync(default));
            Assert.Equal("failed", await ScalarAsync<string>(dataSource, "SELECT processing_status FROM media_assets WHERE id=$1", first.Id));
            Assert.Equal(1L, await ScalarAsync<long>(dataSource, "SELECT count(*) FROM media_object_deletions WHERE asset_id=$1", first.Id));

            Guid secondId = Guid.NewGuid(); var second = await repository.StartAsync(organizationId, secondId, $"organizations/{organizationId:D}/assets/{secondId:D}/original", command, actor, DateTimeOffset.UtcNow.AddMinutes(10), new MediaQuota(100, 1), default);
            Assert.NotNull(await repository.CompleteAsync(organizationId, second.Id, second.ConcurrencyVersion, actor, default));
            Guid backlogOrganization=Guid.NewGuid(),backlogId=Guid.NewGuid();await repository.StartAsync(backlogOrganization,backlogId,$"organizations/{backlogOrganization:D}/assets/{backlogId:D}/original",command,actor,DateTimeOffset.UtcNow.AddMinutes(10),new MediaQuota(100,1),default);await using(var backlog=dataSource.CreateCommand("UPDATE media_assets SET upload_expires_at_utc=now()-interval '1 minute' WHERE id=$1;")){backlog.Parameters.AddWithValue(backlogId);await backlog.ExecuteNonQueryAsync();}
            Assert.True(await worker.ProcessCycleAsync(true,default));
            Assert.Equal(2L, await ScalarAsync<long>(dataSource, "SELECT count(*) FROM media_variants WHERE media_asset_id=$1 AND status='ready'", second.Id));
            Assert.Equal(0L, await ScalarAsync<long>(dataSource, "SELECT count(*) FROM media_processing_jobs WHERE asset_id=$1", second.Id));
            Assert.Equal(2, storage.Writes.Count);

            Guid thirdId = Guid.NewGuid(); var third = await repository.StartAsync(organizationId, thirdId, $"organizations/{organizationId:D}/assets/{thirdId:D}/original", command, actor, DateTimeOffset.UtcNow.AddMinutes(10), new MediaQuota(1000, 2), default);
            var completedThird = await repository.CompleteAsync(organizationId, third.Id, third.ConcurrencyVersion, actor, default); Assert.NotNull(completedThird);
            MEDIA::NexaConnect.Services.Media.Application.MediaAssetSummary? deletedDuringGeneration=null; storage.BeforePut = async () => { storage.BeforePut = null;deletedDuringGeneration=await repository.DeleteAsync(organizationId, third.Id, completedThird!.ConcurrencyVersion, actor, default); };
            Assert.True(await worker.ProcessVariantOnceAsync(default));
            Assert.NotNull(deletedDuringGeneration);
            Assert.Equal(2L, await ScalarAsync<long>(dataSource, "SELECT count(*) FROM media_object_deletions WHERE asset_id=$1", third.Id));
            Assert.Equal(0L, await ScalarAsync<long>(dataSource, "SELECT count(*) FROM media_processing_jobs WHERE asset_id=$1", third.Id));
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE;", admin); await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task ApplyMigrationsAsync(NpgsqlDataSource dataSource)
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../src/Tools/NexaConnect.DataMigration/Scripts/Media"));
        foreach (string path in Directory.GetDirectories(root).OrderBy(value => value, StringComparer.Ordinal))
        { await using var command = dataSource.CreateCommand(await File.ReadAllTextAsync(Path.Combine(path, "up.sql"))); await command.ExecuteNonQueryAsync(); }
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlDataSource source, string sql, Guid id)
    { await using var command = source.CreateCommand(sql); command.Parameters.AddWithValue(id); return (T)(await command.ExecuteScalarAsync())!; }

    private static async Task<bool> TryStartAsync(PostgresMediaManagementRepository repository,Guid organizationId,StartMediaUploadCommand command,string actor,int index)
    { try { Guid id=Guid.NewGuid(); await repository.StartAsync(organizationId,id,$"organizations/{organizationId:D}/assets/{index}/original",command,actor,DateTimeOffset.UtcNow.AddMinutes(10),new MediaQuota(100,1),default); return true; } catch(MediaLifecycleConflictException) { return false; } }

    private sealed class AcceptanceStorage : IMediaObjectStorage
    {
        private static readonly byte[] Png = CreatePng();
        public List<string> Writes { get; } = [];
        public Func<Task>? BeforePut { get; set; }
        public Task<string> CreateUploadUrlAsync(string key,string type,long size,string checksum,TimeSpan lifetime,CancellationToken cancellationToken)=>Task.FromResult("https://upload.invalid");
        public Task<string> CreateDownloadUrlAsync(string key,TimeSpan lifetime,CancellationToken cancellationToken)=>Task.FromResult("https://download.invalid");
        public Task<StoredObjectInfo?> InspectAsync(string key,CancellationToken cancellationToken)=>Task.FromResult<StoredObjectInfo?>(null);
        public Task<byte[]> ReadAsync(string key,long maximumBytes,CancellationToken cancellationToken)=>Task.FromResult(Png);
        public async Task PutAsync(string key,byte[] content,string contentType,string checksumSha256,CancellationToken cancellationToken){Writes.Add(key);if(BeforePut is not null)await BeforePut();}
        public Task DeleteAsync(string key,CancellationToken cancellationToken)=>Task.CompletedTask;
        private static byte[] CreatePng(){using var bitmap=new SKBitmap(2,2);bitmap.Erase(SKColors.Blue);using SKImage image=SKImage.FromBitmap(bitmap);using SKData data=image.Encode(SKEncodedImageFormat.Png,100);return data.ToArray();}
    }
}

public sealed class ConfiguredEnvironmentFactAttribute : FactAttribute
{
    public ConfiguredEnvironmentFactAttribute(string environmentVariable)
    { if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(environmentVariable))) Skip = $"{environmentVariable} is required for this PostgreSQL acceptance test."; }
}
