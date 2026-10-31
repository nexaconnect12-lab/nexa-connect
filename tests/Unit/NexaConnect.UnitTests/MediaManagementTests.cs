using NexaConnect.Services.Media.Application;

namespace NexaConnect.UnitTests;

public sealed class MediaManagementTests
{
    [Theory][InlineData("pending")][InlineData("quarantined")][InlineData("wrong-owner")][InlineData("no-variant")][InlineData("version-race")][InlineData("ready")]
    public async Task Menu_photo_delivery_requires_ready_processed_product_and_rechecks_lifecycle(string condition)
    {
        var repo=new Repo{Found=Asset(),Variant="processed-thumbnail"};var found=repo.Found!.Value;
        repo.Found=found with{Asset=found.Asset with{ProcessingStatus=condition is "pending" or "quarantined"?condition:"ready",OwnerService=condition=="wrong-owner"?"restaurant":"catalog"}};
        if(condition=="no-variant")repo.Variant=null;
        var storage=new Storage();if(condition=="version-race")storage.AfterRead=()=>{var v=repo.Found!.Value;repo.Found=v with{Asset=v.Asset with{ConcurrencyVersion=v.Asset.ConcurrencyVersion+1}};};
        var output=await new MenuPhotoDelivery(repo,storage,new PhotoEncoder()).ReadAsync(repo.Org,repo.Id,default);
        if(condition=="ready"){Assert.Equal(new byte[]{1,2,3},output);Assert.Equal("processed-thumbnail",storage.ReadKey);Assert.Equal(2097152,storage.MaximumRead);}
        else Assert.Null(output);
    }
    private sealed class PhotoEncoder:IMenuPhotoEncoder {public byte[] Encode(byte[] source)=>[1,2,3];}
    [Fact]
    public async Task Start_rejects_unsafe_type_size_and_owner()
    {
        var service = Service();
        await Assert.ThrowsAsync<ArgumentException>(() => service.StartAsync(Guid.NewGuid(), new("other", "product", Guid.NewGuid(), "a.exe", "application/octet-stream", 1, new string('a', 64)), "actor", default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.StartAsync(Guid.NewGuid(), new("catalog", "product", Guid.NewGuid(), "a.png", "image/png", 10_485_761, new string('a', 64)), "actor", default));
    }

    [Fact]
    public async Task Start_generates_tenant_scoped_key_and_normalizes()
    {
        var repo = new Repo(); Guid org = Guid.NewGuid();
        await Service(repo).StartAsync(org, new(" CATALOG ", " PRODUCT ", Guid.NewGuid(), "../image.png", "IMAGE/PNG", 12, new string('a', 64)), " actor ", default);
        Assert.StartsWith($"organizations/{org:D}/assets/", repo.Key); Assert.Equal("image.png", repo.Command!.FileName);
    }

    [Fact]
    public async Task Complete_requires_size_and_checksum_match()
    {
        var repo = new Repo { Found = Asset() }; var storage = new Storage { Info = new(9, new string('b', 64)) };
        await Assert.ThrowsAsync<MediaLifecycleConflictException>(() => Service(repo, storage).CompleteAsync(repo.Org, repo.Id, 1, "actor", default));
    }

    [Fact]
    public async Task Complete_quarantines_unsafe_content()
    {
        var repo = new Repo { Found = Asset() }; var storage = new Storage { Info = new(10, new string('a', 64)), Content = new byte[10] };
        await Assert.ThrowsAsync<MediaLifecycleConflictException>(() => Service(repo, storage, safety: new Safety(false)).CompleteAsync(repo.Org, repo.Id, 1, "actor", default));
        Assert.True(repo.Quarantined);
    }

    [Fact]
    public async Task Complete_classifies_scanner_failure_as_dependency_failure()
    {
        var repo = new Repo { Found = Asset() }; var storage = new Storage { Info = new(10, new string('a', 64)), Content = new byte[10] };
        await Assert.ThrowsAsync<MediaDependencyException>(() => Service(repo, storage, safety: new FailingSafety()).CompleteAsync(repo.Org, repo.Id, 1, "actor", default));
        Assert.False(repo.Quarantined);
    }

    [Fact]
    public async Task Start_rejects_owner_outside_active_organization() =>
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Service(owners: new Owners { Exists = false }).StartAsync(Guid.NewGuid(), new("catalog", "product", Guid.NewGuid(), "a.png", "image/png", 10, new string('a', 64)), "actor", default));

