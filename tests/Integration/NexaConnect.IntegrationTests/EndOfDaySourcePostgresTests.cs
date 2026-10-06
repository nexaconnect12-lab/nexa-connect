extern alias ORDER;
extern alias PAYMENT;
extern alias POS;
using Npgsql;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Contracts.IntegrationEvents;
using Orders=ORDER::NexaConnect.Services.Order.Domain;
using OrderDb=ORDER::NexaConnect.Services.Order.Infrastructure.Persistence;
using PaymentDb=PAYMENT::NexaConnect.Services.Payment.Infrastructure;
using Providers=PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers;
using Payments=PAYMENT::NexaConnect.Services.Payment.Application.Intents;
using PosDb=POS::NexaConnect.Services.POS.Infrastructure.Persistence;

namespace NexaConnect.IntegrationTests;

public sealed class EndOfDaySourcePostgresTests:IAsyncLifetime
{
    private readonly List<(NpgsqlDataSource Source,string Schema)> owned=[];
    private NpgsqlDataSource? order,payment,pos;
    private readonly Guid organization=Guid.NewGuid(),restaurant=Guid.NewGuid(),branch=Guid.NewGuid();
    private readonly DateTimeOffset from=DateTimeOffset.UtcNow.AddHours(-2);
    private EndOfDayWindow Window(DateTimeOffset to)=>new(organization,restaurant,branch,from,to);

    [ReportingDatabaseFact]
    public async Task Order_day_matches_receipts_and_keeps_order_time_distinct_from_tender_time()
    {
        async Task<Guid> Save(decimal amount,DateTimeOffset created,DateTimeOffset paid,Guid tenant,Guid targetBranch)
        {
            var aggregate=Orders.OrderAggregate.Create(Guid.NewGuid(),tenant,targetBranch,[new Orders.OrderLine(Guid.NewGuid(),"Rice",amount,1,"kitchen")],"THB",restaurantId:restaurant,workflowPaymentMethod:"card");
            aggregate.Submit();aggregate.MarkInventoryReserved();aggregate.MarkKitchenAccepted();var db=new OrderDb.PostgresOrderRepository(order!);
            await db.SaveAsync(aggregate,default);
            await Sql(order!,"UPDATE orders SET created_at_utc=$1 WHERE id=$2",created,aggregate.Id);
            aggregate.MarkPaid(Guid.NewGuid());aggregate.IssueReceipt(paid,"card");await db.SaveAsync(aggregate,default);return aggregate.Id;
        }
        var to=from.AddHours(1);
        await Save(100,from,from.AddMinutes(10),organization,branch);
        await Save(40,from.AddDays(-1),from.AddMinutes(20),organization,branch);
        await Save(900,to,to,organization,branch);
        await Save(800,from,from,Guid.NewGuid(),Guid.NewGuid());
        var db=new OrderDb.PostgresOrderDayReader(order!);var summary=await db.ReadAsync(Window(to),default);
        Assert.Equal(100,summary.GrossSales);Assert.Equal(1,summary.CompletedOrders);Assert.Equal(140,Assert.Single(summary.Tenders).Amount);Assert.Equal(0,summary.EvidenceGaps); Assert.Matches("^[a-f0-9]{64}$",summary.EvidenceVersion!); Assert.Equal(summary.EvidenceVersion,(await db.ReadAsync(Window(to),default)).EvidenceVersion);
        Assert.Equal(0,(await db.ReadAsync(Window(to) with{OrganizationId=Guid.NewGuid()},default)).GrossSales);
        var drafts=new List<Guid>();
        for(int i=0;i<2;i++){
            var draft=Orders.OrderAggregate.Create(Guid.NewGuid(),organization,branch,[new Orders.OrderLine(Guid.NewGuid(),"Rice",5,1,"kitchen")],"THB",restaurantId:restaurant,workflowPaymentMethod:"card");
            await new OrderDb.PostgresOrderRepository(order!).SaveAsync(draft,default);drafts.Add(draft.Id);
            await Sql(order!,"UPDATE orders SET created_at_utc=$1,status=$2 WHERE id=$3",from,i==0?"draft":"cancelled",draft.Id);
        }
        var identityBefore=await db.ReadAsync(Window(to),default);
        await Sql(order!,"UPDATE orders SET status=CASE WHEN id=$1 THEN 'cancelled' ELSE 'draft' END WHERE id=$1 OR id=$2",drafts[0],drafts[1]);
        var identityAfter=await db.ReadAsync(Window(to),default);
        Assert.Equal(identityBefore.GrossSales,identityAfter.GrossSales);Assert.Equal(identityBefore.UnresolvedOrders,identityAfter.UnresolvedOrders);
        Assert.NotEqual(identityBefore.EvidenceVersion,identityAfter.EvidenceVersion);
        Assert.Empty((await db.ReadAsync(Window(to) with{RestaurantId=Guid.NewGuid()},default)).Tenders);
        Assert.Equal(0,(await db.ReadAsync(Window(to) with{BranchId=Guid.NewGuid()},default)).GrossSales);
    }

