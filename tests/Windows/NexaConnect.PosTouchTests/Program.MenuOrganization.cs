using System.Collections;
using System.Windows.Controls;
using NexaConnect.Contracts.Catalog;
using NexaConnect.POS;
internal static partial class Program
{
    private static void VerifyMenuOrganization(MainWindow window,PosClientConfiguration config)
    {
        var menu=(IList)typeof(MainWindow).GetField("menu",Private)!.GetValue(window)!;
        var cart=(IList)typeof(MainWindow).GetField("cart",Private)!.GetValue(window)!;
        var list=(ListBox)window.FindName("MenuList");var picker=(ComboBox)window.FindName("CategoryComboBox");var search=(TextBox)window.FindName("MenuSearchTextBox");
        var a=new PosMenuItem(Guid.NewGuid(),"Organization Tea",5,"THB","bar",true);var b=new PosMenuItem(Guid.NewGuid(),"Organization Meal",107,"THB","kitchen",true);
        var category=new MenuCategory(Guid.NewGuid(),"Food");menu.Add(a);menu.Add(b);Pump(window);
        var layout=new BranchMenuOrganization(config.OrganizationId,config.BranchId,1,[category],[new(b.ProductId,category.Id),new(a.ProductId,null)]);
        int cartCount=cart.Count;Invoke(window,"ApplyMenuOrganization",layout);Pump(window);
        Require(ReferenceEquals(((MenuTile)list.Items[0]).Item,b),"published category order preserves original menu object identity");
        picker.SelectedIndex=1;Pump(window);Require(list.Items.Count==1&&ReferenceEquals(((MenuTile)list.Items[0]).Item,b),"category filters only the displayed menu");
        search.Text="Tea";Pump(window);Require(list.Items.Count==0,"text and category filters compose");search.Clear();picker.SelectedIndex=2;Pump(window);
        Require(list.Items.Count==1&&ReferenceEquals(((MenuTile)list.Items[0]).Item,a),"Uncategorized retains original unassigned menu items");
        Require(cart.Count==cartCount&&b.UnitPrice==107m,"category navigation does not mutate cart or financial prices");
        Invoke(window,"ClearReceiptView");Pump(window);Require(picker.SelectedIndex==0&&picker.Items.Count==2,"privacy clearing removes category labels and resets filter");
        var task=(Task)typeof(MainWindow).GetMethod("LoadMenuOrganizationAsync",Private)!.Invoke(window,null)!;var until=DateTime.UtcNow.AddSeconds(5);while(!task.IsCompleted&&DateTime.UtcNow<until)Pump(window);
        Require(task.IsCompletedSuccessfully&&list.Items.Count==2,"invalid or unavailable optional category response leaves all items usable");
        menu.Clear();Pump(window);
    }
}
