using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexaConnect.POS;

internal static partial class Program
{
    private static void VerifySessionDisplayLayout(MainWindow staff)
    {
        Invoke(staff, "RenderWorkflow");
        Require(((FrameworkElement)staff.FindName("CashierReadyPanel")).Visibility == Visibility.Visible, "ready cashier card without a transaction");
        Require(((TextBlock)staff.FindName("CashierReadyIdentity")).Text.Contains("fixture"), "ready card identifies verified cashier");
        ((Button)staff.FindName("StartSaleButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(((TabItem)staff.FindName("ShiftCashTab")).IsSelected, "start sale navigates to shift setup without opening a shift");
        typeof(MainWindow).GetField("busy", Private)!.SetValue(staff, true); Invoke(staff, "RenderWorkflow");
        Require(((FrameworkElement)staff.FindName("CashierReadyPanel")).Visibility == Visibility.Collapsed, "busy operation hides ready card");
        typeof(MainWindow).GetField("busy", Private)!.SetValue(staff, false); Invoke(staff, "RenderWorkflow");
        var orderField=typeof(MainWindow).GetField("pendingOrder",Private)!;var originalOrder=orderField.GetValue(staff);
        orderField.SetValue(staff,new PosOrderResult(Guid.NewGuid(),PosOrderStatus.PaymentPending,80,"THB"));Invoke(staff,"RenderWorkflow");
        Require(((FrameworkElement)staff.FindName("CashierReadyPanel")).Visibility==Visibility.Collapsed,"pending transaction hides idle ready card");
        orderField.SetValue(staff,originalOrder);Invoke(staff,"RenderWorkflow");

        var customer = new CustomerDisplayWindow();
        try
        {
            var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
                new byte[] {0,140,240,255,0,140,240,255,0,140,240,255,0,140,240,255}, 8);
            bitmap.Freeze();
            foreach (var size in new[] { new Size(1366,768), new Size(1920,1080), new Size(800,600) })
            {
                var root = (Grid)customer.Content; root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                Require(Math.Abs(root.ColumnDefinitions[0].ActualWidth-root.ColumnDefinitions[2].ActualWidth) < 1, "equal customer halves at each logical size");
                Require(root.ColumnDefinitions[1].ActualWidth == 2, "visible customer divider");
                customer.Present(CustomerDisplaySnapshot.Welcome with {ShopName="Fixture shop"});
                customer.Screensaver(bitmap,"Fixture advertisement","Approved public caption",false);
                Require(((Image)customer.FindName("SaverImage")).Source == bitmap, "advertisement runs while cashier active");
                Require(((FrameworkElement)customer.FindName("TransactionView")).Visibility == Visibility.Visible, "advertisement never replaces right-hand transaction view");
                var state = new CustomerDisplayState(); state.Cart("THB",[new("Synthetic meal",2,80)]);
                customer.Present(state.Visible(true,false) with {ShopName="Fixture shop"});
                Require(((CustomerDisplaySnapshot)customer.DataContext).IsWorking, "cart automatically enters working presentation");
                Require(((Image)customer.FindName("SaverImage")).Source == bitmap, "working order leaves advertisement intact");
                state.Complete();customer.Present(state.Visible(true,false));
                Require(!((CustomerDisplaySnapshot)customer.DataContext).IsWorking, "completed transaction returns to idle welcome");
                customer.Screensaver(null,"","",true);
                Require(((FrameworkElement)customer.FindName("SaverView")).Visibility == Visibility.Visible, "missing approved image retains neutral advertising half");
            }
            var dialogType=typeof(MainWindow).Assembly.GetType("NexaConnect.POS.PosPinEntryWindow")!;
            var dialog=(Window)Activator.CreateInstance(dialogType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,[staff,"Fixture PIN"],null)!;
            try
            {
                var content=(FrameworkElement)dialog.Content;
                Require(Descendants<PasswordBox>(content).Single().MaxLength==6,"masked PIN entry has a six-digit bound");
                Require(!Descendants<TextBox>(content).Any(),"PIN never enters a general textbox");
                Require(Descendants<Button>(content).Count(b=>b.Content?.ToString()?.Length==1 && char.IsDigit(b.Content.ToString()![0]))==10,"PIN dialog provides all touch digits");
            }
            finally {dialog.Close();}
        }
        finally {customer.Close();}
        Console.WriteLine("PASS: 50/50 advertisement and transaction views, welcome/working transitions, masked touch PIN keypad and ready-for-sale navigation; physical devices remain unverified.");
    }
}
