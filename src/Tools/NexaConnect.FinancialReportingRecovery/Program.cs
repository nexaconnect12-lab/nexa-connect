extern alias ORDER;
extern alias PAYMENT;
extern alias REPORTING;
using System.Globalization;
using System.Text.Json;
using Npgsql;
using ORDER::NexaConnect.Services.Order.Infrastructure.Persistence;
using PAYMENT::NexaConnect.Services.Payment.Infrastructure;
using REPORTING::NexaConnect.Services.Reporting.Application;
using REPORTING::NexaConnect.Services.Reporting.Infrastructure.Persistence;

if(args.Length is not (4 or 5) || (args.Length==5 && args[4] is not ("--apply" or "--record")))
{ Console.Error.WriteLine("Usage: <organization UUID> <branch UUID> <from ISO UTC> <to ISO UTC> [--apply|--record]"); return 2; }
try
{
    Guid org=Guid.Parse(args[0]),branch=Guid.Parse(args[1]);
    var from=DateTimeOffset.Parse(args[2],CultureInfo.InvariantCulture).ToUniversalTime();
    var to=DateTimeOffset.Parse(args[3],CultureInfo.InvariantCulture).ToUniversalTime();
    var range=new ReportingRange(org,branch,from,to); FinancialCompleteness.ValidateRange(range);
    bool apply=args.Length==5 && args[4]=="--apply",record=args.Length==5;
    string? actor=Environment.GetEnvironmentVariable("NEXACONNECT_FINANCIAL_RECOVERY_ACTOR");
    if(record && (string.IsNullOrWhiteSpace(actor) || actor.Length>128 || actor.Any(char.IsControl)))
        throw new ArgumentException("Explicit bounded operator attribution is required.");
    await using var order=NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("NEXACONNECT_FINANCIAL_RECOVERY_ORDER_DB")??throw new ArgumentException("Order source connection is required."));
    await using var payment=NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("NEXACONNECT_FINANCIAL_RECOVERY_PAYMENT_DB")??throw new ArgumentException("Payment source connection is required."));
    await using var reporting=NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("NEXACONNECT_FINANCIAL_RECOVERY_REPORTING_DB")??throw new ArgumentException("Reporting connection is required."));
    using var cancellation=new CancellationTokenSource(TimeSpan.FromMinutes(5));
    var sales=await new PostgresFinancialSaleSource(order).RunAsync(org,branch,from,to,apply,actor,cancellation.Token);
    var refunds=await new PostgresRefundFinancialReplay(payment).RunAsync(org,branch,from,to,apply,actor,cancellation.Token);
    var service=new FinancialCompleteness(new PostgresFinancialCompletenessRepository(reporting));
    var observation=await service.CheckAsync(new(range,sales.ObservedAtUtc,refunds.ObservedAtUtc,sales.Candidates,refunds.Candidates,
        sales.EvidenceGaps,refunds.EvidenceGaps,sales.Unretained,refunds.Unretained,
        sales.Events.Select(SaleFinancialReporting.Translate).ToArray(),refunds.Events.Select(RefundFinancialReporting.Translate).ToArray()),cancellation.Token);
    if(record) await service.RecordAsync(observation,actor!,cancellation.Token);
    Console.WriteLine(JsonSerializer.Serialize(new { Observation=observation,SalesRequeued=sales.Requeued,RefundsRequeued=refunds.Requeued,Recorded=record }));
    return observation.Status=="observed_complete"?0:1;
}
catch(Exception exception)
{ Console.Error.WriteLine($"Financial reporting recovery failed ({exception.GetType().Name}); investigate source/service diagnostics."); return 2; }
