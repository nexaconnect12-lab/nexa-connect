using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NexaConnect.POS;

internal static partial class Program
{
    private static void VerifySessionExpiry()
    {
        using (var fixture = new SessionFixture())
        {
            var button = (Button)fixture.Window.FindName("LockWorkspaceButton");
            Require(button.IsEnabled, "signed-in cashier has a direct lock action");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            fixture.AssertLocked(); fixture.AssertRecovery();
            Require(fixture.Identity.RefreshCalls == 0, "manual lock does not silently refresh or send commands");
        }
        using (var fixture = new SessionFixture())
        {
            fixture.SetToken(fixture.Token with { SessionExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1) });
            fixture.Tick(); fixture.AssertLocked();
            Require(fixture.Identity.RefreshCalls == 0, "absolute session expiry cannot silently renew");
            fixture.AssertRecovery(); fixture.AssertRestartLocked();
            fixture.Resume("cancel"); fixture.AssertLocked(); fixture.AssertRecovery();
            fixture.Resume("wrong-cashier"); fixture.AssertLocked(); fixture.AssertRecovery();
            Require(fixture.Auth.CurrentToken!.Subject == "session-fixture", "wrong cashier cannot replace retained operator");
            fixture.Resume("same-cashier");
            Require(!fixture.Auth.RequiresInteractiveSignIn && fixture.Surface.Visibility == Visibility.Visible && fixture.Surface.IsEnabled,
                "original cashier interactive fallback restores private workspace");
            fixture.AssertRecovery();
        }
        using (var fixture = new SessionFixture())
        {
            fixture.SetToken(fixture.Token with { ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1), RefreshToken = null });
            fixture.Tick(); fixture.AssertLocked(); fixture.AssertRecovery();
            Require(fixture.Identity.RefreshCalls == 0, "expired access without refresh goes directly to fallback");
        }
        foreach (var status in new[] { HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized })
        {
            using var fixture = new SessionFixture();
            fixture.Identity.RefreshStatus = status;
            fixture.SetToken(fixture.Token with { ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(30) });
            fixture.Tick(); fixture.AssertLocked(); fixture.AssertRecovery();
            Require(fixture.Identity.RefreshCalls == 1, "provider session rejection does not retry silently");
        }
        using (var fixture = new SessionFixture())
        {
            fixture.Identity.RefreshStatus = HttpStatusCode.ServiceUnavailable;
            fixture.SetToken(fixture.Token with { ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(30) });
            fixture.Tick();
            Require(!fixture.Auth.RequiresInteractiveSignIn && fixture.Surface.Visibility == Visibility.Visible,
                "temporary refresh outage preserves still-valid access");
            fixture.Tick();
            Require(fixture.Identity.RefreshCalls == 1, "refresh outage has backoff rather than a retry storm");
            fixture.SetToken(fixture.Token with { ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1) });
            typeof(MainWindow).GetField("nextTokenRefreshAttemptUtc", Private)!.SetValue(fixture.Window, DateTimeOffset.MinValue);
            fixture.Tick(); fixture.AssertLocked(); fixture.AssertRecovery();
        }
        using (var fixture = new SessionFixture())
        {
            var original = fixture.Token with { ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(30) };
            fixture.SetToken(original); fixture.Tick();
            Require(fixture.Identity.RefreshCalls == 1 && fixture.Identity.UserInfoCalls == 1 && !fixture.Auth.RequiresInteractiveSignIn,
                "successful refresh verifies the same provider operator");
            Require(fixture.Auth.CurrentToken!.SessionExpiresAtUtc == original.SessionExpiresAtUtc,
                "silent refresh never extends the absolute session boundary");
            fixture.AssertRecovery();
        }
        Console.WriteLine("PASS: isolated session expiry/fallback — absolute expiry, unavailable refresh, 400/401 denial, backoff, restart lock, cancellation, wrong-cashier denial and original-cashier resume; synthetic identity and temporary DPAPI recovery only.");
    }

    private sealed class SessionFixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "NexaConnect-SessionExpiry", Guid.NewGuid().ToString("N"));
        private readonly object tokenStore;
        private readonly PosApiClient api;
        private readonly SessionNoBusinessHandler business = new();
        private readonly LocalPosStore store;
        private readonly LocalShiftState shift;
        private readonly LocalCashSessionState cash;
        private readonly LocalPendingSettlementState pending;
        private readonly string financialHash;
        private readonly PosClientConfiguration configuration;
        private string? authorizeUri;
        public SessionIdentityHandler Identity { get; } = new();
        public PosAuthentication Auth { get; }
        public MainWindow Window { get; }
        public FrameworkElement Surface => (FrameworkElement)Window.FindName("OperationalSurface");
        public PosTokenSet Token { get; }
        public SessionFixture()
        {
            Directory.CreateDirectory(root);
            configuration = new("http://127.0.0.1:49199/realms/session-fixture", "nexaconnect-pos", "nexaconnect-pos://oauth/callback", "openid",
                "http://127.0.0.1:49199/", "http://127.0.0.1:49199/", Guid.NewGuid(), Guid.NewGuid(), "THB", "cash_manual", null,
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CatalogApi: "http://127.0.0.1:49199/");
            tokenStore = Activator.CreateInstance(typeof(MainWindow).Assembly.GetType("NexaConnect.POS.WindowsTokenStore")!, Private | BindingFlags.Public, null, [Path.Combine(root, "tokens")], null)!;
            Auth = NewAuthentication();
            Token = new("fixture-access", "fixture-refresh", DateTimeOffset.UtcNow.AddMinutes(10), "Bearer", DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddHours(1), Subject: "session-fixture", Authority: configuration.Authority);
            SetToken(Token);
            var scope = LocalPosScope.From(configuration);
            string data = Path.Combine(root, "data");
            store = new(scope, data);
            shift = new(Guid.NewGuid(), "SESSION-FIXTURE", DateTimeOffset.UtcNow);
            cash = new(Guid.NewGuid(), shift.ShiftId, DateTimeOffset.UtcNow);
            pending = new(Guid.NewGuid(), 120m, "THB", Guid.NewGuid(), "promptpay_manual", true, "fixture-proof", true);
            store.SaveActiveShift(shift); store.SaveCashSession(cash); store.SavePendingSettlement(pending);
            api = new(configuration, business, business, business, business);
            var drawer = new NexaConnect.POS.Hardware.Drawers.DrawerCoordinator(new(new NexaConnect.POS.Hardware.Drawers.DisabledDrawerDriver(),
                new DrawerFixtureAuthority(), new NexaConnect.POS.Hardware.Drawers.SqliteDrawerIntentStore(Path.Combine(root, "drawer.db"))));
            var history = new NexaConnect.POS.Hardware.Drawers.DrawerHistoryReader(configuration, new DrawerHistoryFixturePort(configuration),
                new NexaConnect.POS.Hardware.Drawers.SqliteDrawerIntentStore(Path.Combine(root, "drawer.db"), configuration.Authority));
            Window = new(Auth, api, store, new LocalOutboxStore(scope, data), configuration, new PosUiPreferences(Path.Combine(root, "preferences")),
                new ReceiptPrinterPreferences(Path.Combine(root, "printers")), new CancelPrintDriver(), () => Task.FromResult(Array.Empty<string>()),
                new PosTerminalDiagnostics(configuration, business), drawer, history)
                { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
            foreach (string timer in new[] { "sessionTimer", "orderListTimer", "galleryTimer", "promptPayQrTimer" })
                ((DispatcherTimer)typeof(MainWindow).GetField(timer, Private)!.GetValue(Window)!).Stop();
            Window.Show(); Pump(Window);
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Window.Dispatcher));
            financialHash = HashData();
        }
        private PosAuthentication NewAuthentication() => (PosAuthentication)Activator.CreateInstance(typeof(PosAuthentication), Private, null,
            [configuration, Identity, tokenStore, (Action<string>)(uri => authorizeUri = uri)], null)!;
        public void SetToken(PosTokenSet value)
        {
            typeof(PosAuthentication).GetProperty("CurrentToken")!.SetValue(Auth, value);
            tokenStore.GetType().GetMethod("Save")!.Invoke(tokenStore, [value]);
        }
        public void Tick()
        {
            Invoke(Window, "SessionTimer_Tick", Window, EventArgs.Empty);
            Wait(() => !(bool)typeof(MainWindow).GetField("tokenRefreshInFlight", Private)!.GetValue(Window)!);
        }
        public void AssertLocked()
        {
            Require(Auth.RequiresInteractiveSignIn && Surface.Visibility == Visibility.Hidden && !Surface.IsEnabled,
                "expiry hides and disables private work");
            Require(((FrameworkElement)Window.FindName("SessionLockOverlay")).Visibility == Visibility.Visible,
                "expiry shows the lock surface");
            Require(!((Button)Window.FindName("PaidButton")).IsEnabled && !((Button)Window.FindName("PlaceOrderButton")).IsEnabled,
                "locked financial actions are disabled");
            var marker = (PosTokenSet)tokenStore.GetType().GetMethod("Load")!.Invoke(tokenStore, null)!;
            Require(marker.LockedAtUtc is not null && marker.Subject == "session-fixture" && marker.Authority == configuration.Authority
                && marker.AccessToken == "" && marker.RefreshToken is null, "protected lock marker retains owner without usable credentials");
        }
        public void AssertRecovery()
        {
            Require(store.LoadActiveShift() == shift && store.LoadCashSession() == cash && store.LoadPendingSettlement() == pending,
                "session transitions retain exact custody and original pending method/proof/idempotency");
            Require(financialHash == HashData(), "session transitions do not write the financial store");
            Require(business.Calls == 0, "session transitions send no business/device requests");
        }
        public void AssertRestartLocked()
        {
            // Do not share/dispose the first HttpClient handler when constructing the restarted auth.
            using var restarted = (PosAuthentication)Activator.CreateInstance(typeof(PosAuthentication), Private, null,
                [configuration, new SessionIdentityHandler(), tokenStore, (Action<string>)(_ => throw new InvalidOperationException("No browser process in fixture"))], null)!;
            Require(restarted.RequiresInteractiveSignIn && restarted.CurrentToken?.Subject == "session-fixture", "restart preserves locked original cashier");
        }
        public void Resume(string result)
        {
            authorizeUri = null;
            Identity.Subject = result == "wrong-cashier" ? "other-fixture-cashier" : "session-fixture";
            Invoke(Window, "RevealLockPrompt");
            var resume = (Button)Window.FindName("ResumeButton");
            Require(resume.IsEnabled && ((FrameworkElement)Window.FindName("LockAuthenticationPanel")).Visibility == Visibility.Visible,
                "interaction exposes enabled resume authentication");
            resume.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Wait(() => authorizeUri is not null);
            var query = ParseSessionQuery(new Uri(authorizeUri!).Query);
            Require(query["prompt"] == "login" && query["code_challenge_method"] == "S256" && query["state"].Length > 20,
                "fallback requests interactive PKCE sign-in");
            string callback = "nexaconnect-pos://oauth/callback?state=" + Uri.EscapeDataString(query["state"]) +
                (result == "cancel" ? "&error=access_denied" : "&code=fixture-code");
            var task = Auth.HandleCallbackAsync(callback); Wait(() => task.IsCompleted); task.GetAwaiter().GetResult();
            Wait(() => typeof(MainWindow).GetField("signInCancellation", Private)!.GetValue(Window) is null);
        }
        private string HashData() => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "data", "pos-state.db"))));
        private void Wait(Func<bool> condition)
        {
            var deadline = Stopwatch.StartNew();
            do { Pump(Window); if (condition()) return; Thread.Sleep(5); } while (deadline.Elapsed < TimeSpan.FromSeconds(5));
            throw new InvalidOperationException("Session fixture did not reach its bounded expected state.");
        }
        public void Dispose()
        {
            Window.Close(); Auth.Dispose(); api.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "NexaConnect-SessionExpiry")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(root).StartsWith(parent, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Session fixture cleanup target escaped its temporary parent.");
            Directory.Delete(root, true);
        }
    }
    private static Dictionary<string, string> ParseSessionQuery(string query) => query.TrimStart('?').Split('&')
        .Select(part => part.Split('=', 2)).ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]));
    private sealed class SessionNoBusinessHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; throw new InvalidOperationException("Session fixture must not call any business/device endpoint."); }
    }
    private sealed class SessionIdentityHandler : HttpMessageHandler
    {
        public HttpStatusCode RefreshStatus { get; set; } = HttpStatusCode.OK;
        public string Subject { get; set; } = "session-fixture";
        public int RefreshCalls { get; private set; }
        public int UserInfoCalls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host != "127.0.0.1" || request.RequestUri.Port != 49199) throw new InvalidOperationException("Unexpected fixture identity host.");
            if (request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath.EndsWith("/userinfo"))
            { UserInfoCalls++; return Json(new { sub = Subject }); }
            if (request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath.EndsWith("/token"))
            {
                var form = ParseSessionQuery(await request.Content!.ReadAsStringAsync(cancellationToken));
                if (form["grant_type"] == "refresh_token")
                { RefreshCalls++; if (RefreshStatus != HttpStatusCode.OK) return new(RefreshStatus); }
                else if (form["grant_type"] != "authorization_code") throw new InvalidOperationException("Unexpected fixture identity grant.");
                return Json(new { access_token = "fixture-refreshed-access", refresh_token = "fixture-refreshed-refresh", expires_in = 3600, token_type = "Bearer" });
            }
            throw new InvalidOperationException("Unexpected fixture identity request.");
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }
}
