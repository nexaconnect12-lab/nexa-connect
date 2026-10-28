using NexaConnect.Contracts.Media;
using NexaConnect.Services.Media.Application;
using NexaConnect.Services.Media.Infrastructure;
using SkiaSharp;

namespace NexaConnect.UnitTests;

public sealed class GalleryDeliveryTests
{
    [Fact]
    public async Task Delivery_revalidates_selected_scope_and_drops_deleted_or_unready_assets()
    {
        var p=new Ports();var service=new BranchGalleryDelivery(p,p,p,p,TimeProvider.System);
        var result=await service.DeliverAsync(p.Org,p.Branch,"Bearer synthetic",default);
        Assert.Single(result.Selection.Staff);Assert.Single(result.Images);Assert.Empty(result.Selection.Customer);
        Assert.Equal(TimeSpan.FromMinutes(5),result.ExpiresAtUtc-result.IssuedAtUtc);Assert.True(p.Reads>1);
        p.Ready=false;result=await service.DeliverAsync(p.Org,p.Branch,"Bearer synthetic",default);
        Assert.Empty(result.Images);Assert.Empty(result.Selection.Staff);
    }
    [Fact]
    public async Task Mismatched_or_denied_selection_never_reads_object_storage()
    {
        var p=new Ports();var service=new BranchGalleryDelivery(p,p,p,p,TimeProvider.System);
        await Assert.ThrowsAsync<InvalidDataException>(()=>service.DeliverAsync(Guid.NewGuid(),p.Branch,"Bearer synthetic",default));Assert.Equal(0,p.Reads);
        p.Denied=true;await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.DeliverAsync(p.Org,p.Branch,"Bearer synthetic",default));Assert.Equal(0,p.Reads);
    }
    [Fact]
    public void Decoder_rejects_malformed_content_and_converts_only_bounded_processed_images()
    {
        var encoder=new GalleryImageEncoder();Assert.Throws<InvalidDataException>(()=>encoder.ToPng([1,2,3]));
        using var bitmap=new SKBitmap(32,32);bitmap.Erase(SKColors.Green);using var image=SKImage.FromBitmap(bitmap);using var webp=image.Encode(SKEncodedImageFormat.Webp,80);
        var png=encoder.ToPng(webp.ToArray());Assert.Equal(new byte[]{137,80,78,71,13,10,26,10},png[..8]);
        using var huge=new SKBitmap(1281,1);using var largeImage=SKImage.FromBitmap(huge);using var large=largeImage.Encode(SKEncodedImageFormat.Png,100);
        Assert.Throws<InvalidDataException>(()=>encoder.ToPng(large.ToArray()));
    }
    [Theory]
    [InlineData(1280,1280)][InlineData(1280,853)][InlineData(853,1280)]
    public void Detailed_processed_photos_are_reduced_to_fit_the_existing_png_delivery_limit(int width,int height)
    {
        using var bitmap=new SKBitmap(width,height);
        var random=new Random(173);var pixels=new SKColor[width*height];
        for(int i=0;i<pixels.Length;i++)pixels[i]=new SKColor((byte)random.Next(256),(byte)random.Next(256),(byte)random.Next(256));
        bitmap.Pixels=pixels;using var source=SKImage.FromBitmap(bitmap);using var display=source.Encode(SKEncodedImageFormat.Jpeg,80);
        Assert.InRange(display.Size,1,2_097_152);
        using var decoded=SKBitmap.Decode(display.ToArray());using var decodedImage=SKImage.FromBitmap(decoded);using var full=decodedImage.Encode(SKEncodedImageFormat.Png,100);
        Assert.True(full.Size>2_097_152); // Reproduces expansion of a valid processed photo.
        byte[] png=new GalleryImageEncoder().ToPng(display.ToArray());
        Assert.InRange(png.Length,24,2_097_152);Assert.Equal(new byte[]{137,80,78,71,13,10,26,10},png[..8]);
        using var result=SKBitmap.Decode(png);Assert.NotNull(result);
        Assert.InRange(result.Width,1,width-1);Assert.InRange(result.Height,1,height-1);
        Assert.InRange(Math.Abs((double)result.Width/result.Height-(double)width/height),0,0.01);
    }
    private sealed class Ports:IBranchGallerySelectionPort,IMediaManagementRepository,IMediaObjectStorage,IGalleryImageEncoder
    {
        public Guid Org=Guid.NewGuid(),Branch=Guid.NewGuid(),Allowed=Guid.NewGuid(),Deleted=Guid.NewGuid();public bool Ready=true,Denied;public int Reads;
        public Task<BranchGallery> ReadAsync(Guid org,Guid branch,string bearer,CancellationToken ct)=>Denied?throw new UnauthorizedAccessException():Task.FromResult(new BranchGallery(Org,Branch,2,[new(Allowed,"Meal","")],[new(Deleted,"Deleted","")]));
        public Task<(MediaAssetSummary Asset,string Key,string Checksum)?> FindAsync(Guid org,Guid id,CancellationToken ct){Reads++;return Task.FromResult<(MediaAssetSummary,string,string)?>(id!=Allowed||!Ready?null:(new(id,"catalog","product",Guid.NewGuid(),"synthetic.png","image/png",1,"ready",DateTimeOffset.UtcNow,null,1),"synthetic",new string('a',64)));}
        public Task<string?> FindVariantKeyAsync(Guid org,Guid id,string name,CancellationToken ct)=>Task.FromResult<string?>("synthetic");
        public byte[] ToPng(byte[] value)=>value;
        public Task<byte[]> ReadAsync(string key,long max,CancellationToken ct)=>Task.FromResult(new byte[]{1,2,3});
        public Task<MediaAssetSummary> StartAsync(Guid o,Guid i,string k,StartMediaUploadCommand v,string a,DateTimeOffset e,MediaQuota q,CancellationToken ct)=>throw new NotSupportedException();
        public Task<MediaAssetSummary?> CompleteAsync(Guid o,Guid i,long v,string a,CancellationToken ct)=>throw new NotSupportedException();
        public Task<MediaAssetSummary?> QuarantineAsync(Guid o,Guid i,long v,string a,string category,CancellationToken ct)=>throw new NotSupportedException();
        public Task<MediaAssetSummary?> DeleteAsync(Guid o,Guid i,long v,string a,CancellationToken ct)=>throw new NotSupportedException();
        public Task<string> CreateUploadUrlAsync(string k,string t,long s,string hash,TimeSpan l,CancellationToken ct)=>throw new NotSupportedException();
        public Task<string> CreateDownloadUrlAsync(string k,TimeSpan l,CancellationToken ct)=>throw new NotSupportedException();
        public Task<StoredObjectInfo?> InspectAsync(string k,CancellationToken ct)=>throw new NotSupportedException();
        public Task PutAsync(string k,byte[] b,string t,string h,CancellationToken ct)=>throw new NotSupportedException();
        public Task DeleteAsync(string k,CancellationToken ct)=>throw new NotSupportedException();
    }
}
