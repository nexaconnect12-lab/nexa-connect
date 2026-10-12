extern alias POS;
extern alias REPORTING;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Infrastructure.Messaging;
using Npgsql;
using RabbitMQ.Client;
using Replay=POS::NexaConnect.Services.POS.Application.DayClose;
using ReplayDb=POS::NexaConnect.Services.POS.Infrastructure.Persistence;
using ReplayMessaging=POS::NexaConnect.Services.POS.Infrastructure.Messaging;
using AppR=REPORTING::NexaConnect.Services.Reporting.Application;
using DbR=REPORTING::NexaConnect.Services.Reporting.Infrastructure.Persistence;
using ConsumerR=REPORTING::NexaConnect.Services.Reporting.Infrastructure.Messaging.SettlementFinancialConsumer;
namespace NexaConnect.IntegrationTests;

public sealed partial class DaySealPostgresTests
{
    private Replay.SettlementReplayRequest ReplaySelection=>new(organization,restaurant,branch,Date,Date.AddDays(1),1);
    private Replay.SettlementReplay ReplayApplication(SettlementReplayProbe transport)=>new(new ReplayDb.PostgresSettlementReplayStore(pos),transport);
    private async Task<string> ReplaySourceFingerprint()
    {
        await using var q=pos.CreateCommand("""
            SELECT jsonb_build_object(
              'state',(SELECT jsonb_agg(to_jsonb(s) ORDER BY id) FROM branch_day_settlements s),
              'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY settlement_id) FROM branch_day_settlement_receipts r),
              'decisions',(SELECT jsonb_agg(to_jsonb(d) ORDER BY settlement_id) FROM branch_day_settlement_decisions d),
              'revision',(SELECT jsonb_agg(to_jsonb(v) ORDER BY branch_id) FROM source_financial_revisions v),
              'outbox',(SELECT jsonb_agg(to_jsonb(o) ORDER BY id) FROM outbox_messages o))::text
            """);return(string)(await q.ExecuteScalarAsync())!;
    }
    [ReportingDatabaseFact]
    public async Task Settlement_replay_uncertain_publication_restart_retains_original_payload_and_append_only_audit()
    {
        var manifest=await CommittedSettlementManifest();var id=manifest.Events.Single().V2!.EventId;
        await Sql(pos,"UPDATE outbox_messages SET published_at_utc=clock_timestamp() WHERE id=$1",id);
        var before=await ReplaySourceFingerprint();var transport=new SettlementReplayProbe{FailAfterPublish=true};var app=ReplayApplication(transport);
        var preview=await app.PreviewAsync(ReplaySelection,default);Assert.Equal(1,preview.Count);Assert.Empty(transport.Events);Assert.Equal(0,await Count(pos,"settlement_replay_runs"));
        var interrupted=await Assert.ThrowsAsync<Replay.SettlementReplayInterruptedException>(()=>app.ExecuteAsync(ReplaySelection,Guid.NewGuid(),"retry",preview.Manifest,default));
        Assert.NotEqual(Guid.Empty,interrupted.RunId);Assert.Equal(1,await Count(pos,"settlement_replay_attempts"));
        transport.FailAfterPublish=false;var restarted=ReplayApplication(transport);Assert.Equal(preview,await restarted.PreviewAsync(ReplaySelection,default));
        await restarted.ExecuteAsync(ReplaySelection,Guid.NewGuid(),"retry",preview.Manifest,default);
        Assert.Equal(transport.Events[0],transport.Events[1]);Assert.Equal(2,await Count(pos,"settlement_replay_runs"));Assert.Equal(3,await Count(pos,"settlement_replay_attempts"));
        Assert.Equal(before,await ReplaySourceFingerprint());
        foreach(var sql in new[]{"DELETE FROM settlement_replay_runs","TRUNCATE settlement_replay_attempts","UPDATE settlement_replay_runs SET reason='rebuild'"})
            await Assert.ThrowsAsync<PostgresException>(()=>Sql(pos,sql));
        await Assert.ThrowsAsync<PostgresException>(()=>Sql(pos,File.ReadAllText(Path.Combine(Root(),"src/Tools/NexaConnect.DataMigration/Scripts/POS/0023_settlement_replay/down.sql"))));
    }
    [ReportingDatabaseFact]
    public async Task Settlement_replay_new_commit_changes_reviewed_selection_and_limits_never_truncate()
    {
        var day=Date;await CommittedSettlementManifest();var request=ReplaySelection with{FromDate=day.AddDays(-1),Limit=2};var transport=new SettlementReplayProbe();var app=ReplayApplication(transport);
        var preview=await app.PreviewAsync(request,default);Assert.Equal(1,preview.Count);
        var prior=correctionTestDate;correctionTestDate=day.AddDays(-1);
        try{await CommittedSettlementManifest();}finally{correctionTestDate=prior;}
        await Assert.ThrowsAsync<Replay.SettlementReplaySelectionChangedException>(()=>app.ExecuteAsync(request,Guid.NewGuid(),"retry",preview.Manifest,default));
        Assert.Empty(transport.Events);Assert.Equal(0,await Count(pos,"settlement_replay_runs"));
        await Assert.ThrowsAsync<ArgumentException>(()=>app.PreviewAsync(request with{Limit=1},default));
        Assert.Equal(2,(await app.PreviewAsync(request,default)).Count);
    }
    [ReportingDatabaseFact]
    public async Task Settlement_replay_exact_tenant_scope_and_corrupt_unknown_or_missing_original_fail_before_audit()
    {
        var manifest=await CommittedSettlementManifest();var id=manifest.Events.Single().V2!.EventId;var transport=new SettlementReplayProbe();var app=ReplayApplication(transport);
        Assert.Equal(0,(await app.PreviewAsync(ReplaySelection with{OrganizationId=Guid.NewGuid()},default)).Count);
        Assert.Equal(0,(await app.PreviewAsync(ReplaySelection with{RestaurantId=Guid.NewGuid()},default)).Count);
        Assert.Equal(0,(await app.PreviewAsync(ReplaySelection with{BranchId=Guid.NewGuid()},default)).Count);
        var preview=await app.PreviewAsync(ReplaySelection,default);
        await Sql(pos,"UPDATE outbox_messages SET payload=payload||'{\"unknown\":true}'::jsonb WHERE id=$1",id);
        await Assert.ThrowsAsync<JsonException>(()=>app.PreviewAsync(ReplaySelection,default));
        await Assert.ThrowsAsync<Replay.SettlementReplayInterruptedException>(()=>app.ExecuteAsync(ReplaySelection,Guid.NewGuid(),"retry",preview.Manifest,default));
        Assert.Equal(0,await Count(pos,"settlement_replay_runs"));Assert.Empty(transport.Events);
        await Sql(pos,"DELETE FROM outbox_messages WHERE id=$1",id);await Assert.ThrowsAsync<InvalidOperationException>(()=>app.PreviewAsync(ReplaySelection,default));
    }
    [ReportingDatabaseFact]
    public async Task Settlement_replay_empty_migration_roundtrip_selection_membership_and_confirmation_order_are_guarded()
    {
        var dir=Path.Combine(Root(),"src/Tools/NexaConnect.DataMigration/Scripts/POS/0023_settlement_replay");await Sql(pos,File.ReadAllText(Path.Combine(dir,"down.sql")));await Sql(pos,File.ReadAllText(Path.Combine(dir,"up.sql")));
        await CommittedSettlementManifest();var store=new ReplayDb.PostgresSettlementReplayStore(pos);var events=await store.SelectAsync(ReplaySelection,default);var plan=Replay.SettlementReplay.Plan(ReplaySelection,events);var run=Guid.NewGuid();
        await store.BeginAsync(run,ReplaySelection,Guid.NewGuid(),"retry",plan.Manifest,default);
        await Assert.ThrowsAsync<PostgresException>(()=>store.AppendAsync(run,Guid.NewGuid(),"started",default));
        await Assert.ThrowsAsync<PostgresException>(()=>store.AppendAsync(run,events.Single().Id,"confirmed",default));
        await store.AppendAsync(run,events.Single().Id,"started",default);await store.AppendAsync(run,events.Single().Id,"confirmed",default);
        await Assert.ThrowsAsync<PostgresException>(()=>store.AppendAsync(run,events.Single().Id,"confirmed",default));Assert.Equal(2,await Count(pos,"settlement_replay_attempts"));
        await Sql(pos,"INSERT INTO settlement_replay_runs(id,organization_id,restaurant_id,branch_id,from_date,to_date,operator_id,reason,manifest,event_count,selection_limit,selected_events,database_actor) VALUES($1,$2,$3,$4,$5,$6,$7,'retry',$8,0,1,'[]'::jsonb,'forged')",
            Guid.NewGuid(),organization,restaurant,branch,Date,Date.AddDays(1),Guid.NewGuid(),new string('A',64));
        await using var actor=pos.CreateCommand("SELECT count(*) FROM settlement_replay_runs WHERE database_actor='forged'");Assert.Equal(0L,await actor.ExecuteScalarAsync());
    }
    [ReportingRabbitFact]public Task Settlement_replay_cli_restricted_V1_history_recovers_after_queue_creation()=>SettlementCliCase(1);
    [ReportingRabbitFact]public Task Settlement_replay_cli_restricted_V2_uncertain_confirmation_restart_and_deduplication()=>SettlementCliCase(2);
    private async Task SettlementCliCase(int version)
    {
        var manifest=await CommittedSettlementManifest();var original=manifest.Events.Single().V2!;
        if(version==1)await Sql(pos,"UPDATE outbox_messages SET event_type='pos.branch-day-settled.v1',contract_version=1,payload=$2::jsonb WHERE id=$1",original.EventId,JsonSerializer.Serialize(original.Settlement,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        await Sql(pos,"UPDATE outbox_messages SET published_at_utc=clock_timestamp() WHERE id=$1",original.EventId);manifest=await SettlementManifest();var before=await ReplaySourceFingerprint();
        var schema=owned.Single(x=>ReferenceEquals(x.Db,pos)).Schema;var role="settlement_replay_"+Guid.NewGuid().ToString("N");var password=Guid.NewGuid().ToString("N");var quote=new NpgsqlCommandBuilder();
        await Sql(pos,"CREATE ROLE "+quote.QuoteIdentifier(role)+" LOGIN PASSWORD '"+password+"'");
        await Sql(pos,"GRANT USAGE ON SCHEMA "+quote.QuoteIdentifier(schema)+" TO "+quote.QuoteIdentifier(role));
        await Sql(pos,"GRANT SELECT ON branch_day_settlements,branch_day_settlement_decisions,branch_day_settlement_receipts,outbox_messages TO "+quote.QuoteIdentifier(role));
        var connection=new NpgsqlConnectionStringBuilder(pos.ConnectionString){Username=role,Password=password}.ConnectionString;
        var preview=await RunSettlementCli(connection,"not-a-broker",[]);Assert.Equal(0,preview.Exit);Assert.DoesNotContain(password,preview.Output);Assert.DoesNotContain("grossSales",preview.Output);
        var planLine=preview.Output.Split('\n').Single(line=>line.TrimStart().StartsWith("{\"mode\":\"preview\"",StringComparison.Ordinal));using var plan=JsonDocument.Parse(planLine);var hash=plan.RootElement.GetProperty("Manifest").GetString()!;Assert.Equal(1,plan.RootElement.GetProperty("Count").GetInt32());
        var uri=Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_INTEGRATION_URI")!;var queue="nexa.settlement.replay.it."+Guid.NewGuid().ToString("N");var exchange=queue+".events";
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["SettlementConsumer:ConnectionString"]=uri,["SettlementConsumer:Queue"]=queue,["SettlementConsumer:Exchange"]=exchange}).Build();
        var repo=new SettlementCommitBeforeAck(new DbR.PostgresSettlementFactRepository(reporting));var services=new ServiceCollection();services.AddSingleton<AppR.ISettlementFactRepository>(repo);services.AddSingleton<AppR.ISettlementSource>(new SettlementSourcePort(this));services.AddSingleton<AppR.IReportingCustomerAuthorizer>(this);services.AddScoped<AppR.SettlementReporting>();
        using var provider=services.BuildServiceProvider();await using var rabbit=await new ConnectionFactory{Uri=new(uri)}.CreateConnectionAsync();
        using var consumer=new ConsumerR(provider.GetRequiredService<IServiceScopeFactory>(),config,NullLogger<ConsumerR>.Instance);
        async Task Wait(Func<Task<bool>> predicate){for(int i=0;i<300;i++){if(await predicate())return;await Task.Delay(50);}throw new TimeoutException("Settlement replay did not reconcile.");}
        try
        {
            await consumer.StartAsync(default);using var ready=new CancellationTokenSource(TimeSpan.FromSeconds(15));await consumer.WaitUntilReadyAsync(ready.Token);await consumer.StopAsync(default);
            string[] execute=["--execute","--operator",Guid.NewGuid().ToString(),"--reason","retry","--manifest",hash];
            var denied=await RunSettlementCli(connection,uri,execute,exchange);Assert.Equal(1,denied.Exit);Assert.Equal(0,await Count(pos,"settlement_replay_runs"));
            await Sql(pos,"GRANT SELECT,INSERT ON settlement_replay_runs,settlement_replay_attempts TO "+quote.QuoteIdentifier(role));
            if(version==2)
            {
                await using var restrictedReplay=NpgsqlDataSource.Create(connection);
                await using var publisher=new RabbitMqOutboxTransport(rabbit,Options.Create(new OutboxOptions{Exchange=exchange}));
                var lost=new Replay.SettlementReplay(new ReplayDb.PostgresSettlementReplayStore(restrictedReplay),new ReplayMessaging.SettlementReplayTransport(new SettlementReplayLostConfirmation(publisher)));
                var uncertain=await Assert.ThrowsAsync<Replay.SettlementReplayInterruptedException>(()=>lost.ExecuteAsync(ReplaySelection,Guid.NewGuid(),"retry",hash,default));
                Assert.NotEqual(Guid.Empty,uncertain.RunId);Assert.Equal(1,await Count(pos,"settlement_replay_attempts"));
            }
            var applied=await RunSettlementCli(connection,uri,execute,exchange);Assert.Equal(0,applied.Exit);Assert.Equal(1,(await repo.CompareAsync(manifest,default)).Missing);
            // Inspect real wire bytes while the consumer is stopped, then put the exact delivery back.
            await using(var inspect=await rabbit.CreateChannelAsync())
            {
                var delivery=await inspect.BasicGetAsync(queue,autoAck:false);Assert.NotNull(delivery);
                await using var originalPayload=pos.CreateCommand("SELECT payload::text FROM outbox_messages WHERE id=$1");originalPayload.Parameters.AddWithValue(original.EventId);
                Assert.Equal((string)(await originalPayload.ExecuteScalarAsync())!,System.Text.Encoding.UTF8.GetString(delivery.Body.Span));
                Assert.Equal("pos.branch-day-settled.v"+version,delivery.RoutingKey);await inspect.BasicNackAsync(delivery.DeliveryTag,false,true);
            }
            using(var restarted=new ConsumerR(provider.GetRequiredService<IServiceScopeFactory>(),config,NullLogger<ConsumerR>.Instance))
            {await restarted.StartAsync(default);using var again=new CancellationTokenSource(TimeSpan.FromSeconds(15));await restarted.WaitUntilReadyAsync(again.Token);await Wait(()=>Task.FromResult(Volatile.Read(ref repo.Calls)>=2));await restarted.StopAsync(default);}
            Assert.Equal("matched",(await repo.CompareAsync(manifest,default)).Status);
            Assert.Equal(version==1?"historical_unavailable":"verified",(await repo.CompareAsync(manifest,default)).Items.Single().CorrectionEvidence);
            var retried=await RunSettlementCli(connection,uri,execute,exchange);Assert.Equal(0,retried.Exit);
            using(var restarted=new ConsumerR(provider.GetRequiredService<IServiceScopeFactory>(),config,NullLogger<ConsumerR>.Instance))
            {int calls=repo.Calls;await restarted.StartAsync(default);using var again=new CancellationTokenSource(TimeSpan.FromSeconds(15));await restarted.WaitUntilReadyAsync(again.Token);await Wait(()=>Task.FromResult(Volatile.Read(ref repo.Calls)>calls));await restarted.StopAsync(default);}
            Assert.Equal(1,await Count(reporting,"settlement_facts"));Assert.Equal(version==2?3:2,await Count(pos,"settlement_replay_runs"));Assert.Equal(version==2?5:4,await Count(pos,"settlement_replay_attempts"));Assert.Equal(before,await ReplaySourceFingerprint());
            await using var actor=pos.CreateCommand("SELECT bool_and(database_actor=$1) FROM settlement_replay_runs");actor.Parameters.AddWithValue(role);Assert.Equal(true,await actor.ExecuteScalarAsync());
            await using var restricted=NpgsqlDataSource.Create(connection);
            await Assert.ThrowsAsync<PostgresException>(async()=>{await using var q=restricted.CreateCommand("UPDATE outbox_messages SET published_at_utc=NULL");await q.ExecuteNonQueryAsync();});
            await Assert.ThrowsAsync<PostgresException>(async()=>{await using var q=restricted.CreateCommand("DELETE FROM settlement_replay_runs");await q.ExecuteNonQueryAsync();});
            Assert.DoesNotContain(uri,applied.Output);Assert.DoesNotContain(password,applied.Output);
        }
        finally{await consumer.StopAsync(default);await using var cleanup=await rabbit.CreateChannelAsync();await cleanup.QueueDeleteAsync(queue);await cleanup.QueueDeleteAsync(queue+".dead");await cleanup.ExchangeDeleteAsync(exchange);}
    }
    private async Task<(int Exit,string Output)> RunSettlementCli(string database,string broker,string[] extra,string? exchange=null)
    {
        var dll=Path.Combine(Root(),"src/Tools/NexaConnect.CashCloseReplay/bin/Debug/net10.0/NexaConnect.CashCloseReplay.dll");
        var info=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{dll,"--settlements","--organization",organization.ToString(),"--restaurant",restaurant.ToString(),"--branch",branch.ToString(),"--from-date",Date.ToString("yyyy-MM-dd"),"--to-date",Date.AddDays(1).ToString("yyyy-MM-dd"),"--limit","1"}.Concat(extra))info.ArgumentList.Add(arg);
        info.Environment["NEXACONNECT_SETTLEMENT_REPLAY_DB"]=database;info.Environment["NEXACONNECT_SETTLEMENT_REPLAY_BROKER"]=broker;
        if(exchange is not null)info.Environment["NEXACONNECT_SETTLEMENT_REPLAY_EXCHANGE"]=exchange;
        using var process=Process.Start(info)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try{await process.WaitForExitAsync(timeout.Token);}catch{process.Kill(true);throw;}
        return(process.ExitCode,await output+await error);
    }
    private sealed class SettlementReplayProbe:Replay.ISettlementReplayTransport
    {public bool FailAfterPublish;public List<Replay.SettlementReplayEvent> Events=[];public Task PublishAsync(Replay.SettlementReplayEvent e,CancellationToken ct){Events.Add(e);if(FailAfterPublish)throw new HttpRequestException();return Task.CompletedTask;}}
    private sealed class SettlementReplayLostConfirmation(IOutboxTransport inner):IOutboxTransport
    {public async Task PublishAsync(OutboxMessage message,CancellationToken ct){await inner.PublishAsync(message,ct);throw new HttpRequestException("Test confirmation loss after actual broker routing");}}
}
