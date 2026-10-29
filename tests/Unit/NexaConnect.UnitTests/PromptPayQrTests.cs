using System.IO;
using System.Security.Cryptography;
using NexaConnect.Contracts.Media;
using NexaConnect.Contracts.Platform;
using NexaConnect.POS;
using NexaConnect.Services.Media.Application;
using NexaConnect.Services.Media.Infrastructure;
using NexaConnect.Services.Restaurant.Application.Branches;
using NexaConnect.Services.Restaurant.Application.Configuration;
using NexaConnect.Services.Restaurant.Domain;
using SkiaSharp;

namespace NexaConnect.UnitTests;

public sealed class PromptPayQrTests
{
    [Theory][InlineData("cash",true,false,false,null,false,true)][InlineData("cash",false,true,true,"ref",false,false)]
    [InlineData("promptpay_manual",false,false,true,"ref",false,false)][InlineData("promptpay_manual",false,true,false,"ref",false,false)]
    [InlineData("promptpay_manual",false,true,true," ",false,false)][InlineData("promptpay_manual",false,true,true,"ref",false,true)]
    [InlineData("promptpay_manual",false,false,true,"original",true,true)][InlineData("promptpay_manual",false,false,false,"original",true,false)]
    [InlineData("card_omise_test",true,true,true,"ref",false,false)]
    public void New_confirmation_requires_qr_and_proof_but_original_verification_does_not_require_image(string method,bool cash,bool qr,bool receipt,string? reference,bool original,bool expected)
        =>Assert.Equal(expected,ManualTenderReadiness.Evaluate(method,cash,qr,receipt,reference,original).Allowed);
    [Fact]public void References_are_bounded_before_new_or_original_submission()
    {Assert.False(ManualTenderReadiness.Evaluate("promptpay_manual",false,true,true,new string('x',65),false).Allowed);}
    [Fact]public void Verification_keeps_the_entire_original_request_despite_changed_ui_or_server_display()
    {
        var id=Guid.NewGuid();var key=Guid.NewGuid();var saved=new LocalPendingSettlementState(id,100,"THB",key,"promptpay_manual",true,"original-ref",true);
        var attempt=ManualTenderIntent.Prepare(saved,new(id,PosOrderStatus.KitchenAccepted,120,"THB"),key,"cash",false,"different-ref");
        Assert.Equal(saved,attempt);Assert.Throws<InvalidDataException>(()=>ManualTenderIntent.Prepare(saved,new(Guid.NewGuid(),PosOrderStatus.KitchenAccepted,100,"THB"),key,"cash",false,null));
        Assert.Throws<InvalidDataException>(()=>ManualTenderIntent.Prepare(saved,new(id,PosOrderStatus.KitchenAccepted,100,"THB"),Guid.NewGuid(),"cash",false,null));
    }
    [Fact]public async Task Approval_requires_live_configuration_authority_and_branch_owned_ready_asset()
    {
        var p=new Ports();var service=new BranchPromptPayQrService(p,p,p);var update=new UpdateBranchPromptPayQrV1(1,p.Asset,true);
        p.Granted=false;await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.SaveAsync(p.Org,p.Branch,update,"actor","Bearer fixture",default));Assert.Equal(0,p.Saves);
        p.Granted=true;p.AssetReady=false;await Assert.ThrowsAsync<ArgumentException>(()=>service.SaveAsync(p.Org,p.Branch,update,"actor","Bearer fixture",default));Assert.Equal(0,p.Saves);
        p.AssetReady=true;var saved=await service.SaveAsync(p.Org,p.Branch,update,"actor","Bearer fixture",default);
        Assert.Equal(p.Asset,saved!.AssetId);Assert.Equal(ProductPermissions.RestaurantConfigurationManage,p.Permission);Assert.Equal(p.Restaurant,p.AuthorizedRestaurant);Assert.Equal(1,p.Saves);
        await service.ReadAsync(p.Org,p.Branch,"Bearer fixture",default);Assert.Equal(ProductPermissions.CatalogMenuRead,p.Permission);
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>service.ReadAsync(Guid.NewGuid(),p.Branch,"Bearer fixture",default));
    }
    [Theory][InlineData(0,true)][InlineData(1,false)]public void Approval_is_explicit_and_versioned(long version,bool approved)
        =>Assert.Throws<ArgumentException>(()=>new PromptPayQrApproval(version,Guid.NewGuid(),approved));
    [Fact]public async Task Removal_is_an_explicit_scoped_configuration_write_not_a_media_delete()
    {
        var p=new Ports();var service=new BranchPromptPayQrService(p,p,p);
        var saved=await service.SaveAsync(p.Org,p.Branch,new(1,null,true),"actor","Bearer fixture",default);
        Assert.Null(saved!.AssetId);Assert.Equal(0,p.AssetChecks);Assert.Equal(1,p.Saves);
    }
    [Fact]public async Task Delivery_is_lossless_scoped_hashed_and_short_lived()
    {
        var p=new Ports();var bundle=await Delivery(p).DeliverAsync(p.Org,p.Branch,"Bearer fixture",default);
        Assert.Equal(TimeSpan.FromSeconds(60),bundle.ExpiresAtUtc-bundle.IssuedAtUtc);Assert.NotNull(bundle.Image);Assert.Equal(2,p.SelectionReads);
        PromptPayQrPolicy.Validate(bundle,p.Org,p.Restaurant,p.Branch,DateTimeOffset.UtcNow);
        using var source=SKBitmap.Decode(p.Png);using var result=SKBitmap.Decode(bundle.Image!.Png);
        Assert.Equal(source.Width,result.Width);Assert.Equal(source.Height,result.Height);Assert.Equal(source.GetPixel(0,0),result.GetPixel(0,0));Assert.Equal(source.GetPixel(64,64),result.GetPixel(64,64));
    }
    [Theory][InlineData("owner")][InlineData("branch")][InlineData("not-ready")][InlineData("type")]
    public async Task Wrong_or_unready_asset_never_reads_storage(string change)
    {
        var p=new Ports{InvalidAsset=change};Assert.Null(await Delivery(p).ReadyPngAsync(p.Org,p.Branch,p.Asset,default));Assert.Equal(0,p.StorageReads);
    }
    [Fact]public async Task Deletion_or_selection_change_during_read_cannot_renew_image()
    {
        var p=new Ports();p.AfterStorage=()=>p.Deleted=true;
        Assert.Null((await Delivery(p).DeliverAsync(p.Org,p.Branch,"Bearer fixture",default)).Image);
        p=new Ports();p.AfterStorage=()=>p.Version++;
        await Assert.ThrowsAsync<InvalidDataException>(()=>Delivery(p).DeliverAsync(p.Org,p.Branch,"Bearer fixture",default));
        p=new Ports();p.AfterStorage=()=>p.Granted=false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Delivery(p).DeliverAsync(p.Org,p.Branch,"Bearer fixture",default));
    }
    [Fact]public async Task Checksum_mismatch_and_wrong_scope_fail_closed()
    {
        var p=new Ports{WrongChecksum=true};await Assert.ThrowsAsync<InvalidDataException>(()=>Delivery(p).DeliverAsync(p.Org,p.Branch,"Bearer fixture",default));
        p=new Ports{WrongSelection=true};await Assert.ThrowsAsync<InvalidDataException>(()=>Delivery(p).DeliverAsync(p.Org,p.Branch,"Bearer fixture",default));Assert.Equal(0,p.StorageReads);
    }
    [Theory][InlineData("branch")][InlineData("organization")][InlineData("restaurant")][InlineData("expiry")][InlineData("hash")][InlineData("asset")]
    public async Task Pos_rejects_wrong_scope_expired_or_substituted_image(string change)
    {
        var p=new Ports();var bundle=await Delivery(p).DeliverAsync(p.Org,p.Branch,"Bearer fixture",default);
        bundle=change switch{"branch"=>bundle with{Selection=bundle.Selection with{BranchId=Guid.NewGuid()}},"organization"=>bundle with{Selection=bundle.Selection with{OrganizationId=Guid.NewGuid()}},"restaurant"=>bundle with{Selection=bundle.Selection with{RestaurantId=Guid.NewGuid()}},"expiry"=>bundle with{ExpiresAtUtc=DateTimeOffset.UtcNow.AddMinutes(-1)},"hash"=>bundle with{Image=bundle.Image! with{Sha256=new string('0',64)}},_=>bundle with{Image=bundle.Image! with{AssetId=Guid.NewGuid()}}};
        Assert.Throws<InvalidDataException>(()=>PromptPayQrPolicy.Validate(bundle,p.Org,p.Restaurant,p.Branch,DateTimeOffset.UtcNow));
    }
    [Theory][InlineData("image/svg+xml",100)][InlineData("image/jpeg",100)][InlineData("image/png",2_097_153)]
    public async Task Payment_upload_accepts_only_bounded_png(string type,long size)
    {
        var p=new Ports();var service=new MediaManagement(p,p,p,p,new(100_000_000,20));
        await Assert.ThrowsAsync<ArgumentException>(()=>new PromptPayQrUploads(p,service,p).StartAsync(p.Org,p.Branch,new("fixture.png",type,size,new string('a',64)),"actor","Bearer fixture",default));Assert.Equal(0,p.Uploads);
    }
    [Fact]public async Task Payment_upload_owner_is_checked_before_signed_upload()
    {
        var p=new Ports();var service=new MediaManagement(p,p,p,p,new(100_000_000,20));
        var uploads=new PromptPayQrUploads(p,service,p);
        await uploads.StartAsync(p.Org,p.Branch,new("fixture.png","image/png",p.Png.Length,new string('a',64)),"actor","Bearer fixture",default);Assert.Equal(1,p.Uploads);
        p.OwnerExists=false;await Assert.ThrowsAsync<KeyNotFoundException>(()=>uploads.StartAsync(p.Org,p.Branch,new("fixture.png","image/png",p.Png.Length,new string('a',64)),"actor","Bearer fixture",default));Assert.Equal(1,p.Uploads);
    }
    [Fact]public void Decoder_rejects_svg_disguised_content_and_oversized_dimensions()
    {
        var encoder=new PromptPayPngEncoder();Assert.Throws<InvalidDataException>(()=>encoder.Normalize("<svg/>"u8.ToArray()));
        using var bitmap=new SKBitmap(2049,64);using var image=SKImage.FromBitmap(bitmap);using var png=image.Encode(SKEncodedImageFormat.Png,100);
        Assert.Throws<InvalidDataException>(()=>encoder.Normalize(png.ToArray()));
    }
    [Fact]public void Animated_png_chunks_cannot_change_the_payee_between_preview_and_pos()
    {
        var png=FixturePng();var animatedChunk=new byte[20];System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(animatedChunk.AsSpan(0,4),8);"acTL"u8.CopyTo(animatedChunk.AsSpan(4,4));
        var bytes=png[..33].Concat(animatedChunk).Concat(png[33..]).ToArray();Assert.Throws<InvalidDataException>(()=>new PromptPayPngEncoder().Normalize(bytes));
    }
    [Fact]public async Task Generic_media_routes_cannot_start_or_complete_branch_payment_uploads()
    {
        var p=new Ports();var media=new MediaManagement(p,p,p,p,new(100_000_000,20));
        await Assert.ThrowsAsync<ArgumentException>(()=>media.StartAsync(p.Org,new("restaurant","promptpay_qr",p.Branch,"fixture.png","image/png",p.Png.Length,new string('a',64)),"actor",default));
        await Assert.ThrowsAsync<ArgumentException>(()=>media.CompleteAsync(p.Org,p.Asset,2,"actor",default));Assert.Equal(0,p.Uploads);Assert.Equal(0,p.StorageReads);
    }
    [Fact]public async Task Revoked_branch_management_blocks_upload_and_completion_but_not_public_menu_read()
    {
        var p=new Ports{ManageGranted=false};var media=new MediaManagement(p,p,p,p,new(100_000_000,20));var uploads=new PromptPayQrUploads(p,media,p);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>uploads.StartAsync(p.Org,p.Branch,new("fixture.png","image/png",p.Png.Length,new string('a',64)),"actor","Bearer fixture",default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>uploads.CompleteAsync(p.Org,p.Branch,p.Asset,2,"actor","Bearer fixture",default));
        Assert.Equal(0,p.Uploads);Assert.Equal(0,p.StorageReads);Assert.NotNull(await Delivery(p).DeliverAsync(p.Org,p.Branch,"Bearer fixture",default));
    }
    [Fact]public async Task Completion_cannot_take_another_branch_asset_even_with_management_access()
    {
        var p=new Ports{InvalidAsset="branch"};var uploads=new PromptPayQrUploads(p,new MediaManagement(p,p,p,p,new(100_000_000,20)),p);
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>uploads.CompleteAsync(p.Org,p.Branch,p.Asset,2,"actor","Bearer fixture",default));Assert.Equal(0,p.StorageReads);
    }
    public static byte[] FixturePng()
    {using var b=new SKBitmap(128,128);b.Erase(SKColors.White);using(var canvas=new SKCanvas(b))using(var paint=new SKPaint{Color=SKColors.Black})canvas.DrawRect(32,32,64,64,paint);using var image=SKImage.FromBitmap(b);using var png=image.Encode(SKEncodedImageFormat.Png,100);return png.ToArray();}
    private static PromptPayQrDelivery Delivery(Ports p)=>new(p,p,p,new PromptPayPngEncoder(),TimeProvider.System);
    private sealed class Ports:IBranchPromptPayQrRepository,IBranchCustomerAuthorizer,IPromptPayQrAssetValidator,IBranchPromptPayQrSelectionPort,IMediaManagementRepository,IMediaObjectStorage,IMediaOwnerValidator,IMediaContentSafety
    {
        public Guid Org=Guid.NewGuid(),Restaurant=Guid.NewGuid(),Branch=Guid.NewGuid(),Asset=Guid.NewGuid();public long Version=1;
        public bool Granted=true,ManageGranted=true,AssetReady=true,Deleted,WrongChecksum,WrongSelection,OwnerExists=true;public string? InvalidAsset,Permission;public Guid? AuthorizedRestaurant;
        public int Saves,AssetChecks,StorageReads,SelectionReads,Uploads;public Action? AfterStorage;public byte[] Png=FixturePng();
        public Task<BranchPromptPayQrV1?> GetAsync(Guid org,Guid branch,CancellationToken ct)=>Task.FromResult<BranchPromptPayQrV1?>(org==Org&&branch==Branch?new(Org,Restaurant,Branch,Version,Asset):null);
        public Task<BranchPromptPayQrV1?> SaveAsync(Guid org,Guid branch,PromptPayQrApproval approval,string actor,CancellationToken ct){Saves++;return Task.FromResult<BranchPromptPayQrV1?>(new(org,Restaurant,branch,++Version,approval.AssetId));}
        public Task<bool> IsGrantedAsync(Guid org,Guid? restaurant,Guid? branch,string permission,string bearer,CancellationToken ct){Permission=permission;AuthorizedRestaurant=restaurant;return Task.FromResult(Granted&&org==Org&&restaurant==Restaurant&&branch==Branch);}
        public Task<bool> ReadyAsync(Guid org,Guid branch,Guid asset,string bearer,CancellationToken ct){AssetChecks++;return Task.FromResult(AssetReady&&org==Org&&branch==Branch&&asset==Asset);}
        public Task<BranchPromptPayQrV1> ReadAsync(Guid org,Guid branch,string bearer,CancellationToken ct,bool manage=false){SelectionReads++;if(!Granted||manage&&!ManageGranted)throw new UnauthorizedAccessException();return Task.FromResult(new BranchPromptPayQrV1(WrongSelection?Guid.NewGuid():Org,Restaurant,Branch,Version,Asset));}
        public Task<(MediaAssetSummary Asset,string Key,string Checksum)?> FindAsync(Guid org,Guid id,CancellationToken ct)=>Task.FromResult<(MediaAssetSummary,string,string)?>(org!=Org||id!=Asset||Deleted?null:(Summary(),"fixture-original",WrongChecksum?new string('0',64):Convert.ToHexString(SHA256.HashData(Png))));
        private MediaAssetSummary Summary()=>new(Asset,InvalidAsset=="owner"?"catalog":"restaurant","promptpay_qr",InvalidAsset=="branch"?Guid.NewGuid():Branch,"fixture.png",InvalidAsset=="type"?"image/jpeg":"image/png",Png.Length,InvalidAsset=="not-ready"?"pending":"ready",DateTimeOffset.UtcNow,null,2);
        public Task<MediaAssetSummary> StartAsync(Guid org,Guid id,string key,StartMediaUploadCommand command,string actor,DateTimeOffset expires,MediaQuota quota,CancellationToken ct){Uploads++;return Task.FromResult(Summary());}
        public Task<string?> FindVariantKeyAsync(Guid org,Guid id,string name,CancellationToken ct)=>Task.FromResult<string?>(null);
        public Task<MediaAssetSummary?> CompleteAsync(Guid org,Guid id,long version,string actor,CancellationToken ct)=>Task.FromResult<MediaAssetSummary?>(Summary());
        public Task<MediaAssetSummary?> QuarantineAsync(Guid org,Guid id,long version,string actor,string category,CancellationToken ct)=>throw new NotSupportedException();
        public Task<MediaAssetSummary?> DeleteAsync(Guid org,Guid id,long version,string actor,CancellationToken ct)=>throw new NotSupportedException();
        public Task<bool> ExistsAsync(Guid org,string owner,string kind,Guid id,CancellationToken ct)=>Task.FromResult(OwnerExists&&org==Org&&id==Branch);
        public Task<byte[]> ReadAsync(string key,long maximum,CancellationToken ct){StorageReads++;AfterStorage?.Invoke();return Task.FromResult(Png);}
        public Task<string> CreateUploadUrlAsync(string key,string type,long size,string hash,TimeSpan lifetime,CancellationToken ct)=>Task.FromResult("https://storage.invalid/fixture");
        public Task<string> CreateDownloadUrlAsync(string key,TimeSpan lifetime,CancellationToken ct)=>throw new NotSupportedException();
        public Task<StoredObjectInfo?> InspectAsync(string key,CancellationToken ct)=>throw new NotSupportedException();
        public Task PutAsync(string key,byte[] bytes,string type,string hash,CancellationToken ct)=>throw new NotSupportedException();
        public Task DeleteAsync(string key,CancellationToken ct)=>throw new NotSupportedException();
        public Task<MediaSafetyResult> InspectAsync(byte[] bytes,string type,CancellationToken ct)=>Task.FromResult(new MediaSafetyResult(true,null));
    }
}
