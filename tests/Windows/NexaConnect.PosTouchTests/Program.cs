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
        using var api = new PosApiClient(config, new NoNetworkHandler(config), new NoNetworkHandler(config), new NoNetworkHandler(config), new NoNetworkHandler(config));
        var scope = LocalPosScope.From(config);
        var preferences = new PosUiPreferences(Path.Combine(root, "preferences"));
        var printers = new ReceiptPrinterPreferences(Path.Combine(root, "printers"));
        var printDriver = new CancelPrintDriver();
        var diagnosticsHandler = new ReachabilityHandler();
        var diagnostics = new PosTerminalDiagnostics(config, diagnosticsHandler);
        var window = new MainWindow(auth, api, new LocalPosStore(scope, root), new LocalOutboxStore(scope, root), config, preferences, printers, printDriver, () => Task.FromResult(new[] { "Fixture PDF" }), diagnostics)
            { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            // Synthetic identity is injected only into this test instance, never a token store.
            typeof(PosAuthentication).GetProperty("CurrentToken")!.SetValue(auth,
                new PosTokenSet("fixture-not-a-token", null, DateTimeOffset.UtcNow.AddMinutes(10), "Bearer",
                    SessionStartedAtUtc: DateTimeOffset.UtcNow, SessionExpiresAtUtc: DateTimeOffset.UtcNow.AddHours(1), Subject: "fixture", Authority: config.Authority));
            Invoke(window, "UpdateOperationalState");
            window.Show(); Pump(window);
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
            var tabs = (TabControl)window.FindName("WorkspaceTabs");
            var inputMode = (CheckBox)window.FindName("TouchFirstCheckBox");
            Require(inputMode.IsChecked == false, "touch-first defaults off");
            Invoke(window, "ShowTouchInput", (TextBox)window.FindName("MenuSearchTextBox")); Pump(window);
            Require(((TouchKeyboard)window.FindName("InputKeyboard")).Visibility == Visibility.Collapsed, "default focus does not open keyboard");
            inputMode.IsChecked = true;
            Require(preferences.LoadTouchFirst(), "explicit touch preference persisted");
            using (var lockedPreference = new FileStream(Path.Combine(root, "preferences", "input.json"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                inputMode.IsChecked = false;
                Require(inputMode.IsChecked == true, "failed save restores the previously selected mode");
                Require(((TextBlock)window.FindName("InputModeStatusText")).Text.Contains("previous mode"), "failed save has visible guidance");
            }
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
            Invoke(window, "ShowTouchInput", searchField); Pump(window);
            inputMode.IsChecked = false;
            Require(((TouchKeyboard)window.FindName("InputKeyboard")).Visibility == Visibility.Collapsed && !preferences.LoadTouchFirst(), "disabling touch mode closes keyboard and persists off");
            Invoke(window, "ShowTouchInput", searchField);
            Require(((TouchKeyboard)window.FindName("InputKeyboard")).Visibility == Visibility.Collapsed, "off mode suppresses subsequent automatic keyboard");
            inputMode.IsChecked = true;
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
            VerifyDensity(window, preferences, args);
            VerifyPrinterSettings(window, printers, printDriver, root);
            VerifyScannerGuards(window);
            VerifyTerminalSetup(window,diagnosticsHandler);
            if(args.Contains("--ux7-preview")){((TabItem)window.FindName("TerminalTab")).IsSelected=true;((ScrollViewer)((TabItem)window.FindName("TerminalTab")).Content).ScrollToTop();Pump(window);SavePreview(window,".runstate/pos-ux7-overview-preview.png");}
            tabs.SelectedIndex = 0; Pump(window);
            Invoke(window, "ShowTouchInput", field);
            Invoke(window, "SetBusy", "Fixture busy");
            inputMode.IsChecked = false;
            Require(inputMode.IsChecked == true && preferences.LoadTouchFirst(), "busy input setting change rejected");
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
            Console.WriteLine("PASS: independent density/keyboard combinations at four sizes, responsive cash panels, Orders rows, exact payment totals, preference rollback and existing input/workflow/lock guards. Physical touch/scanner/OIDC acceptance remains pending.");
        }
        finally { window.Close(); }
    }
    private static void Pump(Window window) { window.UpdateLayout(); window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, () => { }); window.UpdateLayout(); }
    private static void VerifyTerminalSetup(MainWindow window,ReachabilityHandler handler)
    {
        ((TabItem)window.FindName("TerminalTab")).IsSelected=true;Invoke(window,"UpdateOperationalState");Pump(window);
        Require(((TextBlock)window.FindName("DrawerAvailabilityText")).Text.Contains("Disabled"),"drawer preparation must not imply an enabled physical driver");
        Require(window.FindName("DrawerOpenButton") is null,"no ungated manual opening action is exposed");
        var list=(ItemsControl)window.FindName("TerminalSetupList");
        Require(list.Items.Count==15,"setup overview includes settings and six logical service checks");
        var rows=list.Items.Cast<TerminalSetupRow>().ToArray();
        Require(rows.Single(r=>r.Area=="Output acceptance").Status==TerminalReadiness.Untested,"overview never infers physical acceptance");
        Invoke(window,"RefreshTerminalSetup_Click",window,new RoutedEventArgs());
        for(int i=0;i<60&&(bool)typeof(MainWindow).GetField("busy",Private)!.GetValue(window)!;i++){Pump(window);Thread.Sleep(5);}
        Pump(window);Require(handler.Requests==5,"explicit checks issue only anonymous reachability requests");
        Require(((TextBlock)window.FindName("StatusText")).Text.Contains("checks complete"),"global progress guidance completes with the overview");
        Require(list.Items.Cast<TerminalSetupRow>().Single(r=>r.Area=="Printer").Status==TerminalReadiness.Detected,"saved queue detection projected without printing");
        Invoke(window,"OpenScannerSetup_Click",window,new RoutedEventArgs());Pump(window);
        Require(((TabItem)window.FindName("CheckoutTab")).IsSelected&&((CheckBox)window.FindName("ScanModeCheckBox")).IsChecked!=true,"scanner shortcut navigates without enabling or submitting");
        Invoke(window,"OpenPrinterSetup_Click",window,new RoutedEventArgs());Pump(window);
        Require(((TabItem)window.FindName("TerminalTab")).IsSelected,"printer shortcut returns to settings only");
        handler.Hold=true;Invoke(window,"RefreshTerminalSetup_Click",window,new RoutedEventArgs());
        Invoke(window,"ClearReceiptView");handler.Release();
        for(int i=0;i<60&&(bool)typeof(MainWindow).GetField("busy",Private)!.GetValue(window)!;i++){Pump(window);Thread.Sleep(5);}
        Pump(window);
        Require(list.Items.Cast<TerminalSetupRow>().Where(r=>r.Area.StartsWith("Service:")).All(r=>r.Status==TerminalReadiness.Untested),"late diagnostic responses cannot restore cleared results");
    }
    private sealed class ReachabilityHandler:HttpMessageHandler
    {
        public int Requests;public bool Hold;private readonly List<TaskCompletionSource<HttpResponseMessage>> pending=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Requests++;Require(request.Method==HttpMethod.Get&&request.Headers.Authorization is null&&request.Content is null,"setup check is header-only anonymous GET");
            if(!Hold)return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
            var task=new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);pending.Add(task);return task.Task;
        }
        public void Release(){Hold=false;foreach(var task in pending)task.TrySetResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));pending.Clear();}
    }
    private static void VerifyScannerGuards(MainWindow window)
    {
        ((TabItem)window.FindName("CheckoutTab")).IsSelected=true;Pump(window);
        var input=(TextBox)window.FindName("BarcodeScanTextBox");var mode=(CheckBox)window.FindName("ScanModeCheckBox");
        Require(mode.IsChecked!=true&&!input.IsEnabled,"scanner entry defaults off");
        typeof(MainWindow).GetField("_activeShift",Private)!.SetValue(window,new LocalShiftState(Guid.NewGuid(),"FIXTURE",DateTimeOffset.UtcNow));
        Invoke(window,"UpdateOperationalState");mode.IsChecked=true;Pump(window);
        Require(input.IsEnabled,"explicit mode enables dedicated field for editable checkout");
        input.Text="001";((TabItem)window.FindName("PaymentTab")).IsSelected=true;Pump(window);
        Require(input.Text==""&&!input.IsEnabled,"leaving checkout clears and disables scan input");
        ((TabItem)window.FindName("CheckoutTab")).IsSelected=true;Pump(window);input.Text="001";
        Invoke(window,"ClearReceiptView");Require(input.Text=="","privacy transition clears partial scanner data");
        Require(!ScannerSafeConfirmation.AllowsKey(System.Windows.Input.Key.Enter)&&!ScannerSafeConfirmation.AllowsKey(System.Windows.Input.Key.Y),"scanner suffix and payload cannot confirm");
        Require(ScannerSafeConfirmation.AllowsKey(System.Windows.Input.Key.Tab)&&ScannerSafeConfirmation.AllowsKey(System.Windows.Input.Key.Space),"deliberate keyboard confirmation remains available");
        Require(!ScannerSafeConfirmation.Ask(window,"Fixture","Fixture",()=>false),"invalidated confirmation never opens");
        var dialog=new ScannerSafeConfirmation(window,"Fixture only","Fixture");
        try {
            var enter=new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),Environment.TickCount,System.Windows.Input.Key.Enter){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent};
            dialog.RaiseEvent(enter);Require(enter.Handled,"protected WPF dialog consumes scanner Enter");
            var composition=new System.Windows.Input.TextComposition(System.Windows.Input.InputManager.Current,dialog,"Y001");
            var text=new System.Windows.Input.TextCompositionEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,composition){RoutedEvent=System.Windows.Input.TextCompositionManager.PreviewTextInputEvent};
            dialog.RaiseEvent(text);Require(text.Handled,"protected WPF dialog consumes scanner text/mnemonics");
        } finally {dialog.Close();}
        mode.IsChecked=false;typeof(MainWindow).GetField("_activeShift",Private)!.SetValue(window,null);Invoke(window,"UpdateOperationalState");
    }
    private static void VerifyPrinterSettings(MainWindow window, ReceiptPrinterPreferences preferences, CancelPrintDriver driver, string root)
    {
        ((TabItem)window.FindName("TerminalTab")).IsSelected = true; Pump(window);
        Invoke(window, "RefreshPrinters_Click", window, new RoutedEventArgs()); Pump(window);
        var printer = (ComboBox)window.FindName("ReceiptPrinterPicker"); var paper = (ComboBox)window.FindName("ReceiptPaperPicker");
        Require(printer.Items.Count == 2 && paper.SelectedIndex == 0, "default Windows dialog and standard layout");
        printer.SelectedIndex = 1; paper.SelectedIndex = 1;
        Invoke(window, "SavePrinterSettings_Click", window, new RoutedEventArgs());
        Require(preferences.Load() == new ReceiptPrintOptions("Fixture PDF", ReceiptPaper.Roll80), "printer and 80 mm layout saved");
        using (File.Open(Path.Combine(root, "printers", "printing.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            paper.SelectedIndex = 0; Invoke(window, "SavePrinterSettings_Click", window, new RoutedEventArgs());
            Require(paper.SelectedIndex == 1, "failed printer save restores selection");
        }
        Invoke(window, "PreviewTestReceipt_Click", window, new RoutedEventArgs()); Pump(window);
        var preview = (FlowDocumentScrollViewer)window.FindName("TestReceiptPreview");
        Require(preview.Document is not null && Math.Abs(preview.Document.PageWidth - 80 * ReceiptPrintLayout.Millimetre) < 0.01, "test preview uses 80 mm page");
        Invoke(window, "PrintTestReceipt_Click", window, new RoutedEventArgs()); Pump(window);
        Require(driver.Prepared == 1 && ((TextBlock)window.FindName("PrinterSetupStatus")).Text.Contains("cancelled"), "test print cancellation stays non-financial");
        typeof(MainWindow).GetField("busy", Private)!.SetValue(window, true);
        paper.SelectedIndex = 0; Invoke(window, "SavePrinterSettings_Click", window, new RoutedEventArgs());
        Require(preferences.Load().Paper == ReceiptPaper.Roll80, "busy state prevents changing printer preference");
        typeof(MainWindow).GetField("busy", Private)!.SetValue(window, false); paper.SelectedIndex = 1;
        Invoke(window, "ClearReceiptView"); Require(preview.Document is null, "privacy transition clears test preview too");
    }
    private sealed class CancelPrintDriver : IReceiptPrintDriver
    {
        public int Prepared;
        public IReceiptPrintJob? Prepare(ReceiptPrintOptions options) { Prepared++; return null; }
    }
    private static void VerifyDensity(MainWindow window, PosUiPreferences preferences, string[] args)
    {
        var picker = (ComboBox)window.FindName("DensityPicker"); var input = (CheckBox)window.FindName("TouchFirstCheckBox");
        Require(picker.SelectedIndex == 0, "density defaults Standard independently of keyboard opt-in");
        foreach (var density in new[] { PosDisplayDensity.Standard, PosDisplayDensity.Touch })
        foreach (bool keyboard in new[] { false, true })
        {
            picker.SelectedIndex = (int)density; input.IsChecked = keyboard; Pump(window);
            Require(preferences.Load() == new PosUiOptions(keyboard, density), "density and automatic keyboard choices remain independent");
            Require(((Button)window.FindName("OpenShiftButton")).MinHeight == PosDensityMetrics.For(density).ControlHeight, "dynamic control height follows density");
            foreach (var size in new[] { new Size(960, 640), new Size(1024, 768), new Size(1280, 800), new Size(1536, 864) })
            {
                window.Width = size.Width; window.Height = size.Height; Pump(window);
                var tabs = (TabControl)window.FindName("WorkspaceTabs");
                tabs.SelectedIndex = 0; Pump(window);
                var search = (TextBox)window.FindName("MenuSearchTextBox");
                Invoke(window, "ShowTouchInput", search); search.BringIntoView(); Pump(window);
                var keys = (TouchKeyboard)window.FindName("InputKeyboard");
                Require((keys.Visibility == Visibility.Visible) == keyboard, "density does not enable automatic keyboard");
                var viewport = (ScrollViewer)window.FindName("InputViewport");
                var searchBounds = search.TransformToAncestor(viewport).TransformBounds(new Rect(search.RenderSize));
                Require(searchBounds.Top >= -1 && searchBounds.Bottom <= viewport.ActualHeight + 1, $"search visible at {size} {density}/{keyboard}");
                var root = (FrameworkElement)window.Content; var dock = (Border)window.FindName("WorkflowDock");
                var dockBounds = dock.TransformToAncestor(root).TransformBounds(new Rect(dock.RenderSize));
                Require(dockBounds.Top >= 0 && dockBounds.Bottom <= root.ActualHeight + 1, "pinned dock remains inside window");
                if (keyboard)
                {
                    var keyBounds = keys.TransformToAncestor(root).TransformBounds(new Rect(keys.RenderSize));
                    Require(dockBounds.Bottom <= keyBounds.Top + 1, "density dock does not overlay keyboard");
                }
                if (args is ["--preview"] && size.Width == 960 && keyboard)
                    SavePreview(window, $".runstate/pos-ux3-{density}-preview.png");
                tabs.SelectedIndex = 4; Pump(window);
                Require(((ScrollViewer)((TabItem)window.FindName("ShiftCashTab")).Content).PanningMode == PanningMode.VerticalOnly, "stacked cash page supports vertical touch panning");
                Require(Grid.GetRow((Border)window.FindName("CashReconciliationCard")) == (size.Width < 1100 ? 1 : 0), "cash panels stack at narrow width");
                var amount = (TextBox)window.FindName("MovementAmountTextBox");
                Invoke(window, "ShowTouchInput", amount); amount.BringIntoView(); Pump(window);
                var amountBounds = amount.TransformToAncestor(viewport).TransformBounds(new Rect(amount.RenderSize));
                Require(amountBounds.Top >= -1 && amountBounds.Bottom <= viewport.ActualHeight + 1, "cash amount remains reachable");
                tabs.SelectedIndex = 2; Pump(window);
                Require(ScrollViewer.GetPanningMode((DataGrid)window.FindName("OrdersGrid")) == PanningMode.Both, "Orders keeps two-axis panning for wide columns");
                Require(((DataGrid)window.FindName("OrdersGrid")).RowHeight == PosDensityMetrics.For(density).ControlHeight, "Orders row density updates");
                typeof(MainWindow).GetField("pendingOrder", Private)!.SetValue(window, new PosOrderResult(Guid.NewGuid(), PosOrderStatus.KitchenAccepted, 999999999.99m, "THB"));
                ((TabItem)window.FindName("PaymentTab")).IsSelected = true; Pump(window);
                var total = (TextBlock)window.FindName("WorkflowAmount");
                Require(total.Text == CashierPresentation.Money(999999999.99m, "THB"), "large server total stays exact");
                var totalBounds = total.TransformToAncestor(dock).TransformBounds(new Rect(total.RenderSize));
                Require(totalBounds.Left >= 0 && totalBounds.Right <= dock.ActualWidth + 1, "total is not horizontally clipped");
                typeof(MainWindow).GetField("pendingOrder", Private)!.SetValue(window, null);
            }
        }
        input.IsChecked = true; picker.SelectedIndex = 1; Pump(window);
        // Read-only fixture path is obtained by the test, never from product input.
        string file = PreferencesPath(preferences);
        using (var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            picker.SelectedIndex = 0;
            Require(picker.SelectedIndex == 1, "failed density save restores selection");
            Require(((TextBlock)window.FindName("DensityStatusText")).Text.Contains("previous layout"), "density save failure is visible");
        }
        Require(input.IsChecked == true && preferences.Load() == new PosUiOptions(true, PosDisplayDensity.Touch), "failed density save preserves both preferences");
    }
    private static string PreferencesPath(PosUiPreferences preferences) => (string)typeof(PosUiPreferences).GetField("path", Private)!.GetValue(preferences)!;
    private sealed class NoNetworkHandler(PosClientConfiguration configuration) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Require(request.Method == HttpMethod.Get, "presentation fixture must not issue financial commands");
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
                Content = System.Net.Http.Json.JsonContent.Create(new PosBranchOrderPage(configuration.OrganizationId,
                    configuration.RestaurantId, configuration.BranchId, Array.Empty<PosBranchOrder>(), null)) });
        }
    }
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
