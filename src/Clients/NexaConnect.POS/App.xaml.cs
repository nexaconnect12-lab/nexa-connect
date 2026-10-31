using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace NexaConnect.POS;

public partial class App : Application
{
    private const string MutexName = "NexaConnect.POS.SingleInstance";
    private const string CallbackPipeName = "NexaConnect.POS.Callback";
    private PosSingleInstance? instance;
    private PosAuthentication? _authentication;
    private readonly CancellationTokenSource lifetime = new();
    private Task callbackListener = Task.CompletedTask;
    private PosLifecycleTrace lifecycle = PosLifecycleTrace.Disabled;
    private bool workspaceShutdownRequested;

    public App() => ShutdownMode = ShutdownMode.OnMainWindowClose;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length > 0 && e.Args[0] == "--validate-package")
        {
            try
            {
                if (e.Args is not [_, _, _, _]) throw new InvalidDataException("Invalid preflight arguments.");
                PosInstallationPolicy.AssertManagedDirectory(e.Args[3]);
                PosInstallationPolicy.Preflight(e.Args[1], e.Args[2] == "-" ? null : e.Args[2]);
                Shutdown(0);
            }
            catch (Exception) { Shutdown(2); }
            return;
        }
        // Installer preflight is read-only and never registers callbacks, opens the
        // workspace, starts authentication or migrates/deletes recovery state.
        if (e.Args is ["--validate-install"])
        {
            try { LocalPosDatabase.ValidateExistingScope(PosClientConfiguration.Load()); Shutdown(0); }
            catch (Exception) { Shutdown(2); }
            return;
        }
        instance = PosSingleInstance.TryAcquire(MutexName);
        if (instance is null)
        {
            if (e.Args.Length > 0)
            {
                ForwardCallback(e.Args[0]);
            }

            Shutdown();
            return;
        }

        base.OnStartup(e);
        // Setup holds maintenance before checking the POS mutex. Creating our POS mutex
        // first closes the startup/update race; existing-instance callbacks still forward.
        try
        {
            if (Mutex.TryOpenExisting(PosInstallationPolicy.MaintenanceMutex, out var maintenance))
            {
                maintenance.Dispose();
                MessageBox.Show("Terminal maintenance is in progress. Finish or cancel setup before opening POS.", "POS maintenance");
                Shutdown(); return;
            }
        }
        catch (UnauthorizedAccessException) { Shutdown(2); return; }
        PosClientConfiguration configuration;
        try { configuration = PosClientConfiguration.Load(); }
        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException or InvalidOperationException)
        {
            MessageBox.Show("POS configuration is invalid. Check service URLs, branch/terminal identifiers and THB manual checkout settings before starting.",
                "Terminal setup required", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown();
            return;
        }
        MainWindow window;
        try
        {
            // Check retained scope read-only before creating or replacing session metadata.
            LocalPosDatabase.ValidateExistingScope(configuration);
            string storageDirectory = configuration.GetStorageDirectory();
            lifecycle = new PosLifecycleTrace(Path.Combine(storageDirectory,"diagnostics"));
            lifecycle.Record(PosLifecycleStage.Startup);
            _authentication = new PosAuthentication(configuration);
            var scope = LocalPosScope.From(configuration);
            window = new MainWindow(
                _authentication,
                new PosApiClient(configuration),
                new LocalPosStore(scope, storageDirectory),
                new LocalOutboxStore(scope, storageDirectory),
                configuration);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException
            or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            MessageBox.Show(
                "Local POS recovery could not be opened safely. Preserve this terminal's data under %LOCALAPPDATA%\\NexaConnect for reconciliation and contact support before taking another order.",
                "Recovery required", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }
        // A wrong-scope installation must not take over the original terminal's callback.
        try { EnsureProtocolRegistration(); }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            MessageBox.Show("The sign-in callback could not be registered for this Windows account. Repair the draft installation before signing in.",
                "Terminal setup required", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(); return;
        }
        OpenWorkspace(window, _authentication);
        if (e.Args.Length > 0)
        {
            _ = _authentication.HandleCallbackAsync(e.Args[0]);
        }
    }

    // Also exercised by the isolated process fixture, without real onboarding/registry I/O.
    internal void OpenWorkspace(Window window, PosAuthentication authentication,
        string pipeName = CallbackPipeName, PosLifecycleTrace? trace = null)
    {
        _authentication = authentication;
        lifecycle=trace??lifecycle;
        if(window is MainWindow staff)staff.Lifecycle=lifecycle;
        window.Closed+=WorkspaceClosed;
        MainWindow = window;
        callbackListener = PosCallbackListener.RunAsync(pipeName, async (callback, ct) =>
        {
            // Pipe I/O never runs on the dispatcher. Only authentication/UI state does.
            await Dispatcher.InvokeAsync(() => authentication.HandleCallbackAsync(callback, ct),
                System.Windows.Threading.DispatcherPriority.Normal, ct).Task.Unwrap().ConfigureAwait(false);
        }, lifetime.Token);
        window.Show();
        lifecycle.Record(PosLifecycleStage.WorkspaceOpen);
    }

    private static void EnsureProtocolRegistration()
    {
        string? executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return;
        }

        using RegistryKey protocol = Registry.CurrentUser.CreateSubKey(
            @"Software\Classes\nexaconnect-pos\shell\open\command");
        protocol.SetValue(string.Empty, $"\"{executablePath}\" \"%1\"");
        using RegistryKey root = Registry.CurrentUser.CreateSubKey(@"Software\Classes\nexaconnect-pos");
        root.SetValue(string.Empty, "URL:NexaConnect POS callback");
        root.SetValue("URL Protocol", string.Empty);
    }

    private void WorkspaceClosed(object? sender, EventArgs e)
    {
        if(workspaceShutdownRequested)return;
        workspaceShutdownRequested=true;
        // The actual staff workspace owns lifetime even if another window replaced MainWindow.
        lifecycle.Record(PosLifecycleStage.WindowClosed);
        if(!Dispatcher.HasShutdownStarted)Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        lifecycle.Record(PosLifecycleStage.ApplicationExit,PosLifecycleOutcome.Begin);
        try
        {
            lifecycle.Cancel(PosLifecycleStage.ApplicationCancellation,lifetime);
            _ = callbackListener.ContinueWith(task => _ = task.Exception,
                CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted,TaskScheduler.Default);
            lifecycle.Step(PosLifecycleStage.AuthenticationFence,()=>_authentication?.StopForShutdown());
            lifecycle.Background(PosLifecycleStage.AuthenticationDispose,()=>_authentication?.Dispose());
            lifecycle.Step(PosLifecycleStage.InstanceRelease,()=>instance?.Dispose());
            lifecycle.Drain();
        }
        finally
        {
            base.OnExit(e);
            lifecycle.Record(PosLifecycleStage.ApplicationExit);
            lifecycle.Flush();
        }
    }

    private static void ForwardCallback(string callback)
    {
        try
        {
            if (callback.Length > 4096) return;
            using var client = new System.IO.Pipes.NamedPipeClientStream(".", CallbackPipeName, System.IO.Pipes.PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client, System.Text.Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine(callback);
        }
        catch (TimeoutException)
        {
            // The primary process may be starting; the callback will be retried by the browser.
        }
        catch (IOException)
        {
            // Do not display callback contents or tokens in an error dialog.
        }
    }
}
