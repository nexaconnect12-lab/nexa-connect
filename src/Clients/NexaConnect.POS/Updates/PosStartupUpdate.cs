using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace NexaConnect.POS;

internal static class PosStartupUpdate
{
    internal const string MutexName = "NexaConnect.POS.StartupUpdate";
    internal const string SkipArgument = "--skip-startup-update";
    internal const string FailedArgument = "--startup-update-failed";

    internal static async Task<bool> TryHandoffAsync()
    {
        string install = AppContext.BaseDirectory;
        string source = Path.Combine(install, "update-source.json");
        string? executable = Environment.ProcessPath;
        if (executable is null || !File.Exists(source)
            || !string.Equals(executable, Path.Combine(install, "NexaConnect.POS.exe"), StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            PosInstallationPolicy.AssertManagedDirectory(Path.TrimEndingDirectorySeparator(install));
            PosInstallationPolicy.AssertNoLinks(source);
            PosInstallationPolicy.AssertNoLinks(Path.Combine(install, "appsettings.json"));
            if (File.ReadAllText(Path.Combine(install, "installer-owner.txt")).Trim() != "nexaconnect-terminal-installer-v1")
                return false;
            return await CheckAsync(executable, source);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException
            or InvalidOperationException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static async Task<bool> CheckAsync(string executable, string source)
    {
        using var script = typeof(PosStartupUpdate).Assembly.GetManifestResourceStream(
            "NexaConnect.POS.Updates.ApplyPosUpdate.ps1") ?? throw new InvalidOperationException("Update worker is missing.");
        using var reader = new StreamReader(script);
        string stage = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NexaConnect", "Updates", Guid.NewGuid().ToString("N"));
        PosInstallationPolicy.AssertNoLinks(stage);
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
        };
        foreach (string argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand",
            Convert.ToBase64String(Encoding.Unicode.GetBytes(await reader.ReadToEndAsync())) })
            start.ArgumentList.Add(argument);
        start.Environment["NEXA_UPDATE_PARENT"] = Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["NEXA_UPDATE_EXE"] = executable;
        start.Environment["NEXA_UPDATE_SOURCE"] = source;
        start.Environment["NEXA_UPDATE_STAGE"] = stage;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var cancel = new Button { Content = "Open installed POS", MinHeight = 32, Margin = new Thickness(0, 12, 0, 0) };
        var content = new StackPanel { Margin = new Thickness(20) };
        content.Children.Add(new TextBlock { Text = "Checking for a POS update…" });
        content.Children.Add(cancel);
        var progress = new Window
        {
            Title = "NexaConnect POS", Content = content, Width = 340, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        var app = Application.Current;
        var shutdownMode = app.ShutdownMode;
        using var worker = Process.Start(start) ?? throw new InvalidOperationException("Update worker could not start.");
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        progress.Closed += (_, _) => timeout.Cancel();
        cancel.Click += (_, _) => timeout.Cancel();
        bool handoff = false;
        try
        {
            progress.Show();
            handoff = await worker.StandardOutput.ReadLineAsync().WaitAsync(timeout.Token) == "NEXA-UPDATE-READY";
            return handoff;
        }
        catch (OperationCanceledException) { return false; }
        finally
        {
            if (!handoff) StopCheck(worker, stage);
            if (progress.IsVisible) progress.Close();
            app.ShutdownMode = shutdownMode;
        }
    }

    private static void StopCheck(Process worker, string stage)
    {
        try
        {
            if (!worker.HasExited) worker.Kill();
            if (!worker.WaitForExit(1000)) return;
            File.Delete(Path.Combine(stage, "setup.exe"));
            if (Directory.Exists(stage)) Directory.Delete(stage);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
            or InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
}
