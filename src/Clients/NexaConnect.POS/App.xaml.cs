using System.IO.Pipes;
using System.IO;
using System.Text;
using System.Windows;

namespace NexaConnect.POS;

public partial class App : Application
{
    private const string MutexName = "NexaConnect.POS.SingleInstance";
    private const string CallbackPipeName = "NexaConnect.POS.Callback";
    private Mutex? _mutex;
    private PosAuthentication? _authentication;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, MutexName, out bool ownsMutex);
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
        PosClientConfiguration configuration = PosClientConfiguration.Load();
        _authentication = new PosAuthentication(configuration);
        _ = ListenForCallbacksAsync(_authentication, CancellationToken.None);

        var window = new MainWindow(_authentication);
        MainWindow = window;
        window.Show();
        if (e.Args.Length > 0)
        {
            _ = _authentication.HandleCallbackAsync(e.Args[0]);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _authentication?.Dispose();
        _mutex?.ReleaseMutex();
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
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(server, Encoding.UTF8);
                string? callback = await reader.ReadLineAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(callback))
                {
                    await authentication.HandleCallbackAsync(callback);
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
