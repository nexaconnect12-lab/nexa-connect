extern alias ORDER;
extern alias PAYMENT;
extern alias POS;
extern alias REPORTING;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Contracts.Reporting;
using Npgsql;
using PDb=POS::NexaConnect.Services.POS.Infrastructure.Persistence;
using RDb=REPORTING::NexaConnect.Services.Reporting.Infrastructure.Persistence;
using RApp=REPORTING::NexaConnect.Services.Reporting.Application;

namespace NexaConnect.IntegrationTests;

public sealed partial class DaySealPostgresTests
{
    private async Task<PosManualRefundCompletedV1> SeedManualCompletion(DateTimeOffset at)
    {
        var store=await Store();var drawer=await Drawer(store,at.AddMinutes(-20),true);
        var orderId=Guid.NewGuid();var settlementId=Guid.NewGuid();var operationId=Guid.NewGuid();
        await Sql(pos,"INSERT INTO manual_refund_sales VALUES($1,$2,$3,$4,$5,'promptpay_manual','THB',100,$6)",orderId,settlementId,organization,restaurant,branch,at.AddDays(-2));
        await Sql(pos,"INSERT INTO manual_refund_approvals VALUES($1,$2,25,'customer_request','approver',$3,'manager',$4)",operationId,orderId,Guid.NewGuid(),at.AddMinutes(-2));
        await Sql(pos,"INSERT INTO manual_refund_payouts SELECT $1,s.terminal_id,s.id,NULL,$3 FROM shifts s JOIN cash_sessions c ON c.shift_id=s.id WHERE c.id=$2",operationId,drawer,at.AddMinutes(-1));
        await Sql(pos,"INSERT INTO manual_refund_completions VALUES($1,NULL,'synthetic-protected-proof',$2)",operationId,at);
        return new(Guid.NewGuid(),Guid.NewGuid(),at,organization,restaurant,branch,orderId,settlementId,operationId,"promptpay_manual",25,"THB",null);
    }

    private async Task<CutoffReconciliationCommand> ManualCutoff()
    {
        var command=new SourceCutoffCommand(Guid.NewGuid(),Window);
        var o=await new ORDER::NexaConnect.Services.Order.Infrastructure.Persistence.PostgresOrderCutoffStore(order).CaptureAsync(command,"manager",default);
        var p=await new PAYMENT::NexaConnect.Services.Payment.Infrastructure.PostgresPaymentCutoffStore(payment).CaptureAsync(command,"manager",default);
        var s=await new PDb.PostgresPosCutoffStore(pos).CaptureAsync(command,"manager",default);
        Assert.Equal(4,s.EvidenceProtocolVersion);
        return new(Window,o.ManifestId,p.ManifestId,s.ManifestId);
    }

    [ReportingDatabaseFact]
    public async Task Settlement_reporting_manual_refund_delivery_is_required_and_finalized_proof_survives_retries_and_export()
    {
        var refund=await SeedManualCompletion(Window.FromUtc.AddHours(1));
        var command=await ManualCutoff();
        var missing=await Checker.CheckAsync(command,"Bearer token",default);
        Assert.False(missing.DeliveryComplete);Assert.False(missing.ManualRefunds!.Matched);
        var facts=new RDb.PostgresManualRefundFinancialFacts(reporting);
        Assert.True(await facts.ProjectAsync(refund,default));Assert.False(await facts.ProjectAsync(refund,default));
        Assert.True((await Checker.CheckAsync(command,"Bearer token",default)).DeliveryComplete);
        var settlementCommand=await PrepareSettlement();var first=await FinalizeSettlement(settlementCommand);
        Assert.Equal("finalized",first.Settlement!.Status);
        Assert.Equal(first.Settlement.Id,(await FinalizeSettlement(settlementCommand)).Settlement!.Id);
        var publication=Assert.Single((await SettlementManifest()).Events).V3!;
        Assert.Equal(25,publication.ManualRefunds.PromptPayAmount);Assert.Equal(0,publication.ManualRefunds.CashAmount);
        Assert.Equal(-25,publication.Settlement.Settlement.NetSales);
        var repository=new RDb.PostgresSettlementFactRepository(reporting);
        var manifest=await SettlementManifest();var fact=RApp.SettlementReporting.Translate(manifest.Events.Single());
        Assert.True(await repository.ProjectAsync(fact,default));Assert.False(await repository.ProjectAsync(fact,default));
        var report=await repository.CompareAsync(manifest,default);Assert.Equal("matched",report.Status);
        var csv=System.Text.Encoding.UTF8.GetString(RApp.SettlementCsv.Render(report,SettlementRange));
        Assert.Contains("manual_promptpay_refunds",csv);Assert.Contains(publication.ManualRefunds.EvidenceVersion!,csv);
        // New backdated custody cannot bypass an armed/committed source barrier.
        var laterOperation=Guid.NewGuid();
        await Sql(pos,"INSERT INTO manual_refund_approvals SELECT $1,order_id,10,reason,manager_subject,gen_random_uuid(),cashier_subject,approved_at_utc FROM manual_refund_approvals LIMIT 1",laterOperation);
        var error=await Assert.ThrowsAsync<PostgresException>(()=>Sql(pos,
            "INSERT INTO manual_refund_payouts SELECT $1,terminal_id,shift_id,cash_session_id,started_at_utc FROM manual_refund_payouts LIMIT 1",laterOperation));
        Assert.Equal("PDS01",error.SqlState);
        // The same original sale may be refunded on a later day without changing its finalized day.
        var later=DateTimeOffset.UtcNow;
        await Sql(pos,"INSERT INTO manual_refund_payouts SELECT $1,terminal_id,shift_id,cash_session_id,$2 FROM manual_refund_payouts LIMIT 1",laterOperation,later);
        await Sql(pos,"INSERT INTO manual_refund_completions VALUES($1,NULL,'synthetic-later-proof',$2)",laterOperation,later.AddSeconds(1));
        var sealedPos=first.Settlement.Receipt!.Snapshot.Seals!.Pos;
        var observation=await new PDb.PostgresPosCutoffStore(pos).ReadSealAsync(Window,sealedPos.SealId,default);
        Assert.Equal(0,observation!.PendingChanges);Assert.Equal(0,observation.UnknownChanges);
    }

    [ReportingDatabaseFact]
    public async Task Settlement_reporting_manual_refund_same_amount_different_operation_is_not_delivery_proof()
    {
        var refund=await SeedManualCompletion(Window.FromUtc.AddHours(1));
        var command=await ManualCutoff();
        await new RDb.PostgresManualRefundFinancialFacts(reporting).ProjectAsync(refund with{OperationId=Guid.NewGuid()},default);
        var check=await Checker.CheckAsync(command,"Bearer token",default);
        Assert.False(check.DeliveryComplete);Assert.False(check.ManualRefunds!.Matched);Assert.Equal("gaps_detected",check.Status);
        Assert.Equal(25,check.ManualRefunds.PromptPayAmount);
    }
}
