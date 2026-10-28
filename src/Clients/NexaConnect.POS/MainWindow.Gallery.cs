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
    private PosGalleryReaderCache? galleryReaderCache;
    private GalleryReaderCredential? galleryReader;
    private string? galleryReaderSubject;
    private DateTimeOffset galleryRotationEpoch;
    private readonly SemaphoreSlim galleryCacheGate=new(1,1);
    private readonly System.Windows.Threading.DispatcherTimer galleryTimer=new(){Interval=TimeSpan.FromSeconds(1)};
    private long galleryGeneration;
    private bool galleryReading;
    private DateTimeOffset nextGalleryRefresh=DateTimeOffset.MinValue;

    private void InitializeGallery()
    {
        if(string.IsNullOrWhiteSpace(_configuration.MediaApi))return;
        galleryCache=new(_configuration.OrganizationId,_configuration.BranchId);
        galleryReaderCache=new(_configuration);
        galleryTimer.Tick+=GalleryTick;galleryTimer.Start();_ = LoadGalleryCacheAsync();
    }
    private async Task LoadGalleryCacheAsync()
    {
        long generation=galleryGeneration;
        try{
            var result=await Task.Run(()=>{var cached=galleryCache!.Load(DateTimeOffset.UtcNow);return cached is null?null:new GalleryDecoded(cached,DecodeGallery(cached));});
            if(result is not null&&generation==galleryGeneration&&!windowCancellation.IsCancellationRequested
                &&(galleryBundle is null||galleryBundle.IssuedAtUtc<=result.Bundle.IssuedAtUtc))
            {galleryBundle=result.Bundle;galleryRotationEpoch=result.Bundle.IssuedAtUtc;galleryImages=result.Images;RenderGallery();}
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
        if(galleryReading||displaySafetyBlocked||!_authentication.HasVerifiedOperator||DateTimeOffset.UtcNow<nextGalleryRefresh)return;
        galleryReading=true;nextGalleryRefresh=DateTimeOffset.UtcNow.AddMinutes(1);
        long generation=galleryGeneration;var token=_authentication.CurrentToken!;
        try{
            if(galleryReaderSubject!=token.Subject){
                var loaded=await Task.Run(()=>galleryReaderCache!.Load(token.Subject!,DateTimeOffset.UtcNow));
                if(generation!=galleryGeneration||windowCancellation.IsCancellationRequested||token.Subject!=_authentication.CurrentToken?.Subject)return;
                galleryReader=loaded;
                galleryReaderSubject=token.Subject;
            }
            if(generation!=galleryGeneration||windowCancellation.IsCancellationRequested||token.Subject!=_authentication.CurrentToken?.Subject)return;
            if(IsSignedIn() && (!PosGalleryReaderCache.Valid(galleryReader,_configuration.OrganizationId,_configuration.BranchId,DateTimeOffset.UtcNow)
                ||galleryReader!.ExpiresAtUtc<=DateTimeOffset.UtcNow.AddHours(1)))
            {
                var issued=await _api.CreateGalleryReaderAsync(token,windowCancellation.Token);
                if(!PosGalleryReaderCache.Valid(issued,_configuration.OrganizationId,_configuration.BranchId,DateTimeOffset.UtcNow))throw new InvalidDataException();
                if(generation!=galleryGeneration||windowCancellation.IsCancellationRequested||token.Subject!=_authentication.CurrentToken?.Subject)return;
                galleryReader=issued;
                await Task.Run(async()=>{await galleryCacheGate.WaitAsync();try{if(generation==Interlocked.Read(ref galleryGeneration))galleryReaderCache!.Save(issued,token.Subject!,DateTimeOffset.UtcNow);}finally{galleryCacheGate.Release();}});
            }
            if(!PosGalleryReaderCache.Valid(galleryReader,_configuration.OrganizationId,_configuration.BranchId,DateTimeOffset.UtcNow))return;
            var bundle=await _api.ReadGalleryAsync(galleryReader!.Credential,windowCancellation.Token);
            if(!PosGalleryPolicy.Valid(bundle,_configuration.OrganizationId,_configuration.BranchId,DateTimeOffset.UtcNow))throw new InvalidDataException();
            Dictionary<Guid,BitmapSource> images;
            try{images=await Task.Run(()=>DecodeGallery(bundle));}
            catch{if(generation==galleryGeneration)ClearGallery();return;}
            if(generation!=galleryGeneration||windowCancellation.IsCancellationRequested||token.Subject!=_authentication.CurrentToken?.Subject)return;
            if(!PosGalleryPolicy.SamePlaylist(galleryBundle?.Selection,bundle.Selection))galleryRotationEpoch=bundle.IssuedAtUtc;
            galleryBundle=bundle;galleryImages=images;RenderGallery();
            await Task.Run(async()=>{await galleryCacheGate.WaitAsync();try{if(generation==Interlocked.Read(ref galleryGeneration))galleryCache!.Save(bundle,DateTimeOffset.UtcNow);}finally{galleryCacheGate.Release();}});
        }
        catch(PosApiException ex)when(ex.StatusCode is 401 or 403 or 404){if(generation==galleryGeneration)ClearGallery();}
        catch(InvalidDataException){if(generation==galleryGeneration)ClearGallery();}
        catch{ /* Only an unexpired previously validated lease survives an outage. */ }
        finally{galleryReading=false;}
    }
    private void ClearGallery()
    {
        Interlocked.Increment(ref galleryGeneration);galleryBundle=null;galleryReader=null;galleryReaderSubject=null;galleryImages=[];RenderGallery();
        if(galleryCache is not null){var pending=galleryCacheGate.WaitAsync();_=Task.Run(async()=>{await pending;try{galleryCache.Clear();galleryReaderCache?.Clear();}catch{}finally{galleryCacheGate.Release();}});}
    }
    private void RenderGallery()
    {
      try {
        var now=DateTimeOffset.UtcNow;
        if(IsLoaded)displays?.StaffScreensaver(reauthenticationRequired&&!displaySafetyBlocked);
        var staff=reauthenticationRequired&&!displaySafetyBlocked?PosGalleryPolicy.Slide(galleryBundle,false,now,galleryRotationEpoch):null;
        StaffGalleryImage.Source=staff is not null&&galleryImages.TryGetValue(staff.AssetId,out var image)?image:null;
        StaffGalleryTitle.Text=staff?.Title??"";StaffGalleryCaption.Text=staff?.Caption??"";
        StaffGalleryTitle.Visibility=string.IsNullOrWhiteSpace(staff?.Title)?Visibility.Collapsed:Visibility.Visible;
        StaffGalleryCaption.Visibility=string.IsNullOrWhiteSpace(staff?.Caption)?Visibility.Collapsed:Visibility.Visible;
        RenderLockPrompt();
        var customer=reauthenticationRequired&&!displaySafetyBlocked?PosGalleryPolicy.Slide(galleryBundle,true,now,galleryRotationEpoch):null;
        displays?.Screensaver(customer is not null&&galleryImages.TryGetValue(customer.AssetId,out var picture)?picture:null,customer?.Title??"",customer?.Caption??"",reauthenticationRequired&&!displaySafetyBlocked);
      }catch{ /* Media presentation cannot block lock/resume or financial recovery. */ }
    }
}
