using NexaConnect.Contracts.Catalog;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Catalog.Application.Menu;
using NexaConnect.Services.Catalog.Application.MenuPhotos;
using NexaConnect.Services.Catalog.Application.Tenant;
using NexaConnect.Services.Catalog.Domain.Photos;
using NexaConnect.Services.Catalog.Infrastructure;
using NexaConnect.Services.Media.Infrastructure;
using SkiaSharp;

namespace NexaConnect.UnitTests;
public sealed class MenuPhotoTests
{
    [Fact] public void Approval_versions_and_noop_are_explicit()
    {
        var asset=Guid.NewGuid();
        Assert.Throws<ArgumentException>(()=>MenuPhotoAssignment.Change(null,0,asset,0,false));
        Assert.Throws<ArgumentException>(()=>MenuPhotoAssignment.Change(null,0,Guid.Empty,0,true));
        Assert.Throws<MenuPhotoConflictException>(()=>MenuPhotoAssignment.Change(asset,2,asset,1,true));
        Assert.False(MenuPhotoAssignment.Change(asset,2,asset,2,true).Changed);
        Assert.Equal(new MenuPhotoChange(null,3,true),MenuPhotoAssignment.Change(asset,2,null,2,true));
        Assert.Throws<OverflowException>(()=>MenuPhotoAssignment.Change(null,long.MaxValue,asset,long.MaxValue,true));
    }
    [Fact] public async Task Assign_clear_reuse_and_scope_preserve_original_menu_prices()
    {
        var f=new Fixture();var original=f.Menu.GetForOrganizationBranch(f.Org,f.Branch).Single();var asset=Guid.NewGuid();
        var saved=await f.Save(asset,0);Assert.Equal(1,saved.Version);Assert.Equal(asset,saved.AssetId);
        Assert.Equal(original,f.Menu.GetForOrganizationBranch(f.Org,f.Branch).Single());
        Assert.Equal(1,(await f.Save(asset,1)).Version);
        Assert.Null((await f.Save(null,1)).AssetId);
        await Assert.ThrowsAsync<MenuPhotoConflictException>(()=>f.Save(asset,1));
        Assert.Null(await f.Repo.ReadAsync(Guid.NewGuid(),f.Branch,f.Product,default));
        Assert.Null(await f.Repo.ReadAsync(f.Org,Guid.NewGuid(),f.Product,default));
        Assert.Null(await f.Repo.ReadAsync(f.Org,f.Branch,Guid.NewGuid(),default));
    }
    [Theory][InlineData("write")][InlineData("media")][InlineData("revoke")][InlineData("unready")]
    public async Task Unready_assets_and_live_denials_never_save(string condition)
    {
        var f=new Fixture();f.Access.Write=condition!="write";f.Access.Media=condition!="media";
        if(condition=="revoke")f.Images.AfterRead=()=>f.Access.Write=false;
        if(condition=="unready")f.Images.Bytes=null;
        if(condition=="unready")await Assert.ThrowsAsync<ArgumentException>(()=>f.Save(Guid.NewGuid(),0));
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Save(Guid.NewGuid(),0));
        Assert.Equal(0,(await f.Repo.ReadAsync(f.Org,f.Branch,f.Product,default))!.Version);
    }
    [Fact] public async Task Cashier_can_read_only_the_selected_photo_without_general_media_access()
    {
        var f=new Fixture();var asset=Guid.NewGuid();await f.Save(asset,0);f.Access.Write=false;f.Access.Media=false;
        Assert.Equal(f.Images.Bytes,await f.App.DeliverAsync(f.Org,f.Branch,f.Product,"Bearer",default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.App.PreviewAsync(f.Org,f.Branch,f.Product,asset,"Bearer",default));
        f.Access.Read=false;await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.App.DeliverAsync(f.Org,f.Branch,f.Product,"Bearer",default));
    }
    [Fact] public async Task Changed_selection_or_revoked_read_during_delivery_is_not_returned()
    {
        var f=new Fixture();await f.Save(Guid.NewGuid(),0);
        f.Images.AfterRead=()=>f.Repo.ChangeAsync(f.Org,f.Branch,f.Product,c=>MenuPhotoAssignment.Change(c.AssetId,c.Version,null,c.Version,true),f.Actor,default).GetAwaiter().GetResult();
        Assert.Null(await f.App.DeliverAsync(f.Org,f.Branch,f.Product,"Bearer",default));
        f.Images.AfterRead=null;await f.Save(Guid.NewGuid(),2);f.Images.AfterRead=()=>f.Access.Read=false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.App.DeliverAsync(f.Org,f.Branch,f.Product,"Bearer",default));
    }
    [Fact] public async Task Delivery_rejects_oversized_dependency_bytes()
    {
        var f=new Fixture();await f.Save(Guid.NewGuid(),0);f.Images.Bytes=new byte[524289];
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.App.DeliverAsync(f.Org,f.Branch,f.Product,"Bearer",default));
    }
    [Fact] public void Encoder_rejects_bad_input_and_bounds_processed_pixels()
    {
        var encoder=new MenuPhotoEncoder();Assert.Throws<InvalidDataException>(()=>encoder.Encode([1,2,3]));
        Assert.Throws<InvalidDataException>(()=>encoder.Encode(new byte[2097153]));
        using var bitmap=new SKBitmap(1280,640);bitmap.Erase(SKColors.Teal);using var image=SKImage.FromBitmap(bitmap);using var source=image.Encode(SKEncodedImageFormat.Png,100);
        var png=encoder.Encode(source.ToArray());using var decoded=SKBitmap.Decode(png);
        Assert.Equal(512,decoded.Width);Assert.Equal(256,decoded.Height);Assert.InRange(png.Length,1,524288);
        using var large=new SKBitmap(1281,1);using var largeImage=SKImage.FromBitmap(large);using var largePng=largeImage.Encode(SKEncodedImageFormat.Png,100);
        Assert.Throws<InvalidDataException>(()=>encoder.Encode(largePng.ToArray()));
    }
    private sealed class Fixture
    {
        public Guid Org=Guid.NewGuid(),Branch=Guid.NewGuid(),Product=Guid.NewGuid();public InMemoryMenuCatalog Menu=new();
        public InMemoryMenuPhotoRepository Repo;public Access Access=new();public Images Images=new();public MenuPhotos App;public MenuMutationContext Actor=new("manager",Guid.NewGuid());
        public Fixture(){Menu.AddForOrganizationBranch(Org,Branch,new(Product,"Meal",107,"THB","kitchen"));Repo=new(Menu);App=new(Repo,Images,Access);}
        public Task<MenuPhotoSelection> Save(Guid? asset,long version)=>App.SetAsync(Org,Branch,Product,new(asset,version,true),"Bearer",Actor,default);
    }
    private sealed class Access:ICatalogTenantAuthorizer
    {
        public bool Read=true,Write=true,Media=true;
        public Task<bool> HasAccessAsync(Guid o,string p,string b,CancellationToken ct)=>Task.FromResult(p==ProductPermissions.MediaAssetRead&&Media);
        public Task<bool> HasBranchAccessAsync(Guid o,Guid branch,string p,string b,CancellationToken ct)=>Task.FromResult(p==ProductPermissions.CatalogMenuWrite?Write:Read);
    }
    private sealed class Images:IMenuPhotoAssetReader
    {public byte[]? Bytes=[1,2,3];public Action? AfterRead;public Task<byte[]?> ReadAsync(Guid o,Guid a,CancellationToken ct){AfterRead?.Invoke();return Task.FromResult(Bytes);}}
}
