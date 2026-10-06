extern alias POS;
extern alias ORDER;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
namespace NexaConnect.FinancialPortalAcceptance.Infrastructure;

// Acceptance executable only; production service hosts expose no clock switch.
internal static class CashierCommandHost
{
    public static async Task RunAsync(string action)
    {
        string Required(string key) => Environment.GetEnvironmentVariable("Acceptance__" + key) ?? throw new ArgumentException();
        string run = Required("RunId"), service = action == "host-pos" ? "POS" : "Order";
        if (Required("ConfirmDisposable") != "1" || !System.Text.RegularExpressions.Regex.IsMatch(run, "^[a-f0-9]{32}$")) throw new ArgumentException();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NexaConnect.sln"))) directory = directory.Parent;
        string root = directory?.FullName ?? throw new ArgumentException();
        string path = Path.GetFullPath(Required("ClockPath"));
        if (path != Path.GetFullPath(Path.Combine(root, ".runstate", "cashier-day-close", run, "clock.json"))) throw new ArgumentException();
        var db = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ConnectionStrings__" + service));
        if (db.Host != "127.0.0.1" || db.Database != $"nexa_review_it_{run}_{service.ToLowerInvariant()}" || !string.IsNullOrEmpty(db.SearchPath)) throw new ArgumentException();
        var issuer = new Uri(Environment.GetEnvironmentVariable("Authentication__Authority") ?? throw new ArgumentException());
        if (issuer.Scheme != "http" || issuer.Host != "127.0.0.1" || issuer.AbsolutePath != $"/realms/nexa-review-it-{run}") throw new ArgumentException();
        if (!int.TryParse(Required("Port"), out int port) || port is < 1024 or > 65535) throw new ArgumentException();
        var clock = new CommandClock(path, run); _ = clock.GetUtcNow();
        if (service == "POS") await Serve<POS::PosProgram>(root, service, port, clock);
        else await Serve<ORDER::OrderProgram>(root, service, port, clock);
    }
    private static async Task Serve<T>(string root, string service, int port, TimeProvider clock) where T : class
    {
        await using var factory = new CommandFactory<T>(Path.Combine(root, "src", "Services", "NexaConnect.Services." + service), clock);
        factory.UseKestrel(options => options.Listen(System.Net.IPAddress.Loopback, port));
        factory.StartServer(); await Task.Delay(Timeout.InfiniteTimeSpan);
    }
    private sealed class CommandFactory<T>(string contentRoot, TimeProvider clock) : WebApplicationFactory<T> where T : class
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(contentRoot).UseEnvironment("Testing");
            builder.ConfigureServices(services => { services.RemoveAll<TimeProvider>(); services.AddSingleton(clock); });
        }
    }
    private sealed class CommandClock(string path, string run) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path)); var value = document.RootElement;
            if (value.GetProperty("runId").GetString() != run) throw new InvalidOperationException();
            if (value.GetProperty("mode").GetString() == "live") return System.GetUtcNow();
            var at = value.GetProperty("atUtc").GetDateTimeOffset();
            if (value.GetProperty("mode").GetString() != "historical" || at >= System.GetUtcNow() || at < System.GetUtcNow().AddDays(-3)) throw new InvalidOperationException();
            return at;
        }
    }
}
