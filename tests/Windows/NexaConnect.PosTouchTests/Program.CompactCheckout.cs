using System.Collections;
using System.Windows;
using System.Windows.Controls;
using NexaConnect.POS;

internal static partial class Program
{
    private static void VerifyCompactCheckout(MainWindow window)
    {
        var menu=(IList)typeof(MainWindow).GetField("menu",Private)!.GetValue(window)!;
        var shift=typeof(MainWindow).GetField("_activeShift",Private)!;
        var cash=typeof(MainWindow).GetField("cashSessionId",Private)!;
        object? oldShift=shift.GetValue(window),oldCash=cash.GetValue(window);
        var more=(Expander)window.FindName("MenuOptionsExpander");
        var density=(ComboBox)window.FindName("DensityPicker");int oldDensity=density.SelectedIndex;
        try
        {
            shift.SetValue(window,new LocalShiftState(Guid.NewGuid(),"LAYOUT",DateTimeOffset.UtcNow));cash.SetValue(window,Guid.NewGuid());
            for(int i=0;i<24;i++)menu.Add(new PosMenuItem(Guid.NewGuid(),$"Fixture meal {i+1}",65,"THB","kitchen",true));
            ((TabItem)window.FindName("CheckoutTab")).IsSelected=true;
            Invoke(window,"UpdateOperationalState");
            Require(!more.IsExpanded,"secondary menu controls default collapsed");
            Require(((FrameworkElement)window.FindName("WorkspaceOptionsPanel")).Visibility==Visibility.Collapsed,"workspace diagnostics default collapsed");
            foreach(int choice in new[]{0,1})
            foreach(var size in new[]{new Size(1093,614),new Size(1366,768),new Size(1920,1080)})
            {
                density.SelectedIndex=choice;window.Width=size.Width;window.Height=size.Height;Pump(window);
                Require(((FrameworkElement)more.Header).ActualHeight>=PosDensityMetrics.For((PosDisplayDensity)choice).ControlHeight,"secondary menu toggle retains the selected input target size");
                var menuList=(ListBox)window.FindName("MenuList");var viewport=(ScrollViewer)window.FindName("InputViewport");
                Require(menuList.ActualHeight>=230,$"menu retains browsing height at {size}, density {choice}");
                Require(viewport.ScrollableHeight<2,"ordinary checkout does not require whole-page scrolling");
                Require(menuList.ActualWidth>=window.CheckoutCartWidthForFixture(),"menu is wider than bounded cart");
                if(choice==0)
                {
                    var tiles=Descendants<Button>(menuList).Where(b=>b.Style==window.FindResource("MenuTile")).ToArray();
                    var positions=tiles.Select(b=>b.TransformToAncestor(menuList).Transform(new Point())).ToArray();
                    double first=positions.Min(p=>p.Y);
                    Require(positions.Count(p=>Math.Abs(p.Y-first)<1)>=3,"standard menu fits at least three complete columns without wasted wrap space");
                }
                Require(((FrameworkElement)window.FindName("CashierReadyPanel")).Visibility==Visibility.Collapsed,"checkout omits the duplicate ready banner");
                Require(!((FrameworkElement)window.FindName("TotalText")).IsVisible&&((FrameworkElement)window.FindName("WorkflowAmount")).IsVisible,"one visible pinned total");
                var button=(Button)window.FindName("PlaceOrderButton");var root=(FrameworkElement)window.Content;
                var bounds=button.TransformToAncestor(root).TransformBounds(new Rect(button.RenderSize));
                Require(bounds.Top>=0&&bounds.Bottom<=root.ActualHeight+1,"checkout action remains on screen");
                typeof(MainWindow).GetField("settlementUncertain",Private)!.SetValue(window,true);
                typeof(MainWindow).GetField("pendingOrder",Private)!.SetValue(window,new PosOrderResult(Guid.NewGuid(),PosOrderStatus.PaymentPending,65,"THB"));
                Invoke(window,"RenderWorkflow");
                Require(((TextBlock)window.FindName("WorkflowDescription")).IsVisible,"payment uncertainty guidance stays visible even at short heights");
                typeof(MainWindow).GetField("pendingOrder",Private)!.SetValue(window,null);
                typeof(MainWindow).GetField("settlementUncertain",Private)!.SetValue(window,false);Invoke(window,"RenderWorkflow");
                if(choice==0)SavePreview(window,$".runstate/pos-compact-checkout-{(int)size.Width}.png");
            }
            Invoke(window,"WorkspaceOptions_Click",window,new RoutedEventArgs());Pump(window);
            Require(((Button)window.FindName("SignOutButton")).IsVisible,"sign-out remains reachable in workspace options");
            Invoke(window,"WorkspaceOptions_Click",window,new RoutedEventArgs());
            more.IsExpanded=true;Pump(window);Require(((ComboBox)window.FindName("StationComboBox")).IsVisible,"station/scanner tools remain reachable");
            more.IsExpanded=false;Pump(window);
            Console.WriteLine("PASS: compact checkout — scaled logical sizes, both densities, menu space, pinned total/action, reachable options and visible uncertainty guidance.");
        }
        finally
        {
            menu.Clear();shift.SetValue(window,oldShift);cash.SetValue(window,oldCash);density.SelectedIndex=oldDensity;
            Invoke(window,"UpdateOperationalState");window.Width=1280;window.Height=700;
        }
    }
    private static double CheckoutCartWidthForFixture(this MainWindow window)
        => ((Grid)window.FindName("CheckoutLayout")).ColumnDefinitions[1].ActualWidth;
}
