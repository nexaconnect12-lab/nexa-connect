extern alias ORDER;
extern alias PAYMENT;
extern alias POS;
using NexaConnect.Contracts.Reporting;
using OrderDb=ORDER::NexaConnect.Services.Order.Infrastructure.Persistence;
using Orders=ORDER::NexaConnect.Services.Order.Domain;
using PaymentDb=PAYMENT::NexaConnect.Services.Payment.Infrastructure;
using Providers=PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers;
using Payments=PAYMENT::NexaConnect.Services.Payment.Application.Intents;
using PosDb=POS::NexaConnect.Services.POS.Infrastructure.Persistence;
namespace NexaConnect.IntegrationTests;

public sealed partial class DaySealPostgresTests
{
    private async Task<Guid> CompletedOrder(DateTimeOffset created,DateTimeOffset paid)
    {
        var aggregate=Orders.OrderAggregate.Create(Guid.NewGuid(),organization,branch,[new Orders.OrderLine(Guid.NewGuid(),"Rice",100,1,"kitchen")],"THB",restaurantId:restaurant,workflowPaymentMethod:"card");
        aggregate.Submit();aggregate.MarkInventoryReserved();aggregate.MarkKitchenAccepted();var db=new OrderDb.PostgresOrderRepository(order);
        await db.SaveAsync(aggregate,default);await Sql(order,"UPDATE orders SET created_at_utc=$1 WHERE id=$2",created,aggregate.Id);
        aggregate.MarkPaid(Guid.NewGuid());aggregate.IssueReceipt(paid,"card");await db.SaveAsync(aggregate,default);return aggregate.Id;
    }
    private async Task<SourceDaySeal> SealOrder(EndOfDayWindow window)
    {
        var db=new OrderDb.PostgresOrderCutoffStore(order);var manifest=await db.CaptureAsync(new(Guid.NewGuid(),window),"manager",default);
        return await db.SealAsync(new(Guid.NewGuid(),window,manifest.ManifestId,manifest.SourceRevision!),"manager",default);
    }
    private async Task<SourceDaySeal> SealPayment(EndOfDayWindow window)
    {
        var db=new PaymentDb.PostgresPaymentCutoffStore(payment);var manifest=await db.CaptureAsync(new(Guid.NewGuid(),window),"manager",default);
        return await db.SealAsync(new(Guid.NewGuid(),window,manifest.ManifestId,manifest.SourceRevision!),"manager",default);
    }
    [ReportingDatabaseFact]
    public async Task Completed_sale_metadata_changes_affect_creation_and_tender_days_but_not_other_historical_days()
    {
        var saleWindow=Window with{FromUtc=Window.FromUtc.AddDays(-2),ToUtc=Window.ToUtc.AddDays(-2)};
        var tenderWindow=Window with{FromUtc=Window.FromUtc.AddDays(-1),ToUtc=Window.ToUtc.AddDays(-1)};
        var id=await CompletedOrder(saleWindow.FromUtc.AddHours(1),tenderWindow.FromUtc.AddHours(1));
        var sale=await SealOrder(saleWindow);var tender=await SealOrder(tenderWindow);var unrelated=await SealOrder(Window);
        await Sql(order,"UPDATE orders SET updated_at_utc=updated_at_utc+interval '1 second' WHERE id=$1",id);
        var source=new OrderDb.PostgresOrderCutoffStore(order);
        var a=(await source.ReadSealAsync(saleWindow,sale.SealId,default))!;
        var b=(await source.ReadSealAsync(tenderWindow,tender.SealId,default))!;
        var c=(await source.ReadSealAsync(Window,unrelated.SealId,default))!;
        Assert.Equal("sales_date",Assert.Single(a.Changes).Attribution);Assert.Equal("tender_date",Assert.Single(b.Changes).Attribution);
        Assert.Equal(id,a.Changes[0].RecordId);Assert.Equal(id,a.Changes[0].ParentId);Assert.Equal("completed",a.Changes[0].BeforeStatus);
        Assert.Equal(0,c.PendingChanges);Assert.Equal(1,c.ObservedChanges);Assert.True(c.JournalComplete);
        await Sql(order,"UPDATE orders SET updated_at_utc=updated_at_utc-interval '1 second' WHERE id=$1",id);
        Assert.Equal(2,(await source.ReadSealAsync(saleWindow,sale.SealId,default))!.PendingChanges);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>Sql(order,System.IO.File.ReadAllText(System.IO.Path.Combine(Root(),"src/Tools/NexaConnect.DataMigration/Scripts/Order/0016_exact_day_attribution/down.sql"))));
    }
    [ReportingDatabaseFact]
    public async Task Moving_a_completed_sale_out_of_a_day_retains_its_before_state_and_unrelated_seal_is_unchanged()
    {
        var prior=Window with{FromUtc=Window.FromUtc.AddDays(-2),ToUtc=Window.ToUtc.AddDays(-2)};
        var id=await CompletedOrder(Window.FromUtc.AddHours(1),prior.FromUtc.AddHours(1));
        var seal=await SealOrder(Window);
        await Sql(order,"UPDATE orders SET created_at_utc=$1 WHERE id=$2",prior.FromUtc,id);
        var read=(await new OrderDb.PostgresOrderCutoffStore(order).ReadSealAsync(Window,seal.SealId,default))!;
        Assert.Equal(1,read.PendingChanges);Assert.Equal("sales_date",Assert.Single(read.Changes).Attribution);Assert.Equal(seal.ManifestId,read.Manifest.ManifestId);
    }
    [ReportingDatabaseFact]
    public async Task Completed_refund_history_is_immutable_and_publication_captures_original_parent_completion()
    {
        var options=Microsoft.Extensions.Options.Options.Create(new Providers.PaymentProviderOptions());
        var historical=new FixedFinancialClock(Window.FromUtc.AddDays(-2));
        var intents=new PaymentDb.PostgresPaymentIntents(payment,options,historical);var actor=new Payments.PaymentMutationContext("acceptance",Guid.NewGuid());
        var intent=intents.Create(organization,new(restaurant,branch,Guid.NewGuid(),Guid.NewGuid().ToString(),100,"THB","card"),actor);
        var auth=intents.BeginAuthorization(organization,intent.Id,actor);intents.CompleteAuthorization(organization,intent.Id,auth.Intent.ConcurrencyVersion,Providers.ProviderAuthorizationOutcome.Authorized,"charge",null,actor);
        var capture=intents.BeginCapture(organization,intent.Id,actor);intents.CompleteCapture(organization,intent.Id,capture.Intent.ConcurrencyVersion,Providers.ProviderCaptureOutcome.Captured,"capture",null,actor);
        var refunds=new PaymentDb.PostgresPaymentRefunds(payment,options,historical);
        var lease=refunds.Begin(organization,intent.Id,new(Guid.NewGuid(),25,"THB","customer_request",Guid.NewGuid()),actor);
        var completed=refunds.Complete(organization,lease.Refund.Id,lease.Refund.ConcurrencyVersion,Providers.ProviderRefundOutcome.Refunded,"refund",null,actor);
        var financialWindow=Window with{FromUtc=Window.FromUtc.AddDays(-2),ToUtc=Window.ToUtc.AddDays(-2)};
        var related=await SealPayment(financialWindow);var unrelated=await SealPayment(Window);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>Sql(payment,"UPDATE refunds SET concurrency_version=concurrency_version+1 WHERE id=$1",completed.Id));
        var db=new PaymentDb.PostgresPaymentCutoffStore(payment);
        Assert.Equal(0,(await db.ReadSealAsync(financialWindow,related.SealId,default))!.PendingChanges);
        Assert.Equal(0,(await db.ReadSealAsync(Window,unrelated.SealId,default))!.PendingChanges);
        await using var query=payment.CreateCommand("SELECT attribution::text FROM source_financial_changes WHERE attribution->>'kind'='refund_financial_publications' ORDER BY revision DESC LIMIT 1");
        using var json=System.Text.Json.JsonDocument.Parse((string)(await query.ExecuteScalarAsync())!);
        var state=json.RootElement.GetProperty("after");Assert.Equal(completed.Id,state.GetProperty("id").GetGuid());
        Assert.Equal(completed.CompletedAtUtc,state.GetProperty("financialAtUtc").GetDateTimeOffset());
        Assert.NotEqual(Guid.Empty,json.RootElement.GetProperty("recordId").GetGuid());
    }
    [ReportingDatabaseFact]
    public async Task Historical_closed_drawer_review_changes_use_before_and_after_financial_versions()
    {
        var storeId=await Store();var old=Window.FromUtc.AddDays(-2);var drawer=await Drawer(storeId,old,true);
        await Sql(pos,"UPDATE cash_sessions SET actual_closing_amount=10 WHERE id=$1",drawer);
        await Sql(pos,"INSERT INTO cash_session_review_states(cash_session_id,reviewed_session_version,status,reviewed_by,authorization_decision_id,reviewed_at_utc) VALUES($1,1,'approved','manager',$2,now())",drawer,Guid.NewGuid());
        var db=new PosDb.PostgresPosCutoffStore(pos);var manifest=await db.CaptureAsync(new(Guid.NewGuid(),Window),"manager",default);
        var seal=await db.SealAsync(new(Guid.NewGuid(),Window,manifest.ManifestId,manifest.SourceRevision!),"manager",default);
        await Sql(pos,"UPDATE cash_session_review_states SET concurrency_version=concurrency_version+1 WHERE cash_session_id=$1",drawer);
        Assert.Equal(0,(await db.ReadSealAsync(Window,seal.SealId,default))!.PendingChanges);
        await Sql(pos,"UPDATE cash_sessions SET concurrency_version=concurrency_version+1 WHERE id=$1",drawer);
        var read=(await db.ReadSealAsync(Window,seal.SealId,default))!;
        Assert.Equal("cash_review",Assert.Single(read.Changes).Attribution);Assert.Equal(1,read.Changes[0].BeforeFinancialVersion);Assert.Equal(2,read.Changes[0].AfterFinancialVersion);
        await Sql(pos,"UPDATE cash_session_review_states SET reviewed_session_version=2 WHERE cash_session_id=$1",drawer);
        read=(await db.ReadSealAsync(Window,seal.SealId,default))!;Assert.Equal(2,read.PendingChanges);Assert.Equal(0,read.UnknownChanges);
        Assert.Equal("cash_review",read.Changes[1].Attribution);Assert.Equal(drawer,read.Changes[1].RecordId);Assert.Equal("cash_session_review_states",read.Changes[1].RecordKind);
    }
    [ReportingDatabaseFact]
    public async Task Timestamp_only_journal_metadata_stays_unknown_after_exact_upgrade()
    {
        var reviewed=await Reviewed();var sealedDay=await Sealing.SealAsync(organization,SealCommand(reviewed.Version),new("manager","token"),default);
        var migration=System.IO.Path.Combine(Root(),"src/Tools/NexaConnect.DataMigration/Scripts/Order/0016_exact_day_attribution");
        await Sql(order,System.IO.File.ReadAllText(System.IO.Path.Combine(migration,"down.sql")));
        var aggregate=Orders.OrderAggregate.Create(Guid.NewGuid(),organization,branch,[new Orders.OrderLine(Guid.NewGuid(),"Rice",10,1,"kitchen")],"THB",restaurantId:restaurant);
        await new OrderDb.PostgresOrderRepository(order).SaveAsync(aggregate,default);
        await Sql(order,System.IO.File.ReadAllText(System.IO.Path.Combine(migration,"up.sql")));
        var read=await OrderSealAsync(Window,sealedDay.Snapshot!.Seals!.Order.SealId,"token",default);
        Assert.Equal(1,read.UnknownChanges);Assert.Equal("legacy_attribution",Assert.Single(read.Changes).Attribution);
    }
    private sealed class FixedFinancialClock(DateTimeOffset now):TimeProvider{public override DateTimeOffset GetUtcNow()=>now;}
}
