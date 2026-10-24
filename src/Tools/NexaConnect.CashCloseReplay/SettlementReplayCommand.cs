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

internal static class SettlementReplayCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        using var logging=NexaConnectObservabilityExtensions.CreateClientLoggerFactory("nexaconnect-settlement-replay",
            new ObservabilityOptions{OtlpEnabled=!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")),OtlpEndpoint=Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")});
        var logger=logging.CreateLogger("SettlementReplay");
        try
        {
            if(args.Length==0||(args.Length==1&&args[0]=="--help"))
            {
                Console.WriteLine("Settlement replay: --organization UUID --restaurant UUID --branch UUID --from-date yyyy-MM-dd --to-date yyyy-MM-dd --limit 1..31 [--execute --operator UUID --reason retry|rebuild --manifest SHA256]. Default is read-only preview. Credentials: NEXACONNECT_SETTLEMENT_REPLAY_DB, NEXACONNECT_SETTLEMENT_REPLAY_BROKER. Optional exchange: NEXACONNECT_SETTLEMENT_REPLAY_EXCHANGE.");return 0;
            }
            var values=new Dictionary<string,string>(StringComparer.Ordinal);bool execute=false;
            string[] allowed=["--organization","--restaurant","--branch","--from-date","--to-date","--limit","--operator","--reason","--manifest"];
            for(int n=0;n<args.Length;n++)
            {
                if(args[n]=="--execute"&&!execute){execute=true;continue;}
                if(!allowed.Contains(args[n])||n+1>=args.Length||!values.TryAdd(args[n],args[++n]))throw new ArgumentException();
            }
            DateOnly Date(string key)=>DateOnly.TryParseExact(values[key],"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d)?d:throw new ArgumentException();
            var request=new SettlementReplayRequest(Guid.Parse(values["--organization"]),Guid.Parse(values["--restaurant"]),Guid.Parse(values["--branch"]),
                Date("--from-date"),Date("--to-date"),int.Parse(values["--limit"],CultureInfo.InvariantCulture));request.Validate();
            Guid operatorId=Guid.Empty;string reason="",manifest="";
            if(execute)
            {operatorId=Guid.Parse(values["--operator"]);reason=values["--reason"];manifest=values["--manifest"];SettlementReplay.ValidateExecution(request,operatorId,reason,manifest);}
            else if(values.Keys.Any(k=>k is "--operator" or "--reason" or "--manifest"))throw new ArgumentException();
            using var cancellation=new CancellationTokenSource(TimeSpan.FromMinutes(10));
            Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cancellation.Cancel();};
            await using var database=NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("NEXACONNECT_SETTLEMENT_REPLAY_DB")??throw new ArgumentException());
            var brokerAddress=Environment.GetEnvironmentVariable("NEXACONNECT_SETTLEMENT_REPLAY_BROKER")??"";
            if(execute&&string.IsNullOrWhiteSpace(brokerAddress))throw new ArgumentException();
            await using var broker=new RabbitMqOutboxTransport(Options.Create(new OutboxOptions{ConnectionString=brokerAddress,
                Exchange=Environment.GetEnvironmentVariable("NEXACONNECT_SETTLEMENT_REPLAY_EXCHANGE")??"nexaconnect.events"}));
            var service=new SettlementReplay(new PostgresSettlementReplayStore(database),new SettlementReplayTransport(broker));
            if(!execute)
            {var preview=await service.PreviewAsync(request,cancellation.Token);Console.WriteLine(JsonSerializer.Serialize(new{mode="preview",preview.Manifest,preview.Count}));}
            else
            {
                var run=await service.ExecuteAsync(request,operatorId,reason,manifest,cancellation.Token);
                logger.LogInformation("Settlement replay broker confirmations complete; run {RunId}",run);
                Console.WriteLine(JsonSerializer.Serialize(new{mode="executed",runId=run}));
            }
            return 0;
        }
        catch(SettlementReplaySelectionChangedException)
        {logger.LogWarning("Settlement replay selection changed; repeat preview before executing");return 1;}
        catch(SettlementReplayInterruptedException e)
        {logger.LogError("Settlement replay interrupted; inspect audit run {RunId} if recorded. Missing confirmations are uncertain; preview and retry original events",e.RunId);return 1;}
        catch(Exception e)
        {logger.LogError("Settlement replay unavailable or invalid; category {Category}. Inspect configuration and repeat preview",e.GetType().Name);return 1;}
    }
}
