extern alias POS;
extern alias REPORTING;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Infrastructure.Messaging;
using Npgsql;
using RabbitMQ.Client;
using Source = POS::NexaConnect.Services.POS.Infrastructure.Persistence;
using Replay = POS::NexaConnect.Services.POS.Application.CashReviews;
using Reports = REPORTING::NexaConnect.Services.Reporting.Application;

namespace NexaConnect.IntegrationTests;

public sealed partial class CashCloseProjectionPostgresTests
{
    [CashClosePipelineFact]
    public async Task Real_processes_recover_commit_before_ack_broker_outage_and_rebuild_using_replay_cli()
    {
        string exchange="cashclose."+Guid.NewGuid().ToString("N"), queue=exchange+".reporting";
        string directory=Path.Combine(Environment.GetEnvironmentVariable("NEXACONNECT_CASH_RUN_DIR")!,Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string broker=Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_INTEGRATION_URI")!;
        var processes=new List<Process>();
        string Db(string setting,string schema)=>new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(setting)){SearchPath=schema}.ConnectionString;
        string posDb=Db("NEXACONNECT_POS_INTEGRATION_DB",sourceSchema), reportingDb=Db("NEXACONNECT_REPORTING_INTEGRATION_DB",sinkSchema);
        Process Start(string dll,IEnumerable<string> args,Dictionary<string,string> environment)
        {
            var info=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            info.Environment.Remove("NEXACONNECT_CASH_COMMIT_BARRIER");
            info.ArgumentList.Add(dll);foreach(string arg in args)info.ArgumentList.Add(arg);
            foreach(var entry in environment)info.Environment[entry.Key]=entry.Value;
            var process=Process.Start(info)!;processes.Add(process);
            process.OutputDataReceived+=(_,_)=>{};process.ErrorDataReceived+=(_,_)=>{};
            process.BeginOutputReadLine();process.BeginErrorReadLine();return process;
        }
        async Task<Process> Host(string mode,bool barrier=false)
        {
            string ready=Path.Combine(directory,Guid.NewGuid().ToString("N")+".ready");
            var settings=new Dictionary<string,string>{["NEXACONNECT_CASH_HOST_DB"]=mode=="consumer"?reportingDb:posDb,
                ["NEXACONNECT_CASH_EXCHANGE"]=exchange,["NEXACONNECT_CASH_QUEUE"]=queue,["NEXACONNECT_CASH_READY_FILE"]=ready};
            if(barrier)settings["NEXACONNECT_CASH_COMMIT_BARRIER"]=Path.Combine(directory,"committed");
            var p=Start(Environment.GetEnvironmentVariable("NEXACONNECT_CASH_HOST_DLL")!,[mode],settings);
            await Until(()=>Task.FromResult(File.Exists(ready)),p);return p;
        }
        static async Task Kill(Process p){if(!p.HasExited){p.Kill(entireProcessTree:true);await p.WaitForExitAsync();}}
        async Task Broker(string operation)
        {
            string name=Environment.GetEnvironmentVariable("NEXACONNECT_CASH_BROKER_CONTAINER")!;
            if(!Regex.IsMatch(name,@"^cashclose-[a-f0-9]{32}-rabbitmq-1$"))throw new InvalidOperationException("Invalid disposable broker identity.");
            var info=new ProcessStartInfo("docker"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            info.ArgumentList.Add(operation);info.ArgumentList.Add(name);
            using var p=Process.Start(info)!;
            Task output=p.StandardOutput.ReadToEndAsync(),error=p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));await Task.WhenAll(output,error);Assert.Equal(0,p.ExitCode);
        }
        try
        {
            Guid org=Guid.NewGuid(),restaurant=Guid.NewGuid(),branch=Guid.NewGuid(),store=Guid.NewGuid(),terminal=Guid.NewGuid();
            await Sql(source!,"INSERT INTO stores(id,restaurant_id,branch_id,code,name,operational_status,created_at_utc,created_by,updated_at_utc,updated_by) VALUES($1,$2,$3,'test','Test','active',now(),'test',now(),'test')",store,restaurant,branch);
            await Sql(source!,"INSERT INTO terminals(id,restaurant_id,store_id,code,device_type,registration_status,registered_at_utc,created_at_utc,updated_at_utc) VALUES($1,$2,$3,'test','pos','active',now(),now(),now())",terminal,restaurant,store);
            var shift=POS::NexaConnect.Services.POS.Domain.Shifts.Shift.Open(Guid.NewGuid(),store,terminal,"cashier","TEST",Guid.NewGuid(),DateTimeOffset.UtcNow);
            await new Source.PostgresShiftStore(source!).CreateAsync(shift,default);
            var cash=new Source.PostgresCashSessionStore(source!);Guid session=await cash.OpenAsync(shift.Id,store,"THB",100,default);
            DateTimeOffset occurred=DateTimeOffset.UtcNow;await cash.CloseAsync(session,95,1,"cashier",terminal,default);
            var publisher=new Source.PostgresCashClosePublicationStore(source!);
            var candidate=Assert.Single(await publisher.FindAsync(null,default));
            bool[] concurrent=await Task.WhenAll(Enumerable.Range(0,4).Select(_=>publisher.PublishAsync(candidate,org,Guid.NewGuid(),default)));
            Assert.Single(concurrent,x=>x);
            Process consumer=await Host("consumer",true), dispatcher=await Host("dispatcher");
            await Until(()=>Task.FromResult(File.Exists(Path.Combine(directory,"committed"))),consumer);
            await Until(async()=>await Scalar(source!,"SELECT count(*)::int FROM outbox_messages WHERE published_at_utc IS NULL")==0,dispatcher);
            await Kill(consumer); // Fact committed; delivery was not acknowledged.
            await Broker("stop");
            var reviews=new Source.PostgresCashReviewStore(source!);
            var scope=new Replay.CashReviewScope(org,restaurant,branch,store);
            await reviews.ResolveAsync(scope,session,POS::NexaConnect.Services.POS.Domain.CashReviews.CashReviewDecision.Create("approve","checked"),
                "supervisor",Guid.NewGuid(),2,0,Guid.NewGuid(),new string('a',64),DateTimeOffset.UtcNow,default);
            await publisher.PublishAsync(candidate,org,Guid.NewGuid(),default);
            var late=new OrderManualTenderSettledV1(Guid.NewGuid(),Guid.NewGuid(),occurred,org,restaurant,branch,Guid.NewGuid(),Guid.NewGuid(),terminal,"cash",10,"THB");
            await new Source.PostgresOrderSettlementProjectionStore(source!).ProjectAsync(late,default);
            await publisher.PublishAsync(candidate,org,Guid.NewGuid(),default);
            await Until(async()=>await Scalar(source!,"SELECT count(*)::int FROM outbox_messages WHERE published_at_utc IS NULL AND last_error_category IS NOT NULL")>0,dispatcher);
            Assert.Equal(2,await Scalar(source!,"SELECT count(*)::int FROM outbox_messages WHERE published_at_utc IS NULL"));
            await Kill(dispatcher);await Broker("start");
            consumer=await Host("consumer");dispatcher=await Host("dispatcher");
            var repository=new REPORTING::NexaConnect.Services.Reporting.Infrastructure.Persistence.PostgresCashCloseRepository(sink!);
            var query=new Reports.CashCloseQuery(org,branch,store,occurred.AddDays(-1),occurred.AddDays(1),10,null,null);
            await Until(async()=> (await repository.ReadAsync(query,default)).SingleOrDefault()?.Snapshot.SnapshotVersion==3,consumer,dispatcher);
            await Until(async()=>await Scalar(source!,"SELECT count(*)::int FROM outbox_messages WHERE published_at_utc IS NULL")==0,dispatcher);
            await Kill(dispatcher);
            var before=Assert.Single(await repository.ReadAsync(query,default));Assert.Equal("review_required",before.Snapshot.ReviewStatus);Assert.Equal(-15,before.Snapshot.VarianceAmount);
            Assert.Empty(await repository.ReadAsync(query with{OrganizationId=Guid.NewGuid()},default));
            Assert.Empty(await repository.ReadAsync(query with{StoreId=Guid.NewGuid()},default));

            await using var connection=await new ConnectionFactory{Uri=new Uri(broker)}.CreateConnectionAsync();
            await using var channel=await connection.CreateChannelAsync(new CreateChannelOptions(true,true));
            await using var transport=new RabbitMqOutboxTransport(Options.Create(new OutboxOptions{ConnectionString=broker,Exchange=exchange}));
            foreach(var e in (await Events()).AsEnumerable().Reverse())
                await transport.PublishAsync(new(e.EventId,"pos.cash-close.snapshot.v1",1,"cash-session",session,JsonSerializer.Serialize(e),e.CorrelationId.ToString("D"),e.OccurredAtUtc),default);
            await channel.BasicPublishAsync(exchange,"pos.cash-close.snapshot.v1",true,new BasicProperties{Persistent=true},Encoding.UTF8.GetBytes("{}"));
            await Until(async()=>await channel.MessageCountAsync(queue+".dead")==1,consumer);
            Assert.Equal(before,Assert.Single(await repository.ReadAsync(query,default)));
            Assert.Equal(3,await Scalar(sink!,"SELECT count(*)::int FROM cash_close_event_receipts"));
            await Kill(consumer);
            await Sql(sink!,Script("Reporting","0015_cash_close_projection","down"));
            await Sql(sink!,Script("Reporting","0015_cash_close_projection","up"));
            consumer=await Host("consumer");
            var selection=new Replay.CashCloseReplayRequest(org,branch,store,occurred.AddDays(-1),occurred.AddDays(1),100);
            var replayStore=new Source.PostgresCashCloseReplayStore(source!);
            var replayService=new Replay.CashCloseReplay(replayStore,new POS::NexaConnect.Services.POS.Infrastructure.Messaging.CashCloseReplayTransport(transport));
            var preview=await replayService.PreviewAsync(selection,default);Assert.Equal(3,preview.Count);
            Assert.Equal(0,await Scalar(source!,"SELECT count(*)::int FROM cash_close_replay_runs"));
            Assert.Equal(0,(await replayService.PreviewAsync(selection with{OrganizationId=Guid.NewGuid()},default)).Count);
            string fingerprint=await SourceFingerprint();
            for(int attempt=0;attempt<2;attempt++)
            {
                var args=new[]{"--organization",org.ToString(),"--branch",branch.ToString(),"--store",store.ToString(),
                    "--from",selection.FromUtc.UtcDateTime.ToString("O"),"--to",selection.ToUtc.UtcDateTime.ToString("O"),"--limit","100",
                    "--execute","--operator",Guid.NewGuid().ToString(),"--reason","rebuild","--manifest",preview.Manifest};
                var cli=Start(Environment.GetEnvironmentVariable("NEXACONNECT_CASH_REPLAY_DLL")!,args,
                    new(){["NEXACONNECT_CASH_CLOSE_REPLAY_DB"]=posDb,["NEXACONNECT_CASH_CLOSE_REPLAY_BROKER"]=broker,["NEXACONNECT_CASH_CLOSE_REPLAY_EXCHANGE"]=exchange});
                await cli.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));Assert.Equal(0,cli.ExitCode);
            }
            await Until(async()=>(await repository.ReadAsync(query,default)).SingleOrDefault()?.Snapshot.SnapshotVersion==3,consumer);
            await channel.BasicPublishAsync(exchange,"pos.cash-close.snapshot.v1",true,new BasicProperties{Persistent=true},Encoding.UTF8.GetBytes("{}"));
            await Until(async()=>await channel.MessageCountAsync(queue+".dead")==2,consumer);
            Assert.Equal(3,await Scalar(sink!,"SELECT count(*)::int FROM cash_close_event_receipts"));
            Assert.Equal(before.Snapshot,Assert.Single(await repository.ReadAsync(query,default)).Snapshot);
            Assert.Equal(fingerprint,await SourceFingerprint());
            Assert.Equal(6,await Scalar(source!,"SELECT count(*)::int FROM cash_close_replay_attempts WHERE outcome='confirmed'"));
            await Assert.ThrowsAsync<PostgresException>(()=>Sql(source!,"DELETE FROM cash_close_replay_runs"));
            await Assert.ThrowsAsync<PostgresException>(()=>Sql(source!,Script("POS","0007_cash_close_replay_audit","down")));
            await Kill(consumer);
            await channel.QueueDeleteAsync(queue);await channel.QueueDeleteAsync(queue+".dead");await channel.ExchangeDeleteAsync(exchange);
        }
        finally
        {
            foreach(var p in processes){await Kill(p);p.Dispose();}
            // Disposable Compose project cleanup is owned by the runner even when a broker fault fails.
        }
    }
    private async Task<string> SourceFingerprint()
    {
        await using var q=source!.CreateCommand("""
            SELECT jsonb_build_object('sessions',(SELECT jsonb_agg(to_jsonb(s) ORDER BY id) FROM cash_sessions s),
              'movements',(SELECT jsonb_agg(to_jsonb(m) ORDER BY id) FROM cash_movements m),
              'checkpoints',(SELECT jsonb_agg(to_jsonb(p) ORDER BY cash_session_id) FROM cash_close_publications p),
              'outbox',(SELECT jsonb_agg(to_jsonb(o) ORDER BY id) FROM outbox_messages o))::text
            """);
        return (string)(await q.ExecuteScalarAsync())!;
    }
    private static async Task<int> Scalar(NpgsqlDataSource ds,string sql)
    {await using var q=ds.CreateCommand(sql);return (int)(await q.ExecuteScalarAsync())!;}
    private static async Task Until(Func<Task<bool>> condition,params Process[] processes)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while(!await condition())
        {Assert.DoesNotContain(processes,p=>p.HasExited);await Task.Delay(100,timeout.Token);}
    }
}
public sealed class CashClosePipelineFactAttribute : FactAttribute
{
    public CashClosePipelineFactAttribute()
    {
        if(!CashCloseDatabaseFactAttribute.Ready() || Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT")!="Testing" ||
            Environment.GetEnvironmentVariable("NEXACONNECT_CASH_CLOSE_ACCEPTANCE")!="1" ||
            new[]{"NEXACONNECT_CASH_HOST_DLL","NEXACONNECT_CASH_REPLAY_DLL","NEXACONNECT_CASH_RUN_DIR","NEXACONNECT_CASH_BROKER_CONTAINER","NEXACONNECT_RABBITMQ_INTEGRATION_URI"}
                .Any(x=>string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(x))))
            Skip="Requires guarded disposable cash-close runner with PostgreSQL, RabbitMQ and process hosts.";
    }
}
