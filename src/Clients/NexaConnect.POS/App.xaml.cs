using System.IO.Pipes;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace NexaConnect.POS;

public partial class App : Application
{
    private const string MutexName = "NexaConnect.POS.SingleInstance";
    private const string CallbackPipeName = "NexaConnect.POS.Callback";
    private Mutex? _mutex;
    private bool _ownsMutex;
    private PosAuthentication? _authentication;

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
        _mutex = new Mutex(true, MutexName, out bool ownsMutex);
        _ownsMutex = ownsMutex;
        if (!ownsMutex)
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
        MainWindow = window;
        _ = ListenForCallbacksAsync(_authentication, CancellationToken.None);
        window.Show();
        if (e.Args.Length > 0)
        {
            _ = _authentication.HandleCallbackAsync(e.Args[0]);
        }
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

    protected override void OnExit(ExitEventArgs e)
    {
        _authentication?.Dispose();
        if (_ownsMutex) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private static async Task ListenForCallbacksAsync(
        PosAuthentication authentication,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    CallbackPipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(server, Encoding.UTF8);
                var callback = new StringBuilder(capacity: 256);
                while (callback.Length <= 4096)
                {
                    int value = reader.Read();
                    if (value < 0 || value == '\n')
                    {
                        break;
                    }

                    callback.Append((char)value);
                }

                if (callback.Length <= 4096 && callback.Length > 0)
                {
                    await authentication.HandleCallbackAsync(callback.ToString().TrimEnd('\r'));
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                // A second instance can disappear while forwarding its callback.
            }
        }
    }

    private static void ForwardCallback(string callback)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", CallbackPipeName, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
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
