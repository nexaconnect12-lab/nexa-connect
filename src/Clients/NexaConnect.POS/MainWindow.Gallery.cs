using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using NexaConnect.Contracts.Media;

namespace NexaConnect.POS;

public partial class MainWindow
{
    private GalleryBundle? galleryBundle;
    private Dictionary<Guid,BitmapSource> galleryImages=[];
    private PosGalleryCache? galleryCache;
    private readonly SemaphoreSlim galleryCacheGate=new(1,1);
    private readonly System.Windows.Threading.DispatcherTimer galleryTimer=new(){Interval=TimeSpan.FromSeconds(1)};
    private long galleryGeneration;
    private bool galleryReading;
    private DateTimeOffset nextGalleryRefresh=DateTimeOffset.MinValue;

    private void InitializeGallery()
    {
        if(string.IsNullOrWhiteSpace(_configuration.MediaApi))return;
        galleryCache=new(_configuration.OrganizationId,_configuration.BranchId);
        galleryTimer.Tick+=GalleryTick;galleryTimer.Start();_ = LoadGalleryCacheAsync();
    }
    private async Task LoadGalleryCacheAsync()
    {
        long generation=galleryGeneration;
        try{
            var result=await Task.Run(()=>{var cached=galleryCache!.Load(DateTimeOffset.UtcNow);return cached is null?null:new GalleryDecoded(cached,DecodeGallery(cached));});
            if(result is not null&&generation==galleryGeneration&&!windowCancellation.IsCancellationRequested
                &&(galleryBundle is null||galleryBundle.IssuedAtUtc<=result.Bundle.IssuedAtUtc))
            {galleryBundle=result.Bundle;galleryImages=result.Images;RenderGallery();}
        }catch{ /* Unusable cache stays neutral. */ }
    }
    private sealed record GalleryDecoded(GalleryBundle Bundle,Dictionary<Guid,BitmapSource> Images);
    private static Dictionary<Guid,BitmapSource> DecodeGallery(GalleryBundle bundle)
    {
        var decoded=new Dictionary<Guid,BitmapSource>();
        foreach(var image in bundle.Images){
            using var stream=new MemoryStream(image.Png,false);
            var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
            if(decoder.Frames.Count!=1||decoder.Frames[0].PixelWidth is <1 or >1280||decoder.Frames[0].PixelHeight is <1 or >1280)throw new InvalidDataException();
            var bitmap=decoder.Frames[0];bitmap.Freeze();decoded.Add(image.AssetId,bitmap);
        }return decoded;
    }
    private async void GalleryTick(object? sender,EventArgs e)
    {
        RenderGallery();
        if(galleryReading||!IsSignedIn()||DateTimeOffset.UtcNow<nextGalleryRefresh)return;
        galleryReading=true;nextGalleryRefresh=DateTimeOffset.UtcNow.AddMinutes(1);
        long generation=galleryGeneration;var token=_authentication.CurrentToken!;
        try{
            var bundle=await _api.GalleryAsync(token,windowCancellation.Token);
            if(!PosGalleryPolicy.Valid(bundle,_configuration.OrganizationId,_configuration.BranchId,DateTimeOffset.UtcNow))throw new InvalidDataException();
            Dictionary<Guid,BitmapSource> images;
            try{images=await Task.Run(()=>DecodeGallery(bundle));}
            catch{ClearGallery();return;}
            if(generation!=galleryGeneration||windowCancellation.IsCancellationRequested||token.Subject!=_authentication.CurrentToken?.Subject)return;
            galleryBundle=bundle;galleryImages=images;RenderGallery();
            await Task.Run(async()=>{await galleryCacheGate.WaitAsync();try{if(generation==Interlocked.Read(ref galleryGeneration))galleryCache!.Save(bundle,DateTimeOffset.UtcNow);}finally{galleryCacheGate.Release();}});
        }
        catch(PosApiException ex)when(ex.StatusCode is 401 or 403 or 404){ClearGallery();}
        catch(InvalidDataException){ClearGallery();}
        catch{ /* Only an unexpired previously validated lease survives an outage. */ }
        finally{galleryReading=false;}
    }
    private void ClearGallery()
    {
        Interlocked.Increment(ref galleryGeneration);galleryBundle=null;galleryImages=[];RenderGallery();
        if(galleryCache is not null)_=Task.Run(async()=>{await galleryCacheGate.WaitAsync();try{galleryCache.Clear();}catch{}finally{galleryCacheGate.Release();}});
    }
    private void RenderGallery()
    {
      try {
        var now=DateTimeOffset.UtcNow;
        var staff=reauthenticationRequired&&!displaySafetyBlocked?PosGalleryPolicy.Slide(galleryBundle,false,now):null;
        StaffGalleryImage.Source=staff is not null&&galleryImages.TryGetValue(staff.AssetId,out var image)?image:null;
        StaffGalleryTitle.Text=staff?.Title??"";StaffGalleryCaption.Text=staff?.Caption??"";
        StaffGalleryColumn.Width=StaffGalleryImage.Source is null?new GridLength(0):new GridLength(1,GridUnitType.Star);
        var customer=reauthenticationRequired&&!displaySafetyBlocked?PosGalleryPolicy.Slide(galleryBundle,true,now):null;
        displays?.Screensaver(customer is not null&&galleryImages.TryGetValue(customer.AssetId,out var picture)?picture:null,customer?.Title??"",customer?.Caption??"",reauthenticationRequired&&!displaySafetyBlocked);
      }catch{ /* Media presentation cannot block lock/resume or financial recovery. */ }
    }
}
