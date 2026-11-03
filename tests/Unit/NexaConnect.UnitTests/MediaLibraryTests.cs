using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Media.Application;

namespace NexaConnect.UnitTests;

public sealed class MediaLibraryTests
{
    [Theory][InlineData(0)][InlineData(10001)]
    public void Page_is_bounded(int page)=>Assert.Throws<ArgumentException>(()=>new MediaLibraryQuery("",page).Validate());
    [Theory][InlineData("bad\nname")][InlineData("bad\0name")]
    public void Search_rejects_control_characters(string search)=>Assert.Throws<ArgumentException>(()=>new MediaLibraryQuery(search).Validate());
    [Fact]public void Search_is_normalized_and_bounded()
    {Assert.Equal("meal",new MediaLibraryQuery(" meal ").Validate().Search);Assert.Throws<ArgumentException>(()=>new MediaLibraryQuery(new string('a',101)).Validate());}
    [Fact]public async Task Read_permission_is_checked_before_and_after_each_browse()
    {
        var f=new Fixture();var service=f.Service();var result=await service.BrowseAsync(f.Org,new(" rice ",2,true),"Bearer fixture",default);
        Assert.Equal(2,result.Page);Assert.Equal(new("rice",2,true),f.Query);Assert.Equal(2,f.Authorizations);
        f.Allow=false;await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.BrowseAsync(f.Org,new(),"Bearer fixture",default));Assert.Equal(1,f.Lists);
        f.Allow=true;f.RevokeAfterList=true;await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.BrowseAsync(f.Org,new(),"Bearer fixture",default));
    }
    [Theory][InlineData("ready",true)][InlineData("processing",false)][InlineData("quarantined",false)][InlineData("bank",false)]
    public async Task Thumbnail_uses_only_processed_ready_product_and_never_issues_a_storage_url(string status,bool available)
    {
        var f=new Fixture();f.Asset=f.Asset with {ProcessingStatus=status=="bank"?"ready":status,OwnerService=status=="bank"?"restaurant":"catalog",OwnerType=status=="bank"?"promptpay_qr":"product"};
        var output=await f.Service().ThumbnailAsync(f.Org,f.Asset.Id,"Bearer fixture",default);
        Assert.Equal(available,output is not null);Assert.Equal(available?1:0,f.StorageReads);Assert.Equal(0,f.UrlLeases);
    }
    [Fact]public async Task Revocation_or_deleted_variant_during_read_does_not_return_bytes()
    {
        var f=new Fixture{RevokeAfterRead=true};await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Service().ThumbnailAsync(f.Org,f.Asset.Id,"Bearer fixture",default));
        f=new Fixture{ChangeAfterRead=true};Assert.Null(await f.Service().ThumbnailAsync(f.Org,f.Asset.Id,"Bearer fixture",default));
    }
    [Fact]public async Task Different_organization_does_not_read_storage()
    {
        var f=new Fixture();await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Service().ThumbnailAsync(Guid.NewGuid(),f.Asset.Id,"Bearer fixture",default));Assert.Equal(0,f.StorageReads);
    }
    internal sealed class Fixture:IMediaLibraryRepository,IMediaCustomerAuthorizer,IMediaManagementRepository,IMediaObjectStorage,IMenuPhotoEncoder
    {
        public Guid Org=Guid.NewGuid();public MediaAssetSummary Asset=new(Guid.NewGuid(),"catalog","product",Guid.NewGuid(),"Rice.png","image/png",10,"ready",DateTimeOffset.UtcNow,null,1);
        public bool Allow=true,RevokeAfterList,RevokeAfterRead,ChangeAfterRead;public int Lists,Authorizations,StorageReads,UrlLeases;public MediaLibraryQuery? Query;
        public MediaLibrary Service()=>new(this,new(this,this,this),this);
        public Task<bool> IsGrantedAsync(Guid org,string permission,string bearer,CancellationToken ct){Assert.Equal(ProductPermissions.MediaAssetRead,permission);Assert.Equal("Bearer fixture",bearer);Authorizations++;return Task.FromResult(org==Org&&Allow);}
        public Task<MediaLibraryPage> BrowseAsync(Guid org,MediaLibraryQuery query,CancellationToken ct){Assert.Equal(Org,org);Query=query;Lists++;if(RevokeAfterList)Allow=false;return Task.FromResult(new MediaLibraryPage([Asset],false,query.Page));}
        public Task<(MediaAssetSummary Asset,string Key,string Checksum)?> FindAsync(Guid org,Guid id,CancellationToken ct)=>Task.FromResult<(MediaAssetSummary,string,string)?>(org==Org&&id==Asset.Id?(Asset,"original",new string('a',64)):null);
        public Task<string?> FindVariantKeyAsync(Guid org,Guid id,string variant,CancellationToken ct){Assert.Equal("thumbnail",variant);return Task.FromResult<string?>("processed-thumbnail");}
        public Task<byte[]> ReadAsync(string key,long maximumBytes,CancellationToken ct){Assert.Equal("processed-thumbnail",key);Assert.Equal(2097152,maximumBytes);StorageReads++;if(RevokeAfterRead)Allow=false;if(ChangeAfterRead)Asset=Asset with {ConcurrencyVersion=2};return Task.FromResult(new byte[]{137,80,78,71});}
        public byte[] Encode(byte[] source)=>source;
        public Task<string> CreateDownloadUrlAsync(string key,TimeSpan lifetime,CancellationToken ct){UrlLeases++;throw new InvalidOperationException();}
        public Task<string> CreateUploadUrlAsync(string key,string type,long size,string checksum,TimeSpan lifetime,CancellationToken ct)=>throw new NotSupportedException();
        public Task<StoredObjectInfo?> InspectAsync(string key,CancellationToken ct)=>throw new NotSupportedException();
        public Task PutAsync(string key,byte[] content,string type,string checksum,CancellationToken ct)=>throw new NotSupportedException();
        public Task DeleteAsync(string key,CancellationToken ct)=>throw new NotSupportedException();
        public Task<MediaAssetSummary> StartAsync(Guid org,Guid id,string key,StartMediaUploadCommand input,string actor,DateTimeOffset expires,MediaQuota quota,CancellationToken ct)=>throw new NotSupportedException();
        public Task<MediaAssetSummary?> CompleteAsync(Guid org,Guid id,long version,string actor,CancellationToken ct)=>throw new NotSupportedException();
        public Task<MediaAssetSummary?> QuarantineAsync(Guid org,Guid id,long version,string actor,string category,CancellationToken ct)=>throw new NotSupportedException();
        public Task<MediaAssetSummary?> DeleteAsync(Guid org,Guid id,long version,string actor,CancellationToken ct)=>throw new NotSupportedException();
    }
}
