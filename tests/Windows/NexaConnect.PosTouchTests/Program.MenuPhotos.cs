using System.Collections;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexaConnect.POS;
internal static partial class Program
{
    private static void VerifyMenuPhotoPresentation(MainWindow window)
    {
        var menu=(IList)typeof(MainWindow).GetField("menu",Private)!.GetValue(window)!;
        var list=(ListBox)window.FindName("MenuList");var search=(TextBox)window.FindName("MenuSearchTextBox");
        var original=new PosMenuItem(Guid.NewGuid(),"Photo fixture meal",107m,"THB","kitchen",true);
        menu.Add(original);Pump(window);var tile=(MenuTile)list.Items[0];Require(ReferenceEquals(original,tile.Item),"photo wrapper preserves original pricing item identity");
        var bitmap=BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,new byte[]{0,128,128,255},4);bitmap.Freeze();tile.Photo=bitmap;Pump(window);
        Require(tile.Photo==bitmap&&tile.Item.UnitPrice==107m,"setting photo leaves authoritative menu price unchanged");
        search.Text="unmatched";Pump(window);Require(list.Items.Count==0,"menu search filters photo wrappers");search.Clear();Pump(window);
        Require(list.Items.Count==1,"clearing search retains original menu item");
        foreach(int status in new[]{401,403,429,503,200}){
            NoNetworkHandler.PhotoStatus=status;int calls=NoNetworkHandler.PhotoCalls;
            var task=(Task)typeof(MainWindow).GetMethod("LoadMenuPhotosAsync",Private)!.Invoke(window,null)!;
            var deadline=DateTime.UtcNow.AddSeconds(5);while(!task.IsCompleted&&DateTime.UtcNow<deadline)Pump(window);
            Require(NoNetworkHandler.PhotoCalls==calls+1&&task.IsCompletedSuccessfully&&tile.Photo is null&&ReferenceEquals(tile.Item,original),"denied/unavailable photo is absorbed without changing the original item or faulting its background task");
        }
        NoNetworkHandler.PhotoStatus=503;
        Invoke(window,"ClearReceiptView");Require(tile.Photo is null,"privacy transition clears displayed menu photo");
        menu.Clear();menu.Add(original);Pump(window);Require(((MenuTile)list.Items[0]).Photo is null,"reloaded menu does not inherit stale photo");
        menu.Clear();Pump(window);
    }
}
