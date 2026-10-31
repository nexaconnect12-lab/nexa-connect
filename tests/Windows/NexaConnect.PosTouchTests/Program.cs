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
internal static partial class Program
{
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [STAThread]
    private static void Main(string[] args)
    {
        if (args is ["--shutdown-child", var childRoot, var mode]) { RunShutdownChild(childRoot, mode); return; }
        if (args is ["--shutdown-only"]) { VerifyShutdown(); return; }
        VerifySessionExpiry();
        if (args is ["--session-expiry-only"]) return;
        VerifyShutdown();
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
            Guid.NewGuid(), Guid.NewGuid(), "THB", "cash_manual", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CatalogApi: "http://127.0.0.1:49199/", EnableOmiseTestCheckout:true);
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
        var drawerDriver=new DrawerFixtureDriver();var drawerAuthority=new DrawerFixtureAuthority();
        var drawer=new NexaConnect.POS.Hardware.Drawers.DrawerCoordinator(new(drawerDriver,drawerAuthority,new NexaConnect.POS.Hardware.Drawers.SqliteDrawerIntentStore(Path.Combine(root,"drawer-intents.db"))));
        var historyPort=new DrawerHistoryFixturePort(config);
        var history=new NexaConnect.POS.Hardware.Drawers.DrawerHistoryReader(config,historyPort,new NexaConnect.POS.Hardware.Drawers.SqliteDrawerIntentStore(Path.Combine(root,"drawer-intents.db"),config.Authority));
        var window = new MainWindow(auth, api, new LocalPosStore(scope, root), new LocalOutboxStore(scope, root), config, preferences, printers, printDriver, () => Task.FromResult(new[] { "Fixture PDF" }), diagnostics,drawer,history)
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
            VerifyCashierNavigation(window);
            VerifyMenuPhotoPresentation(window);
            VerifyMenuOrganization(window,config);
            VerifyMenuAvailability(window);
            if(args.Contains("--shell-preview")){PreviewCashierShell(window);return;}
            VerifyMixedPayments(window,config);
            VerifyPromptPayProof(window,config);
            VerifyDrawerIntegration(window,drawerDriver,drawerAuthority,root);
            VerifyDrawerHistory(window,historyPort,drawerDriver,root);
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
            if(args.Contains("--ux7-preview")){((TabItem)window.FindName("TerminalTab")).IsSelected=true;((TabItem)window.FindName("TerminalOverviewTab")).IsSelected=true;((ScrollViewer)window.FindName("TerminalOverviewScroll")).ScrollToTop();Pump(window);SavePreview(window,".runstate/pos-ux7-overview-preview.png");}
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
            Console.WriteLine("PASS: independent density/keyboard combinations at four sizes, responsive cash panels, Orders rows, exact payment totals, preference rollback guarded drawer hook/payment isolation and existing input/workflow/lock guards. Physical touch/scanner/OIDC/drawer acceptance remains pending.");
        }
        finally { window.Close(); }
    }
    private static void Pump(Window window) { window.UpdateLayout(); window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, () => { }); window.UpdateLayout(); }
    private static void VerifyMixedPayments(MainWindow window,PosClientConfiguration config)
    {
        var picker=(ComboBox)window.FindName("CheckoutPaymentPicker");var token=(PasswordBox)window.FindName("CardTokenBox");
        Require(picker.Items.Count==2&&picker.SelectedIndex==0,"sandbox capability is additive, manual stays default");
        picker.SelectedIndex=1;Pump(window);
        Require(((StackPanel)window.FindName("OmiseTestCardPanel")).Visibility==Visibility.Visible,"card selection exposes only test token UI");
        token.Password="tokn_test_aaaaaaaaaaaa";picker.SelectedIndex=0;Pump(window);
        Require(token.Password.Length==0,"changing methods clears ephemeral test token");
        var checkout=PendingCheckout.Create(config,[new(Guid.NewGuid(),1)],"card_omise_test");
        typeof(MainWindow).GetField("pendingCheckout",Private)!.SetValue(window,checkout);
        Invoke(window,"UpdateOperationalState");Pump(window);
        Require(picker.SelectedIndex==1&&!picker.IsEnabled,"retained card checkout pins its original method");
        picker.SelectedIndex=0;Require(picker.SelectedIndex==1,"programmatic method change cannot replace recovery method");
        Require(!((Button)window.FindName("PaidButton")).IsEnabled,"card cannot invoke manual paid action");
        token.Password="tokn_test_aaaaaaaaaaaa";
        var qr=PendingCheckout.ForQr(config,Guid.NewGuid().ToString(),Guid.NewGuid(),"fixture");
        typeof(MainWindow).GetField("pendingCheckout",Private)!.SetValue(window,qr);
        Invoke(window,"UpdateOperationalState");Pump(window);
        Require(picker.SelectedIndex==0&&token.Password.Length==0,"QR collection pins manual tender and clears card token");
        Require(((StackPanel)window.FindName("OmiseTestCardPanel")).Visibility==Visibility.Collapsed,"QR does not offer card collection");
        typeof(MainWindow).GetField("pendingCheckout",Private)!.SetValue(window,null);
        Invoke(window,"UpdateOperationalState");Pump(window);
    }
    private static void VerifyTerminalSetup(MainWindow window,ReachabilityHandler handler)
    {
        ((TabItem)window.FindName("TerminalTab")).IsSelected=true;((TabItem)window.FindName("TerminalOverviewTab")).IsSelected=true;Invoke(window,"UpdateOperationalState");Pump(window);
        Require(((TabControl)window.FindName("TerminalSections")).Items.Count==4,"terminal areas have independent inner sections");
        Require(((TextBlock)window.FindName("DrawerAvailabilityText")).Text.Contains("Disabled"),"drawer preparation must not imply an enabled physical driver");
        Require(!((Button)window.FindName("DrawerOpenButton")).IsEnabled,"manual opening remains disabled without verified hardware");
        Require(!((ComboBox)window.FindName("DrawerReasonPicker")).IsEnabled,"drawer reason cannot enable hardware");
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
        Require(((TabItem)window.FindName("TerminalTab")).IsSelected&&((TabItem)window.FindName("TerminalDevicesTab")).IsSelected,"printer shortcut opens its inner settings section");
        Invoke(window,"OpenInputSetup_Click",window,new RoutedEventArgs());Pump(window);
        Require(((TabItem)window.FindName("TerminalDevicesTab")).IsSelected,"input shortcut opens its inner settings section");
        Invoke(window,"OpenDisplaySetup_Click",window,new RoutedEventArgs());Pump(window);
        Require(((TabItem)window.FindName("TerminalDevicesTab")).IsSelected,"display shortcut opens its inner settings section");
        ((TabItem)window.FindName("TerminalSyncTab")).IsSelected=true;Pump(window);
        Invoke(window,"ShowTouchInput",(TextBox)window.FindName("TerminalCodeTextBox"));Pump(window);
        Require(((TouchKeyboard)window.FindName("InputKeyboard")).Visibility==Visibility.Visible,"fixture opens input for the active sync field");
        ((TabItem)window.FindName("TerminalOverviewTab")).IsSelected=true;Pump(window);
        Require(((TouchKeyboard)window.FindName("InputKeyboard")).Visibility==Visibility.Collapsed,"inner section changes cannot leave a keyboard editing hidden settings");
        handler.Hold=true;Invoke(window,"RefreshTerminalSetup_Click",window,new RoutedEventArgs());
        Invoke(window,"ClearReceiptView");handler.Release();
        for(int i=0;i<60&&(bool)typeof(MainWindow).GetField("busy",Private)!.GetValue(window)!;i++){Pump(window);Thread.Sleep(5);}
        Pump(window);
        Require(list.Items.Cast<TerminalSetupRow>().Where(r=>r.Area.StartsWith("Service:")).All(r=>r.Status==TerminalReadiness.Untested),"late diagnostic responses cannot restore cleared results");
    }
    private static void VerifyPromptPayProof(MainWindow window,PosClientConfiguration config)
    {
        var store=(LocalPosStore)typeof(MainWindow).GetField("_localStore",Private)!.GetValue(window)!;
        var tender=(ComboBox)window.FindName("TenderMethodComboBox");var reference=(TextBox)window.FindName("BankReferenceTextBox");
        var receipt=(CheckBox)window.FindName("ReceiptConfirmedCheckBox");var image=(Image)window.FindName("PromptPayQrImage");var paid=(Button)window.FindName("PaidButton");
        var id=Guid.NewGuid();var key=Guid.NewGuid();
        typeof(MainWindow).GetField("_activeShift",Private)!.SetValue(window,new LocalShiftState(Guid.NewGuid(),"Fixture",DateTimeOffset.UtcNow));
        typeof(MainWindow).GetField("pendingOrder",Private)!.SetValue(window,new PosOrderResult(id,PosOrderStatus.KitchenAccepted,50,"THB"));
        typeof(MainWindow).GetField("settlementIdempotencyKey",Private)!.SetValue(window,key);
        store.SavePendingSettlement(new(id,50,"THB",key));tender.SelectedIndex=1;Invoke(window,"UpdateOperationalState");Pump(window);
        reference.Text="fixture-reference";receipt.IsChecked=true;
        Require(!paid.IsEnabled,"missing QR cannot be bypassed by checking receipt proof");
        image.Source=BitmapSource.Create(64,64,96,96,System.Windows.Media.PixelFormats.Bgra32,null,new byte[64*64*4],64*4);
        Invoke(window,"UpdateOperationalState");Require(paid.IsEnabled,"configured image plus explicit proof enables confirmation");
        receipt.IsChecked=false;Require(!paid.IsEnabled,"receipt checkbox updates readiness immediately");receipt.IsChecked=true;
        reference.Clear();Require(!paid.IsEnabled,"empty reference updates readiness immediately");reference.Text="fixture-reference";Require(paid.IsEnabled,"valid reference updates readiness immediately");
        store.ClearCheckoutAndSettlement();var next=Guid.NewGuid();
        typeof(MainWindow).GetField("pendingOrder",Private)!.SetValue(window,new PosOrderResult(next,PosOrderStatus.KitchenAccepted,50,"THB"));
        store.SavePendingSettlement(new(next,50,"THB",key));Invoke(window,"UpdateOperationalState");
        Require(reference.Text.Length==0&&receipt.IsChecked==false&&!paid.IsEnabled,"new order cannot inherit another receipt/reference");
        store.SavePendingSettlement(new(next,50,"THB",key,"promptpay_manual",true,"original-reference",true));
        typeof(MainWindow).GetField("settlementUncertain",Private)!.SetValue(window,true);image.Source=null;Invoke(window,"UpdateOperationalState");
        Require(paid.IsEnabled&&!reference.IsEnabled&&!receipt.IsEnabled,"original uncertain payment can be verified without QR using immutable saved proof");
        Require(((TextBlock)window.FindName("PaymentReadinessText")).Text.Contains("original",StringComparison.OrdinalIgnoreCase),"verification gives original-payment guidance");
        store.ClearCheckoutAndSettlement();typeof(MainWindow).GetField("pendingOrder",Private)!.SetValue(window,null);
        typeof(MainWindow).GetField("settlementIdempotencyKey",Private)!.SetValue(window,null);typeof(MainWindow).GetField("settlementUncertain",Private)!.SetValue(window,false);
        typeof(MainWindow).GetField("_activeShift",Private)!.SetValue(window,null);tender.SelectedIndex=0;Invoke(window,"UpdateOperationalState");
    }
    private static void VerifyDrawerIntegration(MainWindow window,DrawerFixtureDriver driver,DrawerFixtureAuthority authority,string root)
    {
        Require(!File.Exists(Path.Combine(root,"drawer-intents.db")),"unconfigured drawer creates no journal");
        var auth=(PosAuthentication)typeof(MainWindow).GetField("_authentication",Private)!.GetValue(window)!;
        var store=(LocalPosStore)typeof(MainWindow).GetField("_localStore",Private)!.GetValue(window)!;
        string subject=auth.CurrentToken!.Subject!;Guid shift=Guid.NewGuid(),cash=Guid.NewGuid(),order=Guid.NewGuid(),key=Guid.NewGuid();
        typeof(MainWindow).GetField("_activeShift",Private)!.SetValue(window,new LocalShiftState(shift,"drawer-fixture",DateTimeOffset.UtcNow));
        typeof(MainWindow).GetField("cashSessionId",Private)!.SetValue(window,cash);
        driver.Enabled=true;Invoke(window,"UpdateOperationalState");
        Require(((Button)window.FindName("DrawerOpenButton")).IsEnabled,"manual action requires configured fake hardware and current custody");
        var attempt=new LocalPendingSettlementState(order,10,"THB",key,"cash");
        var paid=new ManualTenderResult(Guid.NewGuid(),order,"Paid","cash",10,"THB",DateTimeOffset.UtcNow.AddSeconds(-1),false);
        int generation=(int)typeof(MainWindow).GetField("receiptSessionGeneration",Private)!.GetValue(window)!;
        void Complete(ManualTenderResult result,bool recovery,int capturedGeneration){
            var task=(Task)typeof(MainWindow).GetMethod("HandleCashDrawerAfterPaymentAsync",Private)!.Invoke(window,[result,attempt,recovery,capturedGeneration,subject,(Guid?)shift,(Guid?)cash])!;
            for(int i=0;i<200&&!task.IsCompleted;i++){Pump(window);Thread.Sleep(5);}Require(task.IsCompletedSuccessfully,"drawer hook completes without throwing into payment handling");
        }
        Complete(paid,true,generation);Complete(paid with{Replayed=true},false,generation);Complete(paid with{Method="promptpay_manual"},false,generation);Complete(paid,false,generation-1);
        Require(driver.Calls==0&&authority.Calls==0,"recovery, replay, noncash and stale sessions cannot request a drawer");
        var status=(TextBlock)window.FindName("StatusText");status.Text="Fixture payment remains confirmed";driver.Fail=true;Complete(paid,false,generation);
        Require(driver.Calls==1&&authority.Reported==NexaConnect.POS.Hardware.Drawers.DrawerOutcome.Unknown,"driver failure reports uncertainty once");
        Require(status.Text=="Fixture payment remains confirmed"&&store.LoadPendingSettlement() is null,"drawer failure cannot recreate payment recovery");
        Complete(paid,false,generation);Require(driver.Calls==1,"same original cash completion cannot repeat device submission");
        driver.Enabled=false;typeof(MainWindow).GetField("_activeShift",Private)!.SetValue(window,null);typeof(MainWindow).GetField("cashSessionId",Private)!.SetValue(window,null);Invoke(window,"UpdateOperationalState");
        Require(!((Button)window.FindName("DrawerOpenButton")).IsEnabled,"missing verified hardware disables manual action again");
    }
    private static void VerifyDrawerHistory(MainWindow window,DrawerHistoryFixturePort port,DrawerFixtureDriver driver,string root)
    {
        ((TabItem)window.FindName("TerminalTab")).IsSelected=true;((TabItem)window.FindName("TerminalDrawerTab")).IsSelected=true;Invoke(window,"UpdateOperationalState");Pump(window);
        var list=(DataGrid)window.FindName("DrawerHistoryList");var check=(Button)window.FindName("DrawerHistoryCheckButton");var status=(TextBlock)window.FindName("DrawerHistoryStatusText");int calls=driver.Calls;
        var before=File.ReadAllBytes(Path.Combine(root,"drawer-intents.db"));
        void Wait(){for(int i=0;i<200&&(bool)typeof(MainWindow).GetField("drawerHistoryReading",Private)!.GetValue(window)!;i++){Pump(window);Thread.Sleep(5);}Pump(window);}
        Invoke(window,"DrawerHistoryRefresh_Click",window,new RoutedEventArgs());Wait();Require(list.Items.Count==1,"own terminal history loads without an open shift or physical driver");
        list.SelectedIndex=0;Pump(window);Require(check.IsEnabled,"history row selection replaces UUID entry");Invoke(window,"DrawerHistoryCheck_Click",window,new RoutedEventArgs());Wait();
        Require(((TextBlock)window.FindName("DrawerHistoryGuidanceText")).Text.Contains("uncertain"),"unknown server outcome has safe guidance");Require(before.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"drawer-intents.db")))&&driver.Calls==calls,"review cannot mutate journal or submit hardware");
        port.Denied=true;Invoke(window,"DrawerHistoryRefresh_Click",window,new RoutedEventArgs());Wait();Require(list.Items.Count==0&&!check.IsEnabled&&status.Text.Contains("access denied"),"permission denial clears cached observations");
        port.Denied=false;Invoke(window,"DrawerHistoryRefresh_Click",window,new RoutedEventArgs());Wait();Require(list.Items.Count==1,"authorized history can reload after denial");
        port.Unavailable=true;Invoke(window,"DrawerHistoryRefresh_Click",window,new RoutedEventArgs());Wait();Require(list.Items.Count==0&&!check.IsEnabled&&status.Text.Contains("unavailable"),"dependency outage clears history without a cached fallback");
        Require(before.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"drawer-intents.db")))&&driver.Calls==calls,"denial and outage never change drawer journal or invoke hardware");
        port.Unavailable=false;port.Hold=true;Invoke(window,"DrawerHistoryRefresh_Click",window,new RoutedEventArgs());Pump(window);Invoke(window,"ClearReceiptView");port.Release();Wait();Require(list.Items.Count==0,"late history cannot restore rows after privacy-generation change");
        var auth=(PosAuthentication)typeof(MainWindow).GetField("_authentication",Private)!.GetValue(window)!;var original=auth.CurrentToken!;
        port.Hold=true;Invoke(window,"DrawerHistoryRefresh_Click",window,new RoutedEventArgs());Pump(window);typeof(PosAuthentication).GetProperty("CurrentToken")!.SetValue(auth,original with{Subject="different-history-user"});port.Release();Wait();Require(list.Items.Count==0,"late history cannot cross a cashier change");
        typeof(PosAuthentication).GetProperty("CurrentToken")!.SetValue(auth,original);Invoke(window,"ClearDrawerHistory");Invoke(window,"UpdateOperationalState");
    }
    private sealed class DrawerHistoryFixturePort(PosClientConfiguration c):NexaConnect.POS.Hardware.Drawers.IDrawerHistoryPort
    {
        public bool Denied,Hold,Unavailable;private TaskCompletionSource<NexaConnect.Contracts.POS.DrawerHistoryPageV1>? pending;private NexaConnect.Contracts.POS.DrawerHistoryPageV1? saved;
        private readonly NexaConnect.Contracts.POS.DrawerHistoryItemV1 row=Make();
        private static NexaConnect.Contracts.POS.DrawerHistoryItemV1 Make(){var at=DateTimeOffset.UtcNow.AddMinutes(-2);return new(Guid.NewGuid(),"manual","cash_check",null,Guid.NewGuid(),Guid.NewGuid(),"reserved",at,at.AddSeconds(60),null);}
        public Task<NexaConnect.Contracts.POS.DrawerHistoryPageV1> ListAsync(string subject,DateTimeOffset from,DateTimeOffset to,string? cursor,CancellationToken ct){if(Denied)throw new UnauthorizedAccessException();if(Unavailable)throw new HttpRequestException("synthetic history outage");saved=new(c.OrganizationId,c.RestaurantId,c.BranchId,c.StoreId,c.TerminalId,from,to,[row],null);if(!Hold)return Task.FromResult(saved);pending=new(TaskCreationOptions.RunContinuationsAsynchronously);return pending.Task;}
        public Task<NexaConnect.Contracts.POS.DrawerHistoryItemV1?> ReviewAsync(string subject,Guid operation,CancellationToken ct)=>Task.FromResult<NexaConnect.Contracts.POS.DrawerHistoryItemV1?>(row with{Status="unknown",ReportedAtUtc=DateTimeOffset.UtcNow});
        public void Release(){Hold=false;pending?.TrySetResult(saved!);pending=null;}
    }
    private sealed class DrawerFixtureDriver:NexaConnect.POS.Hardware.Drawers.IDrawerDriver
    {
        public bool Enabled,Fail;public int Calls;public bool Configured=>Enabled;
        public Task SubmitOpenAsync(CancellationToken ct){Calls++;if(Fail)throw new IOException("synthetic drawer failure");return Task.CompletedTask;}
    }
    private sealed class DrawerFixtureAuthority:NexaConnect.POS.Hardware.Drawers.IDrawerAuthorization
    {
        public int Calls;public NexaConnect.POS.Hardware.Drawers.DrawerOutcome? Reported;
        public Task<NexaConnect.POS.Hardware.Drawers.DrawerPermit> AuthorizeAsync(NexaConnect.POS.Hardware.Drawers.DrawerRequest r,CancellationToken ct){Calls++;return Task.FromResult(new NexaConnect.POS.Hardware.Drawers.DrawerPermit(NexaConnect.POS.Hardware.Drawers.DrawerPermitStatus.Granted,DateTimeOffset.UtcNow.AddSeconds(60)));}
        public Task ReportAsync(NexaConnect.POS.Hardware.Drawers.DrawerRequest r,NexaConnect.POS.Hardware.Drawers.DrawerOutcome outcome,CancellationToken ct){Reported=outcome;return Task.CompletedTask;}
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
        ((TabItem)window.FindName("TerminalTab")).IsSelected = true; ((TabItem)window.FindName("TerminalDevicesTab")).IsSelected=true; Pump(window);
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
        public static int PhotoStatus=503,PhotoCalls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Require(request.Method == HttpMethod.Get, "presentation fixture must not issue financial commands");
            if(request.RequestUri!.AbsolutePath.EndsWith("/photo",StringComparison.Ordinal)){PhotoCalls++;return Task.FromResult(new HttpResponseMessage((System.Net.HttpStatusCode)PhotoStatus));}
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
