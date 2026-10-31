using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace NexaConnect.POS;

public sealed class MenuTile(PosMenuItem item):INotifyPropertyChanged
{
    public PosMenuItem Item{get;}=item;
    public Guid? CategoryId{get;set;}
    private BitmapSource? photo;
    public BitmapSource? Photo {get=>photo;set{photo=value;PropertyChanged?.Invoke(this,new(nameof(Photo)));}}
    public event PropertyChangedEventHandler? PropertyChanged;
}
public partial class MainWindow
{
    private readonly ObservableCollection<MenuTile> menuTiles=[];
    private CancellationTokenSource? menuPhotoCancellation;
    private long menuPhotoGeneration;
    private DateTimeOffset menuPhotoExpires;
    private bool menuPhotoRequested,menuPhotoLoading;
    private readonly DispatcherTimer menuPhotoTimer=new(){Interval=TimeSpan.FromSeconds(5)};
    private void InitializeMenuPhotos()
    {
        menu.CollectionChanged+=MenuPhotosChanged;MenuList.ItemsSource=menuTiles;
        InitializeMenuOrganization();
        menuPhotoTimer.Tick+=MenuPhotoTick;menuPhotoTimer.Start();
    }
    private void MenuPhotosChanged(object? sender,NotifyCollectionChangedEventArgs e)
    {
        ClearMenuPhotos();menuTiles.Clear();foreach(var item in menu)menuTiles.Add(new(item));
        ClearMenuOrganization();
    }
    private void ClearMenuPhotos()
    {
        menuPhotoGeneration++;menuPhotoCancellation?.Cancel();menuPhotoCancellation?.Dispose();menuPhotoCancellation=null;menuPhotoLoading=false;
        foreach(var tile in menuTiles)tile.Photo=null;menuPhotoExpires=DateTimeOffset.MinValue;
    }
    private void MenuPhotoTick(object? sender,EventArgs e)
    {
        if(DateTimeOffset.UtcNow>=menuPhotoExpires)foreach(var tile in menuTiles)tile.Photo=null;
        if(menuPhotoRequested&&!menuPhotoLoading&&menu.Count>0&&CheckoutTab.IsSelected&&IsActive&&IsSignedIn()&&!busy&&DateTimeOffset.UtcNow>=menuPhotoExpires)_=LoadMenuPhotosAsync();
    }
    private async Task LoadMenuPhotosAsync()
    {
        if(!IsSignedIn()||_authentication.CurrentToken is not {} token)return;
        ClearMenuPhotos();menuPhotoLoading=true;var generation=menuPhotoGeneration;var privacy=receiptSessionGeneration;var subject=token.Subject;
        var cancellation=CancellationTokenSource.CreateLinkedTokenSource(windowCancellation.Token);menuPhotoCancellation=cancellation;var ct=cancellation.Token;
        menuPhotoExpires=DateTimeOffset.UtcNow.AddMinutes(1);
        using var slots=new SemaphoreSlim(4);
        try{
            await Task.WhenAll(menuTiles.Take(200).Select(async tile=>{
                await slots.WaitAsync(ct);
                try{
                    if(generation!=menuPhotoGeneration||!PrintSessionCurrent(privacy,subject)||DateTimeOffset.UtcNow>=menuPhotoExpires)return;
                    var png=await _api.GetMenuPhotoAsync(token,tile.Item.ProductId,ct);
                    if(png is null||generation!=menuPhotoGeneration||!PrintSessionCurrent(privacy,subject))return;
                    if(png.Length is <24 or >524288||!png.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})
                        ||BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16,4)) is <1 or >512||BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20,4)) is <1 or >512)throw new InvalidDataException();
                    using var stream=new MemoryStream(png);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=192;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();
                    if(generation==menuPhotoGeneration&&PrintSessionCurrent(privacy,subject)&&DateTimeOffset.UtcNow<menuPhotoExpires)tile.Photo=bitmap;
                }catch(Exception e)when(e is PosApiException or System.Net.Http.HttpRequestException or InvalidDataException or IOException or ArgumentException or NotSupportedException or InvalidOperationException or OperationCanceledException or FormatException){/* Optional image failures never change menu/cart or payment. */}
                finally{slots.Release();}
            }));
        }catch(OperationCanceledException){}
        finally{if(generation==menuPhotoGeneration)menuPhotoLoading=false;}
    }
}
