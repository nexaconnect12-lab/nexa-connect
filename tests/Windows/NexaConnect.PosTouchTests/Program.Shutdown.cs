using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using NexaConnect.POS;

internal static partial class Program
{
    private static void VerifyShutdown()
    {
        Task.Run(VerifyCallbackCancellation).GetAwaiter().GetResult();
        string root = Path.Combine(Path.GetTempPath(), "NexaConnect-Shutdown", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var config = new PosClientConfiguration("http://127.0.0.1:49199/realms/shutdown-fixture", "nexaconnect-pos",
                "nexaconnect-pos://oauth/callback", "openid", "http://127.0.0.1:49199/", "http://127.0.0.1:49199/",
                Guid.NewGuid(), Guid.NewGuid(), "THB", "cash_manual", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                CatalogApi: "http://127.0.0.1:49199/");
            File.WriteAllText(Path.Combine(root, "config.json"), JsonSerializer.Serialize(config));
            var store = new LocalPosStore(LocalPosScope.From(config), Path.Combine(root, "data"));
            var shift = new LocalShiftState(Guid.NewGuid(), "SHUTDOWN-FIXTURE", DateTimeOffset.UtcNow);
            store.SaveActiveShift(shift);
            store.SaveCashSession(new(Guid.NewGuid(), shift.ShiftId, DateTimeOffset.UtcNow));
            var checkout = PendingCheckout.Create(config, [new(Guid.NewGuid(), 1)]);
            store.SavePendingCheckout(checkout);
            store.SavePendingSettlement(new(checkout.OrderId, 120m, "THB", checkout.SettlementKey, "cash_manual", false, null, true));
            var outbox = new LocalOutboxStore(LocalPosScope.From(config), Path.Combine(root, "data"));
            outbox.Enqueue("cash-movement", $"api/pos/v1/cash-sessions/{store.LoadCashSession()!.CashSessionId:D}/movements", "POST", "{}", config.TerminalId);
            new WindowsTokenStore(Path.Combine(root, "tokens")).Save(new("", null, DateTimeOffset.MinValue, "Bearer",
                LockedAtUtc: DateTimeOffset.UtcNow, Subject: "shutdown-fixture", Authority: config.Authority));
            var expected = HashShutdownData(root);
            string mutexName = ShutdownName(root);
            // Reopen against the same original scope and financial records, never a live installation.
            foreach (string mode in new[] { "idle", "partial", "refresh", "blocked-dispose", "faulted-dispose", "blocked-cancellation", "faulted-cancellation", "display-notification", "reassigned-main", "customer-close", "signin", "journal-unavailable", "cancelled-close" })
            {
                foreach (string file in new[] { "ready", "close", "cancelled", "application-exit" }) File.Delete(Path.Combine(root, file));
                using var primary = ShutdownProcess(root, mode);
                NamedPipeClientStream? stalled = null;
                try
                {
                    WaitShutdown(() => File.Exists(Path.Combine(root, "ready")), primary);
                    using var duplicate = ShutdownProcess(root, "duplicate");
                    Require(duplicate.WaitForExit(5000) && duplicate.ExitCode == 23, "duplicate cannot open a second workspace");
                    // Retaining an unowned named-object handle must not prevent the next launch.
                    using var existingHandle = Mutex.OpenExisting(mutexName);
                    if (mode == "partial")
                    {
                        stalled = new(".", mutexName + ".Callback", PipeDirection.Out, PipeOptions.Asynchronous);
                        stalled.Connect(3000); stalled.WriteByte((byte)'x'); stalled.Flush();
                    }
                    File.WriteAllText(Path.Combine(root, "close"), "close fixture");
                    Require(primary.WaitForExit(8000) && primary.ExitCode == 0, "staff close exits even with hidden auxiliary window and stalled work ("+mode+")");
                    Require(HashShutdownData(root) == expected, "close/reopen preserves original financial SQLite bytes and queued work ("+mode+")");
                    Require(store.LoadPendingCheckout()?.OrderId == checkout.OrderId
                        && store.LoadPendingSettlement()?.IdempotencyKey == checkout.SettlementKey
                        && outbox.Load().Count == 1, "same original order, payment key and queue survive restart");
                    if (mode == "refresh") Require(File.Exists(Path.Combine(root, "cancelled")), "shutdown cancels in-flight identity transport");
                    VerifyLifecycleJournal(root,mode);
                    using var nextLease = PosSingleInstance.TryAcquire(mutexName);
                    Require(nextLease is not null, "exited primary releases mutex even while another handle exists");
                }
                catch
                {
                    if (File.Exists(Path.Combine(root, "child-error"))) Console.Error.WriteLine(File.ReadAllText(Path.Combine(root, "child-error")));
                    throw;
                }
                finally
                {
                    stalled?.Dispose();
                    if (!primary.HasExited) { primary.Kill(entireProcessTree: true); primary.WaitForExit(); }
                }
            }
            VerifyLifecycleBounds(root);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "NexaConnect-Shutdown")) + Path.DirectorySeparatorChar;
            Require(Path.GetFullPath(root).StartsWith(parent, StringComparison.OrdinalIgnoreCase), "safe fixture cleanup target");
            Directory.Delete(root, true);
        }
        Console.WriteLine("PASS: shutdown/restart — idle/stalled callback/refresh/sign-in, blocked/faulted disposal and cancellation, display notification, auxiliary ownership, lifecycle journal, mutex release and unchanged temporary protected financial recovery. No retained pilot or device operations.");
    }

    private static void VerifyLifecycleBounds(string root)
    {
        string directory=Path.Combine(root,"trace-bounds");
        for(int i=0;i<10;i++)
        {
            var trace=new PosLifecycleTrace(directory);
            for(int row=0;row<128;row++)trace.Record(PosLifecycleStage.WindowClosing);
            var writer=(Task)typeof(PosLifecycleTrace).GetField("writer",Private)!.GetValue(trace)!;
            Require(writer.Wait(TimeSpan.FromSeconds(3)),"bounded diagnostic writes finish without dispatcher access");
        }
        var files=Directory.GetFiles(directory,"*.log");Require(files.Length==8,"diagnostic retention remains bounded across processes/runs");
        Require(files.All(file=>File.ReadAllLines(file).Length==96&&new FileInfo(file).Length<65536),"excess lifecycle records are dropped without growing files");
    }

    private static void VerifyLifecycleJournal(string root,string mode)
    {
        if(mode=="journal-unavailable"){Require(File.ReadAllText(Path.Combine(root,"traces",mode))=="not-directory","diagnostic failure cannot alter recovery or prevent exit");return;}
        var files=Directory.GetFiles(Path.Combine(root,"traces",mode),"*.log");Require(files.Length==1,"one isolated lifecycle journal per process");
        var rows=File.ReadAllLines(files[0]).Select(line=>JsonDocument.Parse(line)).ToArray();
        try
        {
            var allowed=new[]{"version","service","category","processId","stage","outcome","elapsedMilliseconds","observedAtUtc"};
            Require(rows.Length is >0 and <=96&&new FileInfo(files[0]).Length<65536,"bounded lifecycle journal");
            Require(rows.All(r=>r.RootElement.EnumerateObject().Select(p=>p.Name).Order().SequenceEqual(allowed.Order())),"journal only contains typed diagnostic fields, no business or identity data");
            bool Has(string stage,string outcome)=>rows.Any(r=>r.RootElement.GetProperty("stage").GetString()==stage&&r.RootElement.GetProperty("outcome").GetString()==outcome);
            Require(Has("WorkspaceOpen","Complete")&&Has("WindowClosing","Begin")&&Has("WindowClosed","Complete")&&Has("ApplicationExit","Complete"),"journal reaches application exit without treating it as operator acceptance");
            if(mode=="cancelled-close")Require(Has("WindowClosing","Pending"),"cancelled close is diagnosed without shutting down");
            if(mode=="faulted-dispose")Require(Has("ApiClients","Fault"),"faulted disposal is diagnosed without skipping exit");
            if(mode=="faulted-cancellation")Require(Has("WindowCancellation","Fault"),"faulted cancellation is diagnosed without skipping exit");
        }
        finally{foreach(var row in rows)row.Dispose();}
    }

    private static string HashShutdownData(string root) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "data", "pos-state.db"))));
    private static string ShutdownName(string root) => "NexaConnect.UX12." + Path.GetFileName(root);
    private static Process ShutdownProcess(string root, string mode)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
        foreach (string argument in new[] { "--shutdown-child", root, mode }) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new InvalidOperationException("Fixture child did not start.");
    }
    private static void WaitShutdown(Func<bool> ready, Process process)
    {
        var watch = Stopwatch.StartNew();
        while (!ready() && !process.HasExited && watch.Elapsed < TimeSpan.FromSeconds(8)) Thread.Sleep(20);
        Require(ready() && !process.HasExited, "isolated process reaches ready state (" + (process.HasExited ? "exit " + process.ExitCode : "timeout") + ")");
    }
    private static void RunShutdownChild(string root, string mode)
    {
        try
        {
            var app = new ShutdownApplication(root, mode);
            Environment.ExitCode = app.Run();
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(root, "child-error"), error.ToString()); throw; }
    }
    private sealed class ShutdownApplication(string root, string mode) : App
    {
        private PosSingleInstance? lease;
        private readonly DispatcherTimer closeTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
        protected override void OnStartup(StartupEventArgs e)
        {
            // Deliberately bypass live config, protocol registration and all real named objects.
            lease = PosSingleInstance.TryAcquire(ShutdownName(root));
            if (lease is null) { Shutdown(23); return; }
            Require(mode != "duplicate", "unexpected duplicate workspace");
            Require(ShutdownMode == ShutdownMode.OnMainWindowClose, "production application exits on staff close");
            var config = JsonSerializer.Deserialize<PosClientConfiguration>(File.ReadAllText(Path.Combine(root, "config.json")))!;
            var identity = new ShutdownIdentity(root);
            var auth = new PosAuthentication(config, identity, new WindowsTokenStore(Path.Combine(root, "tokens")), _ => { });
            Require(auth.RequiresInteractiveSignIn && auth.CurrentToken?.Subject == "shutdown-fixture", "reopening stays locked to original cashier");
            HttpMessageHandler business = mode is "blocked-dispose" or "faulted-dispose" ? new ShutdownDisposalHandler(root,mode) : new SessionNoBusinessHandler();
            var window = new MainWindow(auth, new PosApiClient(config, business, business, business, business),
                new LocalPosStore(LocalPosScope.From(config), Path.Combine(root, "data")),
                new LocalOutboxStore(LocalPosScope.From(config), Path.Combine(root, "data")), config,
                new PosUiPreferences(Path.Combine(root, "preferences")), new ReceiptPrinterPreferences(Path.Combine(root, "printers")),
                new CancelPrintDriver(), () => Task.FromResult(Array.Empty<string>()), new PosTerminalDiagnostics(config, business))
                { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
            var traceDirectory=Path.Combine(root,"traces",mode);
            if(mode=="journal-unavailable"){Directory.CreateDirectory(Path.GetDirectoryName(traceDirectory)!);File.WriteAllText(traceDirectory,"not-directory");}
            OpenWorkspace(window, auth, ShutdownName(root) + ".Callback",new PosLifecycleTrace(traceDirectory));
            var auxiliary=new Window { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
            auxiliary.Show();
            if(mode=="reassigned-main")MainWindow=auxiliary;
            if(mode=="customer-close")auxiliary.Close();
            if(mode is "blocked-cancellation" or "faulted-cancellation")
            {
                var cancellation=(CancellationTokenSource)typeof(MainWindow).GetField("windowCancellation",Private)!.GetValue(window)!;
                cancellation.Token.Register(()=>{
                    if(mode=="faulted-cancellation")throw new IOException("synthetic cancellation fault");
                    var deadline=Stopwatch.StartNew();
                    while(!File.Exists(Path.Combine(root,"application-exit"))&&deadline.Elapsed<TimeSpan.FromSeconds(30))Thread.Sleep(10);
                });
            }
            if(mode=="cancelled-close"){bool cancelled=false;window.Closing+=(_,e)=>{if(!cancelled){cancelled=true;e.Cancel=true;}};}
            if(mode=="signin")_ = auth.SignInAsync(true).ContinueWith(t=>_ = t.Exception,TaskScheduler.Default);
            if (mode == "refresh")
            {
                typeof(PosAuthentication).GetProperty("CurrentToken")!.SetValue(auth,
                    new PosTokenSet("fixture", "fixture", DateTimeOffset.UtcNow.AddMinutes(5), "Bearer", DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow.AddHours(1), Subject: "shutdown-fixture", Authority: config.Authority));
                typeof(PosAuthentication).GetField("_requiresInteractiveSignIn", Private)!.SetValue(auth, false);
                _ = auth.RefreshAsync().ContinueWith(task => _ = task.Exception, TaskScheduler.Default);
            }
            closeTimer.Tick += (_, _) => { if (File.Exists(Path.Combine(root, "close"))) { closeTimer.Stop();
                if(mode=="display-notification"){var coordinator=typeof(MainWindow).GetField("displays",Private)!.GetValue(window)!;var notification=Task.Run(()=>coordinator.GetType().GetMethod("Changing",Private)!.Invoke(coordinator,[null,EventArgs.Empty]));Require(notification.Wait(1000),"display notification never waits on the closing dispatcher");}
                window.Close();
                if(mode=="cancelled-close"){Require(window.IsVisible&&!Dispatcher.HasShutdownStarted,"cancelled close retains the workspace");window.Close();}
            } };
            closeTimer.Start();
            File.WriteAllText(Path.Combine(root, "ready"), "ready");
        }
        protected override void OnExit(ExitEventArgs e)
        {
            closeTimer.Stop();
            File.WriteAllText(Path.Combine(root,"application-exit"),"exit");
            base.OnExit(e);
            lease?.Dispose();
        }
    }
    private sealed class ShutdownDisposalHandler(string root,string mode):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>throw new InvalidOperationException("No business calls allowed");
        protected override void Dispose(bool disposing)
        {
            if(disposing&&mode=="faulted-dispose")throw new IOException("synthetic disposal fault");
            if(disposing&&mode=="blocked-dispose")
            {
                var deadline=Stopwatch.StartNew();
                while(!File.Exists(Path.Combine(root,"application-exit"))&&deadline.Elapsed<TimeSpan.FromSeconds(30))Thread.Sleep(10);
            }
            base.Dispose(disposing);
        }
    }
    private sealed class ShutdownIdentity(string root) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            try { await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { File.WriteAllText(Path.Combine(root, "cancelled"), "cancelled"); throw; }
            throw new InvalidOperationException("Unreachable");
        }
    }
    private static async Task VerifyCallbackCancellation()
    {
        string pipe = "NexaConnect.UX12.Pipe." + Guid.NewGuid().ToString("N");
        using var lifetime = new CancellationTokenSource();
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var listener = PosCallbackListener.RunAsync(pipe, (text, _) => { received.TrySetResult(text); return Task.CompletedTask; }, lifetime.Token, TimeSpan.FromMilliseconds(200));
        using (var stalled = new NamedPipeClientStream(".", pipe, PipeDirection.Out, PipeOptions.Asynchronous))
        {
            await stalled.ConnectAsync(3000);
            await stalled.WriteAsync(Encoding.UTF8.GetBytes("partial")); await stalled.FlushAsync();
            await Task.Delay(350);
            Require(!received.Task.IsCompleted, "incomplete callback expires without dispatch");
        }
        using (var oversized = new NamedPipeClientStream(".", pipe, PipeDirection.Out, PipeOptions.Asynchronous))
        {
            await oversized.ConnectAsync(3000);
            await oversized.WriteAsync(Encoding.UTF8.GetBytes(new string('x', 4097) + "\n")); await oversized.FlushAsync();
        }
        using (var valid = new NamedPipeClientStream(".", pipe, PipeDirection.Out, PipeOptions.Asynchronous))
        {
            await valid.ConnectAsync(3000);
            await valid.WriteAsync(Encoding.UTF8.GetBytes("fixture\r\n")); await valid.FlushAsync();
        }
        Require(await received.Task.WaitAsync(TimeSpan.FromSeconds(3)) == "fixture", "listener rejects oversized peer and accepts next bounded callback");
        lifetime.Cancel();
        await listener.WaitAsync(TimeSpan.FromSeconds(2));
        // Cancellation while connected and waiting for newline, not just while accepting.
        using var partialLifetime = new CancellationTokenSource();
        var partial = PosCallbackListener.RunAsync(pipe, (_, _) => throw new InvalidOperationException("No incomplete dispatch"), partialLifetime.Token);
        using var peer = new NamedPipeClientStream(".", pipe, PipeDirection.Out, PipeOptions.Asynchronous);
        await peer.ConnectAsync(3000); await peer.WriteAsync(new byte[] { 120 }); await peer.FlushAsync();
        partialLifetime.Cancel();
        await partial.WaitAsync(TimeSpan.FromSeconds(2));
    }
}
