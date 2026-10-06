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

public sealed class DayCutoffPipelineTests:IAsyncLifetime,ReportingApp.ICutoffSources,ReportingApp.IReportingCustomerAuthorizer,
    PosApp.IDayCloseEvidenceReader,IHttpClientFactory,PosAuth.IRestaurantScopeReader,PosAuth.IAuthorizationDecisionClient
{
    private readonly List<(NpgsqlDataSource Db,string Schema)> owned=[];
    private NpgsqlDataSource order=null!,payment=null!,pos=null!,reporting=null!;
    private readonly Guid organization=Guid.NewGuid(),restaurant=Guid.NewGuid(),branch=Guid.NewGuid();
    private DateOnly Date=>DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
    private EndOfDayWindow Window=>new(organization,restaurant,branch,new(Date.ToDateTime(TimeOnly.MinValue,DateTimeKind.Utc)),new(Date.AddDays(1).ToDateTime(TimeOnly.MinValue,DateTimeKind.Utc)));
    private PosDomain.DayIdentity Day=>new(organization,restaurant,branch,Date);
    private bool allowed=true,fail;
    private readonly List<Guid> capturedOperations=[];
    private ReportingApp.DayCutoffReconciliation Checker=>new(this,this,new ReportingDb.PostgresFinancialCompletenessRepository(reporting));
    private PosApp.DayCloseCutoff Workflow=>new(new CoordinatorDb.PostgresDayCutoffStore(pos),
        new CoordinatorDb.HttpCutoffEvidenceReader(this,new CoordinatorDb.PostgresDayCutoffStore(pos),this,TimeProvider.System),
        this,this,TimeProvider.System,NullLoggerFactory.Instance);
    private PosDomain.PreparationCommand Command(long version=0)=>new(branch,Date,Guid.NewGuid(),version,"routine_close");
    [ReportingDatabaseFact]
    public async Task Missing_delivery_blocks_then_original_projection_allows_ready_and_same_total_late_work_invalidates()
    {
        var aggregate=OrderDomain.OrderAggregate.Create(Guid.NewGuid(),organization,branch,
            [new OrderDomain.OrderLine(Guid.NewGuid(),"Rice",100,1,"kitchen")],"THB",restaurantId:restaurant,workflowPaymentMethod:"card");
        aggregate.Submit();aggregate.MarkInventoryReserved();aggregate.MarkKitchenAccepted();
        var repository=new OrderDb.PostgresOrderRepository(order);await repository.SaveAsync(aggregate,default);
        await Sql(order,"UPDATE orders SET created_at_utc=$1 WHERE id=$2",Window.FromUtc.AddHours(1),aggregate.Id);
        aggregate.MarkPaid(Guid.NewGuid());aggregate.IssueReceipt(Window.FromUtc.AddHours(2),"card");await repository.SaveAsync(aggregate,default);
        var blocked=await Workflow.PrepareAsync(organization,Command(),new("manager","token"),default);
        Assert.Equal("blocked",blocked.Status);Assert.Contains("cutoff_financial_gaps",blocked.Blockers);
        var cut=blocked.Snapshot!.Cutoff!;var manifest=(await new OrderDb.PostgresOrderCutoffStore(order).ReadAsync(Window,cut.Order.ManifestId,default))!.Manifest;
        var original=Assert.Single(manifest.Sales);
        var projector=new ReportingApp.SaleFinancialReporting(new ReportingDb.PostgresSaleFinancialFactRepository(reporting));
        Assert.True(await projector.ProjectAsync(original,default));Assert.False(await projector.ProjectAsync(original,default));
        var ready=await Workflow.PrepareAsync(organization,Command(blocked.Version),new("manager","token"),default);
        Assert.Equal("ready_for_review",ready.Status);Assert.Equal(0,ready.Snapshot!.Cutoff!.FinancialGaps);
        Assert.True(ready.Snapshot.Cutoff.Order.Generation>cut.Order.Generation);
        var loaded=await Workflow.ReadAsync(organization,branch,Date,new("manager","token"),default);Assert.Equal(ready.Version,loaded.Version);
        var late=OrderDomain.OrderAggregate.Create(Guid.NewGuid(),organization,branch,[new OrderDomain.OrderLine(Guid.NewGuid(),"Rice",5,1,"kitchen")],"THB",restaurantId:restaurant);
        await repository.SaveAsync(late,default);await Sql(order,"UPDATE orders SET created_at_utc=$1 WHERE id=$2",Window.FromUtc.AddHours(3),late.Id);
        loaded=await Workflow.ReadAsync(organization,branch,Date,new("manager","token"),default);
        Assert.Equal("blocked",loaded.Status);Assert.Contains("cutoff_superseded",loaded.Blockers);
        Assert.Equal(ready.Snapshot.GrossSales,loaded.Snapshot!.GrossSales);
        Assert.Equal(ready.Snapshot.Cutoff.Order.ManifestId,loaded.Snapshot.Cutoff!.Order.ManifestId);
    }
    [ReportingDatabaseFact]
    public async Task Partial_capture_response_loss_resumes_original_identity_after_restart_and_live_denial_precedes_writes()
    {
        var command=Command();var store=new CoordinatorDb.PostgresDayCutoffStore(pos);
        var actor=new PosApp.PreparationActor("manager",Guid.NewGuid());
        await store.BeginAsync(Day,command,actor,DateTimeOffset.UtcNow.AddSeconds(-31),default);
        var partial=await new OrderDb.PostgresOrderCutoffStore(order).CaptureAsync(new(command.OperationId,Window),"manager",default);
        var resumed=await Workflow.PrepareAsync(organization,command,new("manager","token"),default);
        Assert.Equal("ready_for_review",resumed.Status);Assert.Equal(partial.ManifestId,resumed.Snapshot!.Cutoff!.Order.ManifestId);
        Assert.All(capturedOperations,x=>Assert.Equal(command.OperationId,x));
        var replay=await Workflow.PrepareAsync(organization,command,new("manager","token"),default);Assert.Equal(resumed.Version,replay.Version);
        allowed=false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Workflow.PrepareAsync(organization,Command(resumed.Version),new("manager","token"),default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Workflow.ReadAsync(organization,branch,Date,new("manager","token"),default));
        allowed=true;fail=true;
        var invalid=await Workflow.ReadAsync(organization,branch,Date,new("manager","token"),default);
        Assert.Equal("blocked",invalid.Status);Assert.Contains("source_unavailable",invalid.Blockers);
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
        Task.FromResult(new PosDomain.DayEvidence("UTC","THB",Window.FromUtc,Window.ToUtc,0,0,0,0,[],new('a',64),new('b',64),new('c',64),0,0,0,0,0,0,[],DateTimeOffset.UtcNow));
    public HttpClient CreateClient(string name)=>new(new Handler(async(request,ct)=>
    {
        if(fail)return new(HttpStatusCode.ServiceUnavailable);
        Assert.Equal("Bearer token",request.Headers.Authorization!.ToString());
        string service=name.Replace("DayCutoff","");object result;
        if(service=="Reporting")result=await Checker.CheckAsync((await request.Content!.ReadFromJsonAsync<CutoffReconciliationCommand>(cancellationToken:ct))!,"Bearer token",ct);
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
