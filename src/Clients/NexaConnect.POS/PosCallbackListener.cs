using System.IO;
using System.IO.Pipes;
using System.Text;

namespace NexaConnect.POS;

/// <summary>Current-user IPC only; never logs a callback or grants sign-in authority.</summary>
internal static class PosCallbackListener
{
    internal static async Task RunAsync(string pipeName, Func<string, CancellationToken, Task> callback,
        CancellationToken lifetime, TimeSpan? readTimeout = null)
    {
        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(lifetime).ConfigureAwait(false);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
                deadline.CancelAfter(readTimeout ?? TimeSpan.FromSeconds(5));
                using var reader = new StreamReader(server, Encoding.UTF8);
                var text = new StringBuilder(256);
                var character = new char[1];
                while (text.Length <= 4096)
                {
                    int count = await reader.ReadAsync(character.AsMemory(), deadline.Token).ConfigureAwait(false);
                    if (count == 0 || character[0] == '\n') break;
                    text.Append(character[0]);
                }
                if (text.Length is > 0 and <= 4096 && !lifetime.IsCancellationRequested)
                    await callback(text.ToString().TrimEnd('\r'), lifetime).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
            catch (OperationCanceledException) { /* Stalled peer: close it and accept the next callback. */ }
            catch (IOException) { /* A forwarding process may disappear. Never echo callback contents. */ }
        }
    }
}
