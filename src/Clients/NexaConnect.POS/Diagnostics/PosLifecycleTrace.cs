using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace NexaConnect.POS;

internal enum PosLifecycleStage
{
    Startup, WorkspaceOpen, WindowClosing, WindowClosed, WindowCancellation, SignInCancellation,
    Timers, MenuPhotos, MenuOrganization, ApiClients, Diagnostics, Drawer, DrawerHistory, Displays, ApplicationCancellation,
    AuthenticationFence, AuthenticationDispose, InstanceRelease, ApplicationExit, BackgroundDrain
}
internal enum PosLifecycleOutcome { Begin, Complete, Fault, Pending }

// Bounded, allow-listed local diagnostics. Never serialize an exception, context or request.
internal sealed class PosLifecycleTrace(string? directory = null)
{
    internal static PosLifecycleTrace Disabled => new();
    private readonly object gate = new();
    private readonly List<Task> cleanup = [];
    private Task writer = Task.CompletedTask;
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private readonly string fileName = $"pos-lifecycle-{Environment.ProcessId}-{Guid.NewGuid():N}.log";
    private int records;
    private bool initialized;

    internal void Record(PosLifecycleStage stage, PosLifecycleOutcome outcome = PosLifecycleOutcome.Complete)
    {
        if (directory is null || Interlocked.Increment(ref records) > 96) return;
        var line = JsonSerializer.Serialize(new { version = 1, service = "nexaconnect-pos-client", category = "lifecycle",
            processId = Environment.ProcessId, stage = stage.ToString(), outcome = outcome.ToString(),
            elapsedMilliseconds = elapsed.ElapsedMilliseconds, observedAtUtc = DateTimeOffset.UtcNow });
        lock (gate) writer = writer.ContinueWith(_ => Write(line), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }
    internal void Step(PosLifecycleStage stage, Action action)
    {
        Record(stage, PosLifecycleOutcome.Begin);
        try { action(); Record(stage); }
        catch (Exception) { Record(stage, PosLifecycleOutcome.Fault); }
    }
    internal void Background(PosLifecycleStage stage, Action action)
    {
        var task = Task.Run(() => Step(stage, action));
        lock (gate) cleanup.Add(task);
    }
    internal void Cancel(PosLifecycleStage stage, CancellationTokenSource? source)
    {
        if (source is null) return;
        Record(stage, PosLifecycleOutcome.Begin);
        try
        {
            // Cancellation is requested immediately; callbacks cannot block the dispatcher.
            var task = source.CancelAsync().ContinueWith(done => {
                if (done.IsFaulted) { _ = done.Exception; Record(stage, PosLifecycleOutcome.Fault); }
                else Record(stage);
            }, TaskScheduler.Default);
            lock (gate) cleanup.Add(task);
        }
        catch (Exception) { Record(stage, PosLifecycleOutcome.Fault); }
    }
    internal void Drain()
    {
        Task[] tasks; lock (gate) tasks = cleanup.ToArray();
        // One overall grace period, never an unbounded wait or one timeout per dependency.
        Record(PosLifecycleStage.BackgroundDrain, Task.WaitAll(tasks, TimeSpan.FromMilliseconds(500))
            ? PosLifecycleOutcome.Complete : PosLifecycleOutcome.Pending);
    }
    internal void Flush()
    {
        Task pending; lock (gate) pending = writer;
        _ = pending.Wait(TimeSpan.FromMilliseconds(250));
    }
    private void Write(string line)
    {
        try
        {
            string full = Path.GetFullPath(directory!);
            PosInstallationPolicy.AssertNoLinks(full);
            Directory.CreateDirectory(full);
            string path = Path.Combine(full, fileName);
            PosInstallationPolicy.AssertNoLinks(path);
            using (var stream = new FileStream(path, initialized ? FileMode.Append : FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                initialized = true;
                if (stream.Length >= 65536) return;
                using var output = new StreamWriter(stream); output.WriteLine(line);
            }
            foreach (var old in new DirectoryInfo(full).EnumerateFiles("pos-lifecycle-*.log")
                .Where(f => f.Name != fileName && System.Text.RegularExpressions.Regex.IsMatch(f.Name, "^pos-lifecycle-[0-9]+-[a-f0-9]{32}\\.log$"))
                .OrderByDescending(f => f.LastWriteTimeUtc).Skip(7))
            {
                PosInstallationPolicy.AssertNoLinks(old.FullName); old.Delete();
            }
        }
        catch (Exception) { /* Diagnostics must never prevent shutdown or alter recovery. */ }
    }
}
