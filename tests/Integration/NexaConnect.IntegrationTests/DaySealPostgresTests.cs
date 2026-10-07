extern alias ORDER;
extern alias PAYMENT;
extern alias POS;
extern alias REPORTING;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Contracts.IntegrationEvents;
using OrderDb=ORDER::NexaConnect.Services.Order.Infrastructure.Persistence;
using OrderDomain=ORDER::NexaConnect.Services.Order.Domain;
using PaymentDb=PAYMENT::NexaConnect.Services.Payment.Infrastructure;
using PosDb=POS::NexaConnect.Services.POS.Infrastructure.Persistence;
using CoordinatorDb=POS::NexaConnect.Services.POS.Infrastructure.DayClose;
using PosApp=POS::NexaConnect.Services.POS.Application.DayClose;
using PosDomain=POS::NexaConnect.Services.POS.Domain.DayClose;
using PosAuth=POS::NexaConnect.Services.POS.Application.Shifts;
using ReportingApp=REPORTING::NexaConnect.Services.Reporting.Application;
using ReportingDb=REPORTING::NexaConnect.Services.Reporting.Infrastructure.Persistence;

namespace NexaConnect.IntegrationTests;

public sealed class DaySealPostgresTests:IAsyncLifetime,ReportingApp.ICutoffSources,ReportingApp.ISealedSources,ReportingApp.IReportingCustomerAuthorizer,
    PosApp.IDayCloseEvidenceReader,IHttpClientFactory,PosAuth.IRestaurantScopeReader,PosAuth.IAuthorizationDecisionClient
{
    private readonly List<(NpgsqlDataSource Db,string Schema)> owned=[];
    private NpgsqlDataSource order=null!,payment=null!,pos=null!,reporting=null!;
    private readonly Guid organization=Guid.NewGuid(),restaurant=Guid.NewGuid(),branch=Guid.NewGuid();
    private DateOnly Date=>DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
    private EndOfDayWindow Window=>new(organization,restaurant,branch,new(Date.ToDateTime(TimeOnly.MinValue,DateTimeKind.Utc)),new(Date.AddDays(1).ToDateTime(TimeOnly.MinValue,DateTimeKind.Utc)));
    private PosDomain.DayIdentity Day=>new(organization,restaurant,branch,Date);
    private bool allowed=true,fail,calendarDrift;
    private TimeProvider sealClock=TimeProvider.System;
    private readonly List<Guid> capturedOperations=[];
    private ReportingApp.DayCutoffReconciliation Checker=>new(this,this,new ReportingDb.PostgresFinancialCompletenessRepository(reporting));
    private PosApp.DayCloseCutoff Workflow=>new(new CoordinatorDb.PostgresDayCutoffStore(pos),
        new CoordinatorDb.HttpCutoffEvidenceReader(this,new CoordinatorDb.PostgresDayCutoffStore(pos),this,TimeProvider.System),
        this,this,TimeProvider.System,NullLoggerFactory.Instance);
    private PosDomain.PreparationCommand Command(long version=0)=>new(branch,Date,Guid.NewGuid(),version,"routine_close");
    private PosApp.DayCloseSealing Sealing=>new(new CoordinatorDb.PostgresDaySealStore(pos),
        new CoordinatorDb.HttpDaySealEvidenceReader(this,new CoordinatorDb.PostgresDaySealStore(pos),this,sealClock),this,this,sealClock,NullLoggerFactory.Instance);
    public async Task<SourceSealRead<OrderDaySummary>> OrderSealAsync(EndOfDayWindow w,Guid id,string bearer,CancellationToken ct)=>
        await new OrderDb.PostgresOrderCutoffStore(order).ReadSealAsync(w,id,ct)??throw new InvalidOperationException();
    public async Task<SourceSealRead<PaymentDaySummary>> PaymentSealAsync(EndOfDayWindow w,Guid id,string bearer,CancellationToken ct)=>
        await new PaymentDb.PostgresPaymentCutoffStore(payment).ReadSealAsync(w,id,ct)??throw new InvalidOperationException();
    private async Task<PosApp.PreparationView> Reviewed()=>await Workflow.PrepareAsync(organization,Command(),new("manager","token"),default);
    private PosDomain.PreparationCommand SealCommand(long reviewed,long version=0)=>Command(version) with{ReviewedCutoffVersion=reviewed};
    private static SourceSealCommand SourceCommand(EndOfDayWindow w,PosDomain.CutoffReference r,Guid? operation=null)=>new(operation??Guid.NewGuid(),w,r.ManifestId,new(r.RevisionEpoch!.Value,r.SourceRevision!.Value));
    private async Task<Guid> Store()
    {
        var id=Guid.NewGuid();await Sql(pos,"INSERT INTO stores(id,restaurant_id,branch_id,code,name,operational_status,created_at_utc,created_by,updated_at_utc,updated_by) VALUES($1,$2,$3,'test','Test','active',now(),'test',now(),'test')",id,restaurant,branch);return id;
    }
    [ReportingDatabaseFact]
    public async Task Seals_remain_immutable_after_late_changes_and_new_review_creates_a_new_set()
    {
        var reviewed=await Reviewed();Assert.Equal("ready_for_review",reviewed.Status);
        var sealedDay=await Sealing.SealAsync(organization,SealCommand(reviewed.Version),new("manager","token"),default);
        Assert.Equal("ready_for_review",sealedDay.Status);Assert.Equal(0,sealedDay.PendingSealChanges);
        var original=sealedDay.Snapshot!.Seals!;
        var late=OrderDomain.OrderAggregate.Create(Guid.NewGuid(),organization,branch,[new OrderDomain.OrderLine(Guid.NewGuid(),"Rice",10,1,"kitchen")],"THB",restaurantId:restaurant);
        await new OrderDb.PostgresOrderRepository(order).SaveAsync(late,default);
        var changed=await Sealing.ReadAsync(organization,branch,Date,new("manager","token"),default);
        Assert.Equal("blocked",changed.Status);Assert.Contains("sealed_changes_pending",changed.Blockers);
        Assert.Equal(original,changed.Snapshot!.Seals);Assert.True(changed.PendingSealChanges>0);
        var source=await OrderSealAsync(Window,original.Order.SealId,"token",default);
        Assert.True(source.JournalComplete);Assert.NotEmpty(source.Changes);Assert.True(source.PendingChanges>0);
        var refreshed=await Workflow.PrepareAsync(organization,Command(reviewed.Version),new("manager","token"),default);
        var resealed=await Sealing.SealAsync(organization,SealCommand(refreshed.Version,changed.Version),new("manager","token"),default);
        Assert.Equal("ready_for_review",resealed.Status);Assert.NotEqual(original.Order.SealId,resealed.Snapshot!.Seals!.Order.SealId);
        Assert.Equal(original.Order.SealId,(await OrderSealAsync(Window,original.Order.SealId,"token",default)).Seal.SealId);
    }
    [ReportingDatabaseFact]
    public async Task Partial_response_loss_resumes_pinned_manifests_after_cutoff_refresh_and_restart()
    {
        var reviewed=await Reviewed();var command=SealCommand(reviewed.Version);
        var store=new CoordinatorDb.PostgresDaySealStore(pos);var actor=new PosApp.PreparationActor("manager",Guid.NewGuid());
        await store.BeginAsync(Day,command,actor,DateTimeOffset.UtcNow,default);
        sealClock=new SealClock(DateTimeOffset.UtcNow.AddSeconds(31));
        var partial=await new OrderDb.PostgresOrderCutoffStore(order).SealAsync(SourceCommand(Window,reviewed.Snapshot!.Cutoff!.Order,command.OperationId),"manager",default);
        var refreshed=await Workflow.PrepareAsync(organization,Command(reviewed.Version),new("manager","token"),default);
        Assert.NotEqual(reviewed.Snapshot.Cutoff.Order.ManifestId,refreshed.Snapshot!.Cutoff!.Order.ManifestId);
        var resumed=await Sealing.SealAsync(organization,command,new("manager","token"),default);
        Assert.Equal("ready_for_review",resumed.Status);Assert.Equal(partial.SealId,resumed.Snapshot!.Seals!.Order.SealId);
        Assert.Equal(reviewed.Snapshot.Cutoff.Payment.ManifestId,resumed.Snapshot.Seals.Payment.ManifestId);
        var replay=await Sealing.SealAsync(organization,command,new("manager","token"),default);Assert.Equal(resumed.Version,replay.Version);
        await Assert.ThrowsAsync<PosDomain.DayCloseConflictException>(()=>Sealing.SealAsync(organization,command with{ReviewedCutoffVersion=refreshed.Version},new("manager","token"),default));
    }
    [ReportingDatabaseFact]
    public async Task Competing_managers_replacement_and_revocation_fence_commands_and_old_completion()
    {
        var reviewed=await Reviewed();var store=new CoordinatorDb.PostgresDaySealStore(pos);
        var command=SealCommand(reviewed.Version);var actor=new PosApp.PreparationActor("manager",Guid.NewGuid());
        var started=await store.BeginAsync(Day,command,actor,DateTimeOffset.UtcNow,default);
        sealClock=new SealClock(DateTimeOffset.UtcNow.AddSeconds(31));
        var replaced=await Sealing.SealAsync(organization,SealCommand(reviewed.Version,started.State.Version),new("manager-2","token"),default);
        Assert.Equal("ready_for_review",replaced.Status);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>store.CompleteAsync(Day,started.ClaimId!.Value,replaced.Snapshot,actor,DateTimeOffset.UtcNow,default));
        await Assert.ThrowsAsync<PosDomain.DayCloseConflictException>(()=>Sealing.SealAsync(organization,command,new("manager","token"),default));
        allowed=false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Sealing.SealAsync(organization,SealCommand(reviewed.Version,replaced.Version),new("manager","token"),default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Sealing.ReadAsync(organization,branch,Date,new("manager","token"),default));
        allowed=true;
        Assert.Null(await new PosDb.PostgresPosCutoffStore(pos).ReadSealAsync(Window with{OrganizationId=Guid.NewGuid()},replaced.Snapshot!.Seals!.Pos.SealId,default));
    }
    [ReportingDatabaseFact]
    public async Task Restored_values_conflict_with_old_revision_and_journal_rollback_preserves_a_seal()
    {
        var id=await Store();var reviewed=await Reviewed();var source=new PosDb.PostgresPosCutoffStore(pos);
        await Sql(pos,"UPDATE stores SET name='Changed' WHERE id=$1",id);await Sql(pos,"UPDATE stores SET name='Test' WHERE id=$1",id);
        await Assert.ThrowsAsync<NexaConnect.Infrastructure.Persistence.SnapshotOperationConflictException>(()=>source.SealAsync(SourceCommand(Window,reviewed.Snapshot!.Cutoff!.Pos),"manager",default));
        var refreshed=await Workflow.PrepareAsync(organization,Command(reviewed.Version),new("manager","token"),default);
        var seal=await source.SealAsync(SourceCommand(Window,refreshed.Snapshot!.Cutoff!.Pos),"manager",default);
        await using(var c=await pos.OpenConnectionAsync())await using(var t=await c.BeginTransactionAsync())
        {
            await using var q=new NpgsqlCommand("UPDATE stores SET name='Rolled back' WHERE id=$1",c,t);q.Parameters.AddWithValue(id);
            await q.ExecuteNonQueryAsync();await t.RollbackAsync();
        }
        var read=(await source.ReadSealAsync(Window,seal.SealId,default))!;Assert.Equal(0,read.PendingChanges);Assert.True(read.JournalComplete);
        foreach(var sql in new[]{"UPDATE source_day_seals SET actor='rewrite'","DELETE FROM source_day_seals","TRUNCATE source_day_seals",
            "UPDATE source_financial_changes SET revision=revision+1","DELETE FROM source_financial_changes","TRUNCATE source_financial_changes"})
            await Assert.ThrowsAsync<PostgresException>(()=>Sql(pos,sql));
        await Assert.ThrowsAsync<PostgresException>(()=>Sql(pos,File.ReadAllText(Path.Combine(Root(),"src/Tools/NexaConnect.DataMigration/Scripts/POS/0011_day_seals/down.sql"))));
    }
    [ReportingDatabaseFact]
    public async Task A_seal_serializes_an_untouched_branch_before_a_writer_and_the_writer_is_journaled()
    {
        var reviewed=await Reviewed();var command=SourceCommand(Window,reviewed.Snapshot!.Cutoff!.Order);
        var retention=new NexaConnect.Infrastructure.Persistence.PostgresEvidenceSeals<OrderDaySummary>(order);
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        var sealing=Task.Run(()=>retention.RetainAsync(command,"manager",(manifest,revision)=>
        {Assert.Equal(0,revision.Revision);entered.Set();if(!release.Wait(TimeSpan.FromSeconds(10)))throw new TimeoutException();},default));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        var aggregate=OrderDomain.OrderAggregate.Create(Guid.NewGuid(),organization,branch,[new OrderDomain.OrderLine(Guid.NewGuid(),"Rice",10,1,"kitchen")],"THB",restaurantId:restaurant);
        var writing=Task.Run(()=>new OrderDb.PostgresOrderRepository(order).SaveAsync(aggregate,default));
        try{Assert.NotEqual(writing,await Task.WhenAny(writing,Task.Delay(100)));}
        finally{release.Set();}
        var seal=await sealing;await writing;
        var read=await OrderSealAsync(Window,seal.SealId,"token",default);Assert.True(read.PendingChanges>0);Assert.True(read.JournalComplete);
    }
    [ReportingDatabaseFact]
    public async Task Change_lists_are_bounded_but_full_revision_coverage_and_seal_history_are_preserved()
    {
        var id=await Store();var reviewed=await Reviewed();var source=new PosDb.PostgresPosCutoffStore(pos);
        var command=SourceCommand(Window,reviewed.Snapshot!.Cutoff!.Pos);var seal=await source.SealAsync(command,"manager",default);
        Assert.Equal(seal.SealId,(await source.SealAsync(command,"manager",default)).SealId);
        await Assert.ThrowsAsync<NexaConnect.Infrastructure.Persistence.SnapshotOperationConflictException>(()=>source.SealAsync(command,"other",default));
        for(int i=0;i<260;i++)await Sql(pos,"UPDATE stores SET name=$1 WHERE id=$2","Change "+i,id);
        var read=(await source.ReadSealAsync(Window,seal.SealId,default))!;
        Assert.Equal(260,read.PendingChanges);Assert.Equal(256,read.Changes.Count);Assert.True(read.ChangesTruncated);Assert.True(read.JournalComplete);
    }
    [ReportingDatabaseFact]
    public async Task Outage_and_calendar_changes_clear_readiness_and_preserve_original_seals()
    {
        var reviewed=await Reviewed();var original=await Sealing.SealAsync(organization,SealCommand(reviewed.Version),new("manager","token"),default);
        fail=true;
        var blocked=await Sealing.ReadAsync(organization,branch,Date,new("manager","token"),default);
        Assert.Equal("blocked",blocked.Status);Assert.Contains("source_unavailable",blocked.Blockers);Assert.Equal(original.Snapshot!.Seals,blocked.Snapshot!.Seals);
        fail=false;
        var fresh=await Sealing.SealAsync(organization,SealCommand(reviewed.Version,blocked.Version),new("manager","token"),default);
        calendarDrift=true;
        var drift=await Sealing.ReadAsync(organization,branch,Date,new("manager","token"),default);
        Assert.Equal("blocked",drift.Status);Assert.Equal(fresh.Snapshot!.FromUtc,drift.Snapshot!.FromUtc);Assert.Equal(fresh.Snapshot.Seals,drift.Snapshot.Seals);
    }
    [ReportingDatabaseFact]
    public async Task Two_managers_have_one_winner_and_stale_review_cannot_create_source_seals()
    {
        var reviewed=await Reviewed();
        async Task<PosApp.PreparationView?> Attempt(string actor)
        {try{return await Sealing.SealAsync(organization,SealCommand(reviewed.Version),new(actor,"token"),default);}catch(PosDomain.DayCloseConflictException){return null;}}
        var attempts=await Task.WhenAll(Attempt("manager"),Attempt("manager-2"));Assert.Single(attempts,v=>v is not null);
        var winner=attempts.Single(v=>v is not null)!;
        await Workflow.PrepareAsync(organization,Command(reviewed.Version),new("manager","token"),default);
        await Assert.ThrowsAsync<PosDomain.DayCloseConflictException>(()=>Sealing.SealAsync(organization,SealCommand(reviewed.Version,winner.Version),new("manager","token"),default));
        Assert.Equal(winner.Version,(await new CoordinatorDb.PostgresDaySealStore(pos).ReadAsync(Day,default))!.Version);
    }
    [ReportingDatabaseFact]
    public async Task Existing_preparation_and_cutoff_operation_hashes_remain_compatible_with_the_prior_release()
    {
        var command=Command();var actor=new PosApp.PreparationActor("manager",Guid.NewGuid());
        var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var prior=new{day=Day,command=new{command.BranchId,command.BusinessDate,command.OperationId,command.ExpectedVersion,command.ReasonCode},actor.Subject};
        var expected=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(prior,json)));
        foreach(var(store,table)in new (PosApp.IDayCloseStore,string)[]{(new CoordinatorDb.PostgresDayCloseStore(pos),"branch_day_close_operations"),(new CoordinatorDb.PostgresDayCutoffStore(pos),"branch_day_cutoff_operations")})
        {
            await store.BeginAsync(Day,command,actor,DateTimeOffset.UtcNow,default);
            await using var q=pos.CreateCommand("SELECT fingerprint FROM "+table+" WHERE organization_id=$1 AND operation_id=$2");
            q.Parameters.AddWithValue(organization);q.Parameters.AddWithValue(command.OperationId);
            Assert.Equal(expected,await q.ExecuteScalarAsync()); // Allow-listed owning tables only; no data is composed into SQL.
        }
    }
    [ReportingDatabaseFact]
    public async Task Empty_seal_migrations_downgrade_and_reapply_without_changing_revision_epochs()
    {
        foreach(var(owner,db)in new[]{("Order",order),("Payment",payment),("POS",pos)})
        {
            var directory=Directory.GetDirectories(Path.Combine(Root(),"src/Tools/NexaConnect.DataMigration/Scripts",owner),"*_day_seals").Single();
            await using var epochQuery=db.CreateCommand("SELECT epoch FROM source_financial_epoch");var epoch=await epochQuery.ExecuteScalarAsync();
            await Sql(db,await File.ReadAllTextAsync(Path.Combine(directory,"down.sql")));await Sql(db,await File.ReadAllTextAsync(Path.Combine(directory,"up.sql")));
            Assert.Equal(epoch,await epochQuery.ExecuteScalarAsync());
        }
    }
    public Task<SourceCutoffRead<OrderDaySummary>> OrderAsync(EndOfDayWindow w,Guid id,string bearer,CancellationToken ct)=>
        ReadOrder(w,id,ct);
    private async Task<SourceCutoffRead<OrderDaySummary>> ReadOrder(EndOfDayWindow w,Guid id,CancellationToken ct)=>
        await new OrderDb.PostgresOrderCutoffStore(order).ReadAsync(w,id,ct)??throw new InvalidOperationException();
    public async Task<SourceCutoffRead<PaymentDaySummary>> PaymentAsync(EndOfDayWindow w,Guid id,string bearer,CancellationToken ct)=>
        await new PaymentDb.PostgresPaymentCutoffStore(payment).ReadAsync(w,id,ct)??throw new InvalidOperationException();
    public Task<bool> IsGrantedAsync(Guid org,Guid? target,string permission,string bearer,CancellationToken ct)=>Task.FromResult(allowed&&org==organization&&target==branch);
    public Task<PosAuth.RestaurantAuthorizationScope> GetAsync(Guid target,CancellationToken ct)=>Task.FromResult(new PosAuth.RestaurantAuthorizationScope(organization,restaurant,branch));
    public Task<PosAuth.AuthorizationDecision> DecideAsync(PosAuth.PosUserContext user,PosAuth.RestaurantAuthorizationScope scope,string permission,CancellationToken ct)=>Task.FromResult(new PosAuth.AuthorizationDecision(Guid.NewGuid(),allowed,null));
    public Task<PosDomain.DayEvidence> ReadAsync(PosDomain.DayIdentity day,string token,CancellationToken ct)=>
        Task.FromResult(new PosDomain.DayEvidence(calendarDrift?"Asia/Bangkok":"UTC","THB",Window.FromUtc,Window.ToUtc,0,0,0,0,[],new('a',64),new('b',64),new('c',64),0,0,0,0,0,0,[],DateTimeOffset.UtcNow));
    public HttpClient CreateClient(string name)=>new(new Handler(async(request,ct)=>
    {
        if(fail)return new(HttpStatusCode.ServiceUnavailable);
        Assert.Equal("Bearer token",request.Headers.Authorization!.ToString());
        string service=name.Replace("DayCutoff","");object result;
        if(request.RequestUri!.AbsolutePath.Contains("/seals"))
        {
            if(request.Method==HttpMethod.Post)
            {
                var command=(await request.Content!.ReadFromJsonAsync<SourceSealCommand>(cancellationToken:ct))!;
                var seal=service switch
                {
                    "Order"=>await new OrderDb.PostgresOrderCutoffStore(order).SealAsync(command,"manager",ct),
                    "Payment"=>await new PaymentDb.PostgresPaymentCutoffStore(payment).SealAsync(command,"manager",ct),
                    _=>await new PosDb.PostgresPosCutoffStore(pos).SealAsync(command,"manager",ct)
                };
                result=service switch
                {
                    "Order"=>await OrderSealAsync(command.Window,seal.SealId,"Bearer token",ct),
                    "Payment"=>await PaymentSealAsync(command.Window,seal.SealId,"Bearer token",ct),
                    _=>(await new PosDb.PostgresPosCutoffStore(pos).ReadSealAsync(command.Window,seal.SealId,ct))!
                };
            }
            else
            {
                var id=Guid.Parse(request.RequestUri.AbsolutePath.Split('/').Last());
                result=service switch
                {
                    "Order"=>await OrderSealAsync(Window,id,"Bearer token",ct),
                    "Payment"=>await PaymentSealAsync(Window,id,"Bearer token",ct),
                    _=>(await new PosDb.PostgresPosCutoffStore(pos).ReadSealAsync(Window,id,ct))!
                };
            }
        }
        else if(service=="Reporting" && request.RequestUri.AbsolutePath.EndsWith("day-seal-reconciliation"))
            result=await new ReportingApp.SealedDayReconciliation(this,this,new ReportingDb.PostgresFinancialCompletenessRepository(reporting)).CheckAsync((await request.Content!.ReadFromJsonAsync<SealedReconciliationCommand>(cancellationToken:ct))!,"Bearer token",ct);
        else if(service=="Reporting")result=await Checker.CheckAsync((await request.Content!.ReadFromJsonAsync<CutoffReconciliationCommand>(cancellationToken:ct))!,"Bearer token",ct);
        else if(request.Method==HttpMethod.Post)
        {
            var command=(await request.Content!.ReadFromJsonAsync<SourceCutoffCommand>(cancellationToken:ct))!;capturedOperations.Add(command.OperationId);
            result=service switch
            {
                "Order"=>new SourceCutoffRead<OrderDaySummary>(await new OrderDb.PostgresOrderCutoffStore(order).CaptureAsync(command,"manager",ct),true),
                "Payment"=>new SourceCutoffRead<PaymentDaySummary>(await new PaymentDb.PostgresPaymentCutoffStore(payment).CaptureAsync(command,"manager",ct),true),
                _=>new SourceCutoffRead<PosDaySummary>(await new PosDb.PostgresPosCutoffStore(pos).CaptureAsync(command,"manager",ct),true)
            };
        }
        else
        {
            var id=Guid.Parse(request.RequestUri!.AbsolutePath.Split('/').Last());
            result=service switch
            {
                "Order"=>await ReadOrder(Window,id,ct),
                "Payment"=>(await new PaymentDb.PostgresPaymentCutoffStore(payment).ReadAsync(Window,id,ct))!,
                _=>(await new PosDb.PostgresPosCutoffStore(pos).ReadAsync(Window,id,ct))!
            };
        }
        return new(HttpStatusCode.OK){Content=JsonContent.Create(result)};
    })){BaseAddress=new Uri("https://cutoff.test/")};
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> action):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>action(request,ct);}
    private sealed class SealClock(DateTimeOffset now):TimeProvider{public override DateTimeOffset GetUtcNow()=>now;}
    public async Task InitializeAsync()
    {
        string? input=Environment.GetEnvironmentVariable("NEXACONNECT_REPORTING_INTEGRATION_DB");if(string.IsNullOrWhiteSpace(input))return;
        foreach(string service in new[]{"Order","Payment","POS","Reporting"})
        {
            string schema="cutoff_pipeline_"+Guid.NewGuid().ToString("N");
            var db=NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(input){SearchPath=schema+",public"}.ConnectionString);owned.Add((db,schema));
            await Sql(db,"CREATE SCHEMA "+new NpgsqlCommandBuilder().QuoteIdentifier(schema));
            foreach(string dir in Directory.GetDirectories(Path.Combine(Root(),"src/Tools/NexaConnect.DataMigration/Scripts",service)).Order())await Sql(db,await File.ReadAllTextAsync(Path.Combine(dir,"up.sql")));
            if(service=="Order")order=db;else if(service=="Payment")payment=db;else if(service=="POS")pos=db;else reporting=db;
        }
    }
    public async Task DisposeAsync(){foreach(var(db,schema)in owned){try{await Sql(db,"DROP SCHEMA "+new NpgsqlCommandBuilder().QuoteIdentifier(schema)+" CASCADE");}finally{await db.DisposeAsync();}}}
    private static async Task Sql(NpgsqlDataSource db,string text,params object[] values){await using var query=db.CreateCommand(text);foreach(var value in values)query.Parameters.AddWithValue(value);await query.ExecuteNonQueryAsync();}
    private static string Root(){var dir=new DirectoryInfo(AppContext.BaseDirectory);while(!File.Exists(Path.Combine(dir.FullName,"NexaConnect.sln")))dir=dir.Parent!;return dir.FullName;}
}