    [ReportingDatabaseFact]
    public async Task Payment_day_uses_completed_refund_time_and_retains_older_uncertain_work()
    {
        var options=Microsoft.Extensions.Options.Options.Create(new Providers.PaymentProviderOptions());
        var intents=new PaymentDb.PostgresPaymentIntents(payment!,options);var actor=new Payments.PaymentMutationContext("acceptance",Guid.NewGuid());
        var intent=intents.Create(organization,new(restaurant,branch,Guid.NewGuid(),Guid.NewGuid().ToString(),100,"THB","card"),actor);
        var auth=intents.BeginAuthorization(organization,intent.Id,actor);
        intents.CompleteAuthorization(organization,intent.Id,auth.Intent.ConcurrencyVersion,Providers.ProviderAuthorizationOutcome.Authorized,"charge",null,actor);
        var capture=intents.BeginCapture(organization,intent.Id,actor);
        intents.CompleteCapture(organization,intent.Id,capture.Intent.ConcurrencyVersion,Providers.ProviderCaptureOutcome.Captured,"capture",null,actor);
        var refunds=new PaymentDb.PostgresPaymentRefunds(payment!,options);
        var lease=refunds.Begin(organization,intent.Id,new(Guid.NewGuid(),25,"THB","customer_request",Guid.NewGuid()),actor);
        var completed=refunds.Complete(organization,lease.Refund.Id,lease.Refund.ConcurrencyVersion,Providers.ProviderRefundOutcome.Refunded,"refund",null,actor);
        var uncertain=refunds.Begin(organization,intent.Id,new(Guid.NewGuid(),10,"THB","customer_request",Guid.NewGuid()),actor);
        refunds.Complete(organization,uncertain.Refund.Id,uncertain.Refund.ConcurrencyVersion,Providers.ProviderRefundOutcome.Unknown,null,null,actor);
        intents.Create(organization,new(restaurant,branch,Guid.NewGuid(),Guid.NewGuid().ToString(),20,"THB","card"),actor);
        var to=DateTimeOffset.UtcNow;var db=new PaymentDb.PostgresPaymentDayReader(payment!);
        var result=await db.ReadAsync(Window(to),default);
        Assert.Equal(25,result.CompletedRefunds);Assert.Equal(1,result.UnresolvedPayments);Assert.Equal(1,result.UnresolvedRefunds);Assert.Equal(0,result.EvidenceGaps); Assert.Matches("^[a-f0-9]{64}$",result.EvidenceVersion!); Assert.Equal(result.EvidenceVersion,(await db.ReadAsync(Window(to),default)).EvidenceVersion);
        var recovery=refunds.ClaimExpired(organization,uncertain.Refund.Id,actor);
        refunds.Reconcile(organization,uncertain.Refund.Id,recovery.Refund.ConcurrencyVersion,Providers.ProviderRefundOutcome.Unknown,null,null,actor);
        var changed=await db.ReadAsync(Window(to),default); Assert.Equal(result.CompletedRefunds,changed.CompletedRefunds); Assert.NotEqual(result.EvidenceVersion,changed.EvidenceVersion);
        Assert.Equal(0,(await db.ReadAsync(Window(completed.CompletedAtUtc!.Value),default)).CompletedRefunds);
        Assert.Equal(0,(await db.ReadAsync(Window(to) with{OrganizationId=Guid.NewGuid()},default)).UnresolvedRefunds);
        Assert.Equal(0,(await db.ReadAsync(Window(to) with{RestaurantId=Guid.NewGuid()},default)).CompletedRefunds);
        Assert.Equal(0,(await db.ReadAsync(Window(to) with{BranchId=Guid.NewGuid()},default)).UnresolvedPayments);
    }

