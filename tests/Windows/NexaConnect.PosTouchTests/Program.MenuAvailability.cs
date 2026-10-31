using System.Collections;
using System.Windows;
using System.Windows.Controls;
using NexaConnect.POS;
internal static partial class Program
{
    private static void VerifyMenuAvailability(MainWindow window)
    {
        var menu=(IList)typeof(MainWindow).GetField("menu",Private)!.GetValue(window)!;
        var cart=(IList)typeof(MainWindow).GetField("cart",Private)!.GetValue(window)!;
        var shiftField=typeof(MainWindow).GetField("_activeShift",Private)!;var previous=shiftField.GetValue(window);
        var add=typeof(MainWindow).GetMethod("TryAddMenuItem",Private)!;
        var available=new PosMenuItem(Guid.NewGuid(),"Availability fixture",107,"THB","kitchen",true);
        try
        {
            shiftField.SetValue(window,new LocalShiftState(Guid.NewGuid(),"AVAILABILITY-FIXTURE",DateTimeOffset.UtcNow));
            Invoke(window,"UpdateOperationalState");
            Require((bool)add.Invoke(window,[available])!,"available item can enter the temporary cart");int count=cart.Count;
            var sold=available with {Available=false};menu.Add(sold);Pump(window);
            var list=(ListBox)window.FindName("MenuList");
            var button=Descendants<Button>(list).Single(b=>b.DataContext is MenuTile tile&&tile.Item.ProductId==sold.ProductId);
            Require(!button.IsEnabled&&Descendants<TextBlock>(button).Any(t=>t.Text=="Sold out"&&t.Visibility==Visibility.Visible),"sold-out menu tile is labelled and disabled");
            Require(!(bool)add.Invoke(window,[sold])!&&cart.Count==count,"unavailable item cannot add quantities or discard original cart");
        }
        finally{cart.Clear();menu.Clear();shiftField.SetValue(window,previous);Invoke(window,"UpdateOperationalState");Pump(window);}
    }
}
