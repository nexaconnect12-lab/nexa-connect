using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexaConnect.Infrastructure.Messaging;
using NexaConnect.Services.Reporting.Application;
using NexaConnect.Services.Reporting.Domain;
using NexaConnect.Services.Reporting.Infrastructure.Messaging;
using NexaConnect.Services.Reporting.Infrastructure.Persistence;
using Npgsql;

// This process host is an acceptance fixture, never registered in a deployed service.
if (Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT") != "Testing" ||
    Environment.GetEnvironmentVariable("NEXACONNECT_CASH_CLOSE_ACCEPTANCE") != "1" || args.Length != 1 ||
    args[0] is not ("consumer" or "dispatcher")) return 2;
var builder = Host.CreateApplicationBuilder();
builder.Logging.ClearProviders(); // Evidence is bounded marker files; no connection or financial payload logging.
string database = Environment.GetEnvironmentVariable("NEXACONNECT_CASH_HOST_DB") ?? throw new InvalidOperationException();
string broker = Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_INTEGRATION_URI") ?? throw new InvalidOperationException();
string exchange = Environment.GetEnvironmentVariable("NEXACONNECT_CASH_EXCHANGE") ?? throw new InvalidOperationException();
string queue = Environment.GetEnvironmentVariable("NEXACONNECT_CASH_QUEUE") ?? throw new InvalidOperationException();
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["CashCloseConsumer:ConnectionString"] = broker, ["CashCloseConsumer:Exchange"] = exchange,
    ["CashCloseConsumer:Queue"] = queue, ["ConnectionStrings:POS"] = database,
    ["Outbox:ConnectionString"] = broker, ["Outbox:Exchange"] = exchange, ["Outbox:PollInterval"] = "00:00:00.100"
});
builder.Services.AddSingleton(NpgsqlDataSource.Create(database));
if (args[0] == "consumer")
{
    builder.Services.AddScoped<PostgresCashCloseRepository>();
    builder.Services.AddScoped<ICashCloseRepository, CommitBarrierRepository>();
    builder.Services.AddSingleton<ICashCloseAccess, DenyAccess>();
    builder.Services.AddScoped<CashCloseReporting>();
    builder.Services.AddSingleton<CashCloseConsumer>();
    builder.Services.AddHostedService(p => p.GetRequiredService<CashCloseConsumer>());
}
else builder.Services.AddPostgresOutbox(builder.Configuration, "POS");
using var host = builder.Build();
await host.StartAsync();
if (args[0] == "consumer")
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    await host.Services.GetRequiredService<CashCloseConsumer>().WaitUntilReadyAsync(timeout.Token);
}
await File.WriteAllTextAsync(Environment.GetEnvironmentVariable("NEXACONNECT_CASH_READY_FILE")!, "ready");
await host.WaitForShutdownAsync();
return 0;

sealed class DenyAccess : ICashCloseAccess
{
    public Task<bool> CanReadAsync(Guid o, Guid b, Guid s, string a, CancellationToken ct) => Task.FromResult(false);
}
sealed class CommitBarrierRepository(PostgresCashCloseRepository inner) : ICashCloseRepository
{
    public async Task<bool> ProjectAsync(Guid id, CashCloseSnapshot snapshot, CancellationToken ct)
    {
        bool changed = await inner.ProjectAsync(id, snapshot, ct);
        string? marker = Environment.GetEnvironmentVariable("NEXACONNECT_CASH_COMMIT_BARRIER");
        if (marker is not null)
        {
            await File.WriteAllTextAsync(marker, "committed", ct);
            await Task.Delay(Timeout.Infinite, ct); // Parent kills this exact process after observing commit.
        }
        return changed;
    }
    public Task<IReadOnlyList<CashCloseRow>> ReadAsync(CashCloseQuery query, CancellationToken ct) => inner.ReadAsync(query, ct);
}
