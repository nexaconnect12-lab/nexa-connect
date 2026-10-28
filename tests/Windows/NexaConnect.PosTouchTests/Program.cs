using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NexaConnect.POS;

// Windows-only presentation fixture. Never reads live POS storage or authenticates.
internal static class Program
{
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [STAThread]
    private static void Main(string[] args)
    {
        var keyboard = new TouchKeyboard();
        string? last = null; keyboard.KeyPressed += key => last = key;
        keyboard.Configure("Fixture reference", false);
        Click(keyboard, "q"); Require(last == "q", "lowercase input");
        Click(keyboard, "Shift"); Click(keyboard, "Q"); Require(last == "Q", "shift");
        Click(keyboard, "Space"); Require(last == " ", "space");
        Click(keyboard, "Symbols"); Click(keyboard, "@"); Require(last == "@", "symbols");
        Click(keyboard, "⌫"); Require(last == "⌫", "backspace command");
        Click(keyboard, "Clear"); Require(last == "Clear", "clear command");
        bool dismissed = false; keyboard.Dismissed += () => dismissed = true;
        Click(keyboard, "Done · Hide keyboard"); Require(dismissed, "dismiss");
        keyboard.Configure("Fixture amount", true);
        Require(!Descendants<Button>(keyboard).Any(b => Equals(b.Content, "q")), "numeric layout");
        foreach (var button in Descendants<Button>(keyboard)) Require(!button.Focusable && button.MinHeight >= 48, "touch keys preserve field focus");

        string root = Path.Combine(Path.GetTempPath(), "NexaConnect-PosTouchTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var config = new PosClientConfiguration("http://127.0.0.1:49199/realms/fixture", "nexaconnect-pos",
            "nexaconnect-pos://oauth/callback", "openid", "http://127.0.0.1:49199/", "http://127.0.0.1:49199/",
            Guid.NewGuid(), Guid.NewGuid(), "THB", "cash_manual", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CatalogApi: "http://127.0.0.1:49199/");
        var tokenStore = Activator.CreateInstance(typeof(MainWindow).Assembly.GetType("NexaConnect.POS.WindowsTokenStore")!,
            Private | BindingFlags.Public, null, [root], null)!;
        using var auth = (PosAuthentication)Activator.CreateInstance(typeof(PosAuthentication), Private, null,
            [config, new HttpClientHandler(), tokenStore, (Action<string>)(_ => throw new InvalidOperationException("Fixture must not launch sign-in"))], null)!;
        using var api = new PosApiClient(config);
        var scope = LocalPosScope.From(config);
        var window = new MainWindow(auth, api, new LocalPosStore(scope, root), new LocalOutboxStore(scope, root), config)
            { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            // Synthetic identity is injected only into this test instance, never a token store.
            typeof(PosAuthentication).GetProperty("CurrentToken")!.SetValue(auth,
                new PosTokenSet("fixture-not-a-token", null, DateTimeOffset.UtcNow.AddMinutes(10), "Bearer",
                    SessionStartedAtUtc: DateTimeOffset.UtcNow, SessionExpiresAtUtc: DateTimeOffset.UtcNow.AddHours(1), Subject: "fixture", Authority: config.Authority));
            Invoke(window, "UpdateOperationalState");
            window.Show(); Pump(window);
            var tabs = (TabControl)window.FindName("WorkspaceTabs");
            tabs.SelectedIndex = 4; Pump(window); // Shift & cash, no network use case.
            var field = (TextBox)window.FindName("MovementAmountTextBox");
            foreach (var size in new[] { new Size(1000, 680), new Size(1280, 850), new Size(1536, 864) })
            {
                window.Width = size.Width; window.Height = size.Height; Pump(window);
                Invoke(window, "ShowTouchInput", field); Pump(window);
                var input = (TouchKeyboard)window.FindName("InputKeyboard");
                var viewport = (ScrollViewer)window.FindName("InputViewport");
                Require(input.Visibility == Visibility.Visible && input.ActualHeight >= 250, "keyboard visible");
                var fieldRect = field.TransformToAncestor(viewport).TransformBounds(new Rect(field.RenderSize));
                Require(fieldRect.Top >= -1 && fieldRect.Bottom <= viewport.ActualHeight + 1, "focused amount stays in viewport");
                var shell = (FrameworkElement)window.Content;
                var inputRect = input.TransformToAncestor(shell).TransformBounds(new Rect(input.RenderSize));
                var viewportRect = viewport.TransformToAncestor(shell).TransformBounds(new Rect(viewport.RenderSize));
                Require(viewportRect.Bottom <= inputRect.Top + 1, "keyboard never overlays workspace");
                var dock = (Border)window.FindName("WorkflowDock");
                var dockRect = dock.TransformToAncestor(shell).TransformBounds(new Rect(dock.RenderSize));
                Require(viewportRect.Bottom <= dockRect.Top + 1 && dockRect.Bottom <= inputRect.Top + 1, "workflow amount/actions stay above keyboard without overlap");
                Require(dockRect.Bottom <= shell.ActualHeight && dockRect.Top >= 0, "workflow dock stays on screen");
                if (args is ["--preview"] && size.Width == 1280)
                {
                    var bitmap = new RenderTargetBitmap((int)shell.ActualWidth, (int)shell.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(shell); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    Directory.CreateDirectory(".runstate"); using var output = File.Create(".runstate/pos-touch-preview.png"); encoder.Save(output);
                }
                field.Text = "12345"; field.Select(2, 0); field.SelectedText = "678";
                Require(field.Text == "12678345", "native scanner-style insertion is preserved");
                Invoke(window, "HideTouchInput"); Require(input.Visibility == Visibility.Collapsed, "hide restores workspace");
            }
            foreach (var size in new[] { new Size(1000, 680), new Size(1280, 850), new Size(1536, 864) })
            {
                window.Width = size.Width; window.Height = size.Height;
                tabs.SelectedIndex = 0; Pump(window);
                var search = (TextBox)window.FindName("MenuSearchTextBox");
                Invoke(window, "ShowTouchInput", search); Pump(window);
                var viewport = (ScrollViewer)window.FindName("InputViewport");
                var bounds = search.TransformToAncestor(viewport).TransformBounds(new Rect(search.RenderSize));
                Require(bounds.Top >= -1 && bounds.Bottom <= viewport.ActualHeight + 1, "search stays visible with sticky actions");
                Require(((Button)window.FindName("PlaceOrderButton")).IsVisible, "checkout retains original action button");
                Require(!((Button)window.FindName("PaidButton")).IsVisible, "payment action hidden outside Payment");
                if (args is ["--preview"] && size.Width == 1280) SavePreview(window, ".runstate/pos-ux2-checkout-preview.png");
            }
            Invoke(window, "WorkflowNext_Click", window, new RoutedEventArgs()); Pump(window);
            Require(((TabItem)window.FindName("ShiftCashTab")).IsSelected, "next step only navigates to shift setup");
            tabs.SelectedIndex = 0; Pump(window);
            var searchField = (TextBox)window.FindName("MenuSearchTextBox"); searchField.Text = "fixture filter";
            Invoke(window, "ClearMenuSearch_Click", window, new RoutedEventArgs());
            Require(searchField.Text.Length == 0, "clear filters requires no typing");
            typeof(MainWindow).GetField("pendingOrder", Private)!.SetValue(window,
                new PosOrderResult(Guid.NewGuid(), PosOrderStatus.KitchenAccepted, 123.45m, "THB"));
            ((TabItem)window.FindName("PaymentTab")).IsSelected = true; Pump(window);
            Require(((Button)window.FindName("PaidButton")).IsVisible && !((Button)window.FindName("PlaceOrderButton")).IsVisible, "Payment retains only original payment action");
            Require(((TextBlock)window.FindName("WorkflowAmount")).Text == CashierPresentation.Money(123.45m, "THB"), "original server total wins over zero cart");
            typeof(MainWindow).GetField("settlementUncertain", Private)!.SetValue(window, true);
            Invoke(window, "RenderWorkflow");
            Require(((TextBlock)window.FindName("WorkflowTitle")).Text == "Verify the previous payment", "uncertain payment guidance");
            typeof(MainWindow).GetField("cancellationStatus", Private)!.SetValue(window, "pending");
            Invoke(window, "RenderWorkflow");
            Require(((TextBlock)window.FindName("WorkflowTitle")).Text == "Verify the original cancellation", "cancellation recovery guidance");
            typeof(MainWindow).GetField("pendingOrder", Private)!.SetValue(window, null);
            typeof(MainWindow).GetField("settlementUncertain", Private)!.SetValue(window, false);
            typeof(MainWindow).GetField("cancellationStatus", Private)!.SetValue(window, null);
            tabs.SelectedIndex = 0; Pump(window);
            Invoke(window, "ShowTouchInput", field);
            Invoke(window, "SetBusy", "Fixture busy");
            Require(((TouchKeyboard)window.FindName("InputKeyboard")).Visibility == Visibility.Collapsed, "busy clears input target");
            Require(!((Button)window.FindName("PlaceOrderButton")).IsEnabled && !((Button)window.FindName("PaidButton")).IsEnabled, "pinned financial actions disabled while busy");
            Require(((ProgressBar)window.FindName("BusyProgress")).Visibility == Visibility.Visible, "busy progress visible");
            Invoke(window, "BrowseOrders_Click", window, new RoutedEventArgs());
            Require(tabs.SelectedIndex == 0, "busy shortcut cannot navigate or load orders");
            typeof(MainWindow).GetField("busy", Private)!.SetValue(window, false);
            field.IsEnabled = false; Invoke(window, "ShowTouchInput", field);
            Require(((TouchKeyboard)window.FindName("InputKeyboard")).Visibility == Visibility.Collapsed, "disabled field rejected");
            field.IsEnabled = true;
            typeof(MainWindow).GetField("reauthenticationRequired", Private)!.SetValue(window, true);
            Invoke(window, "RenderWorkflow");
            Require(((Border)window.FindName("WorkflowDock")).Visibility == Visibility.Collapsed, "workflow totals and controls hidden while locked");
            Invoke(window, "ShowTouchInput", field);
            Require(((TouchKeyboard)window.FindName("InputKeyboard")).Visibility == Visibility.Collapsed, "locked field rejected");
            Invoke(window, "ReportWindowsKeyboardFailure");
            Require(((TextBlock)window.FindName("LockStatusText")).Text.Contains("Accessibility"), "keyboard failure guidance visible during lock");
            Console.WriteLine("PASS: keyboard actions, numeric/text viewport layouts at three sizes, pinned totals/actions, navigation, filter reset, native insertion, busy/disabled/locked guards and visible locked fallback guidance. Physical touch/scanner/OIDC acceptance remains pending.");
        }
        finally { window.Close(); }
    }
    private static void Pump(Window window) { window.UpdateLayout(); window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, () => { }); window.UpdateLayout(); }
    private static void SavePreview(Window window, string path)
    {
        var root = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(".runstate"); using var output = File.Create(path); encoder.Save(output);
    }
    private static void Invoke(MainWindow window, string name, params object[] args) => typeof(MainWindow).GetMethod(name, Private)!.Invoke(window, args);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Click(DependencyObject root, string content)
    {
        var element = (FrameworkElement)root;
        element.Measure(new Size(1000, 400)); element.Arrange(new Rect(0, 0, 1000, 400)); element.UpdateLayout();
        Descendants<Button>(root).Single(b => Equals(b.Content, content)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
