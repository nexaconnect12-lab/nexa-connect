extern alias ORDER;
extern alias REPORTING;
using System.Globalization;
using System.Text.Json;
using Npgsql;
using ORDER::NexaConnect.Services.Order.Infrastructure.Persistence;
using REPORTING::NexaConnect.Services.Reporting.Application;
using REPORTING::NexaConnect.Services.Reporting.Infrastructure.Persistence;

// Credentials come only from the process environment and are never printed.
if (args.Length is not (4 or 5) || (args.Length == 5 && args[4] != "--apply"))
{
    Console.Error.WriteLine("Usage: <organization UUID> <branch UUID> <from ISO UTC> <to ISO UTC> [--apply]");
    return 2;
}
try
{
    Guid organization = Guid.Parse(args[0]), branch = Guid.Parse(args[1]);
    DateTimeOffset from = DateTimeOffset.Parse(args[2], CultureInfo.InvariantCulture).ToUniversalTime();
    DateTimeOffset to = DateTimeOffset.Parse(args[3], CultureInfo.InvariantCulture).ToUniversalTime();
    await using var order = NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("NEXACONNECT_SALE_RECOVERY_ORDER_DB")
        ?? throw new ArgumentException("Order database environment variable is required."));
    await using var reporting = NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("NEXACONNECT_SALE_RECOVERY_REPORTING_DB")
        ?? throw new ArgumentException("Reporting database environment variable is required."));
    using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    SaleReplayResult replay = await new PostgresSaleReplay(order).RunAsync(organization, branch, from, to, args.Length == 5,
        cancellation.Token, Environment.GetEnvironmentVariable("NEXACONNECT_SALE_RECOVERY_ACTOR"));
    SaleReconciliationResult reconciliation = await new PostgresSaleReconciliation(reporting)
        .CheckAsync(replay.Publications.Select(SaleFinancialReporting.Translate).ToArray(), cancellation.Token);
    int unpublished = replay.Candidates - replay.MissingReceipts - replay.Publications.Count;
    Console.WriteLine(JsonSerializer.Serialize(new { replay.Candidates, replay.MissingReceipts, Unpublished = unpublished,
        replay.Published, replay.Requeued, Reconciliation = reconciliation }));
    return replay.MissingReceipts + unpublished + reconciliation.Missing + reconciliation.Conflicting == 0 ? 0 : 1;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Sale reporting recovery failed ({exception.GetType().Name}); inspect service diagnostics.");
    return 2;
}