    private static MediaManagement Service(Repo? repo = null, Storage? storage = null, Owners? owners = null, IMediaContentSafety? safety = null) => new(repo ?? new(), storage ?? new(), owners ?? new(), safety ?? new Safety(true), new MediaQuota(1_073_741_824, 20));
    private static (MediaAssetSummary Asset, string Key, string Checksum) Asset() { Guid id = Guid.NewGuid(); return (new(id, "catalog", "product", Guid.NewGuid(), "a.png", "image/png", 10, "pending", DateTimeOffset.UtcNow, null, 1), "key", new string('a', 64)); }

    private sealed class Safety(bool safe) : IMediaContentSafety { public Task<MediaSafetyResult> InspectAsync(byte[] content, string type, CancellationToken c) => Task.FromResult(new MediaSafetyResult(safe, safe ? null : "unsafe")); }
    private sealed class FailingSafety : IMediaContentSafety { public Task<MediaSafetyResult> InspectAsync(byte[] content, string type, CancellationToken c) => throw new IOException("scanner unavailable"); }
    private sealed class Owners : IMediaOwnerValidator { public bool Exists = true; public Task<bool> ExistsAsync(Guid o, string s, string t, Guid id, CancellationToken c) => Task.FromResult(Exists); }
    private sealed class Storage : IMediaObjectStorage
    {
        public StoredObjectInfo? Info; public byte[] Content = new byte[10];public Action? AfterRead;public string? ReadKey;public long MaximumRead;
        public Task<string> CreateUploadUrlAsync(string k, string t, long s, string h, TimeSpan l, CancellationToken c) => Task.FromResult("https://upload.test");
        public Task<string> CreateDownloadUrlAsync(string k, TimeSpan l, CancellationToken c) => Task.FromResult("https://download.test");
        public Task<StoredObjectInfo?> InspectAsync(string k, CancellationToken c) => Task.FromResult(Info);
        public Task<byte[]> ReadAsync(string k, long m, CancellationToken c) {ReadKey=k;MaximumRead=m;AfterRead?.Invoke();return Task.FromResult(Content);}
        public Task PutAsync(string k, byte[] content, string type, string checksum, CancellationToken c) => Task.CompletedTask;
        public Task DeleteAsync(string k, CancellationToken c) => Task.CompletedTask;
    }
    private sealed class Repo : IMediaManagementRepository
    {
        public Guid Org = Guid.NewGuid(), Id = Guid.NewGuid(); public string? Key; public StartMediaUploadCommand? Command; public (MediaAssetSummary Asset, string Key, string Checksum)? Found; public bool Quarantined;
        public Task<MediaAssetSummary> StartAsync(Guid o, Guid id, string k, StartMediaUploadCommand x, string a, DateTimeOffset e, MediaQuota quota, CancellationToken c) { Key = k; Command = x; return Task.FromResult(new MediaAssetSummary(id, x.OwnerService, x.OwnerType, x.OwnerId, x.FileName, x.ContentType, x.SizeBytes, "pending", DateTimeOffset.UtcNow, null, 1)); }
        public Task<(MediaAssetSummary Asset, string Key, string Checksum)?> FindAsync(Guid o, Guid id, CancellationToken c) => Task.FromResult(Found);
        public string? Variant;
        public Task<string?> FindVariantKeyAsync(Guid o, Guid id, string name, CancellationToken c) => Task.FromResult(Variant);
        public Task<MediaAssetSummary?> CompleteAsync(Guid o, Guid id, long v, string a, CancellationToken c) => Task.FromResult<MediaAssetSummary?>(Found?.Asset);
        public Task<MediaAssetSummary?> QuarantineAsync(Guid o, Guid id, long v, string a, string category, CancellationToken c) { Quarantined = true; return Task.FromResult<MediaAssetSummary?>(Found?.Asset); }
        public Task<MediaAssetSummary?> DeleteAsync(Guid o, Guid id, long v, string a, CancellationToken c) => Task.FromResult<MediaAssetSummary?>(Found?.Asset);
    }
}
