using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaConnect.Infrastructure.Messaging;
using NexaConnect.Observability;
using NexaConnect.Services.POS.Application.DayClose;
using NexaConnect.Services.POS.Infrastructure.Messaging;
using NexaConnect.Services.POS.Infrastructure.Persistence;
using Npgsql;

internal static class CashCorrectionReplayCommand
{
 public static async Task<int> RunAsync(string[] args)
 {
using var logging = NexaConnectObservabilityExtensions.CreateClientLoggerFactory("nexaconnect-cash-correction-replay",
    new ObservabilityOptions { OtlpEnabled = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")), OtlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") });
var logger = logging.CreateLogger("CashCorrectionReplay");
try
{
    if (args.Length == 0 || args.Contains("--help"))
    {
        Console.WriteLine("Cash correction replay: --organization UUID --branch UUID --restaurant UUID --from UTC --to UTC --limit 1..1000 [--execute --operator UUID --reason rebuild|retry --manifest SHA256]. Default is read-only preview. Credentials: NEXACONNECT_CASH_CORRECTION_REPLAY_DB, NEXACONNECT_CASH_CORRECTION_REPLAY_BROKER. Optional exchange: NEXACONNECT_CASH_CORRECTION_REPLAY_EXCHANGE.");
        return 0;
    }
    var values = new Dictionary<string, string>(StringComparer.Ordinal);
    bool execute = false;
    string[] allowed = ["--organization", "--branch", "--restaurant", "--from", "--to", "--limit", "--operator", "--reason", "--manifest"];
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--execute" && !execute) { execute = true; continue; }
        if (!allowed.Contains(args[i]) || i + 1 >= args.Length || !values.TryAdd(args[i], args[++i])) throw new ArgumentException();
    }
    DateTimeOffset Utc(string key)
    {
        string value = values[key];
        if (!value.EndsWith('Z') || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)) throw new ArgumentException();
        return date;
    }
    var request = new CashCorrectionReplayRequest(Guid.Parse(values["--organization"]), Guid.Parse(values["--branch"]), Guid.Parse(values["--restaurant"]),
        Utc("--from"), Utc("--to"), int.Parse(values["--limit"], CultureInfo.InvariantCulture));
    request.Validate();
    using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(10));
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    await using var database = NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("NEXACONNECT_CASH_CORRECTION_REPLAY_DB") ?? throw new ArgumentException());
    var options = Options.Create(new OutboxOptions
    {
        ConnectionString = Environment.GetEnvironmentVariable("NEXACONNECT_CASH_CORRECTION_REPLAY_BROKER") ?? "",
        Exchange = Environment.GetEnvironmentVariable("NEXACONNECT_CASH_CORRECTION_REPLAY_EXCHANGE") ?? "nexaconnect.events"
    });
    await using var broker = new RabbitMqOutboxTransport(options);
    var service = new CashCorrectionReplay(new PostgresCashCorrectionReplayStore(database), new CashCorrectionReplayTransport(broker));
    if (!execute)
    {
        var plan = await service.PreviewAsync(request, cancellation.Token);
        Console.WriteLine(JsonSerializer.Serialize(new { mode = "preview", plan.Manifest, plan.Count }));
    }
    else
    {
        Guid run = await service.ExecuteAsync(request, Guid.Parse(values["--operator"]), values["--reason"], values["--manifest"], cancellation.Token);
        logger.LogInformation("Cash-close replay confirmed; run {RunId}", run);
    }
    return 0;
}
catch (CashCorrectionReplayInterruptedException e)
{
    logger.LogError("Cash-close replay interrupted; inspect audit run {RunId}. Unconfirmed attempts are uncertain; retry original events after a fresh preview.", e.RunId);
    return 1;
}
catch (Exception)
{
    logger.LogError("Cash-close replay did not complete. Inspect durable replay audit; started attempts without confirmation are uncertain. Correct configuration or repeat preview and retry original events.");
    return 1;
}

 }
}