    [ReportingDatabaseFact]
    public async Task Pos_day_recomputes_late_cash_and_supersedes_prior_review_without_mutating_it()
    {
        Guid store=Guid.NewGuid(),terminal=Guid.NewGuid();
        await Sql(pos!,"INSERT INTO stores(id,restaurant_id,branch_id,code,name,operational_status,created_at_utc,created_by,updated_at_utc,updated_by) VALUES($1,$2,$3,'test','Test','active',now(),'test',now(),'test')",store,restaurant,branch);
        await Sql(pos!,"INSERT INTO terminals(id,restaurant_id,store_id,code,device_type,registration_status,registered_at_utc,created_at_utc,updated_at_utc) VALUES($1,$2,$3,'test','pos','active',now(),now(),now())",terminal,restaurant,store);
        var shift=POS::NexaConnect.Services.POS.Domain.Shifts.Shift.Open(Guid.NewGuid(),store,terminal,"cashier","TEST",Guid.NewGuid(),DateTimeOffset.UtcNow);
        await new PosDb.PostgresShiftStore(pos!).CreateAsync(shift,default);
        var cash=new PosDb.PostgresCashSessionStore(pos!);var session=await cash.OpenAsync(shift.Id,store,"THB",100,default);
        var occurred=DateTimeOffset.UtcNow;
        await cash.CloseAsync(session,95,1,"cashier",terminal,default);
        var reviews=new PosDb.PostgresCashReviewStore(pos!);
        var scope=new POS::NexaConnect.Services.POS.Application.CashReviews.CashReviewScope(organization,restaurant,branch,store);
        await reviews.ResolveAsync(scope,session,POS::NexaConnect.Services.POS.Domain.CashReviews.CashReviewDecision.Create("approve","checked"),"supervisor",Guid.NewGuid(),2,0,Guid.NewGuid(),new string('a',64),DateTimeOffset.UtcNow,default);
        var db=new PosDb.PostgresPosDayReader(pos!);
        var stableWindow=Window(DateTimeOffset.UtcNow);
        var before=await db.ReadAsync(stableWindow,default);
        Assert.Matches("^[a-f0-9]{64}$",before.EvidenceVersion!); Assert.Equal(before.EvidenceVersion,(await db.ReadAsync(stableWindow,default)).EvidenceVersion);
        Assert.Equal(-5,before.CashVariance);Assert.Equal(0,before.PendingCashReviews);Assert.Equal(1,before.OpenShifts);
        var late=new OrderManualTenderSettledV1(Guid.NewGuid(),Guid.NewGuid(),occurred,organization,restaurant,branch,Guid.NewGuid(),Guid.NewGuid(),terminal,"cash",10,"THB");
        await new PosDb.PostgresOrderSettlementProjectionStore(pos!).ProjectAsync(late,default);
        var after=await db.ReadAsync(stableWindow,default); Assert.NotEqual(before.EvidenceVersion,after.EvidenceVersion);
        Assert.Equal(-15,after.CashVariance);Assert.Equal(1,after.PendingCashReviews);
        Assert.Equal(0,(await db.ReadAsync(Window(DateTimeOffset.UtcNow) with{RestaurantId=Guid.NewGuid()},default)).OpenShifts);
        Assert.Equal(0,(await db.ReadAsync(Window(DateTimeOffset.UtcNow) with{BranchId=Guid.NewGuid()},default)).PendingCashReviews);
    }

    [ReportingDatabaseFact]
    public async Task Evidence_hash_never_certifies_a_truncated_row_or_byte_inventory()
    {
        await using var valid=order!.CreateCommand("SELECT n::text FROM generate_series(1,10000) n");
        Assert.Matches("^[a-f0-9]{64}$",(await NexaConnect.Infrastructure.Persistence.BoundedEvidenceHash.ReadAsync(valid,"test-scope",default))!);
        await using var overflow=order.CreateCommand("SELECT n::text FROM generate_series(1,10001) n");
        Assert.Null(await NexaConnect.Infrastructure.Persistence.BoundedEvidenceHash.ReadAsync(overflow,"test-scope",default));
        await using var oversized=order.CreateCommand("SELECT repeat('x',16777217)");
        Assert.Null(await NexaConnect.Infrastructure.Persistence.BoundedEvidenceHash.ReadAsync(oversized,"test-scope",default));
    }

    public async Task InitializeAsync()
    {
        string? input=Environment.GetEnvironmentVariable("NEXACONNECT_REPORTING_INTEGRATION_DB");if(string.IsNullOrWhiteSpace(input))return;
        foreach(string service in new[]{"Order","Payment","POS"})
        {
            string schema="day_"+Guid.NewGuid().ToString("N");
            var source=NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(input){SearchPath=schema+",public"}.ConnectionString);owned.Add((source,schema));
            await Sql(source,"CREATE SCHEMA "+new NpgsqlCommandBuilder().QuoteIdentifier(schema));
            foreach(string dir in Directory.GetDirectories(Path.Combine(Root(),"src/Tools/NexaConnect.DataMigration/Scripts",service)).Order())await Sql(source,await File.ReadAllTextAsync(Path.Combine(dir,"up.sql")));
            if(service=="Order")order=source;else if(service=="Payment")payment=source;else pos=source;
        }
    }
    public async Task DisposeAsync()
    {
        foreach(var (source,schema) in owned)
        {await Sql(source,"DROP SCHEMA IF EXISTS "+new NpgsqlCommandBuilder().QuoteIdentifier(schema)+" CASCADE");await source.DisposeAsync();}
    }
    private static async Task Sql(NpgsqlDataSource source,string sql,params object[] values)
    {await using var command=source.CreateCommand(sql);foreach(var value in values)command.Parameters.AddWithValue(value);await command.ExecuteNonQueryAsync();}
    private static string Root(){var dir=new DirectoryInfo(AppContext.BaseDirectory);while(!File.Exists(Path.Combine(dir.FullName,"NexaConnect.sln")))dir=dir.Parent!;return dir.FullName;}
}
