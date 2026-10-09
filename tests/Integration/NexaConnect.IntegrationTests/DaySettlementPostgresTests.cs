extern alias POS;
extern alias ORDER;
extern alias PAYMENT;
using Microsoft.Extensions.Logging.Abstractions;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Infrastructure.Persistence;
using Npgsql;
using App=POS::NexaConnect.Services.POS.Application.DayClose;
using Domain=POS::NexaConnect.Services.POS.Domain.DayClose;
using Db=POS::NexaConnect.Services.POS.Infrastructure.DayClose;
using PosDb=POS::NexaConnect.Services.POS.Infrastructure.Persistence;
namespace NexaConnect.IntegrationTests;

public sealed partial class DaySealPostgresTests
{
    private string? barrierFailAfter;
    private bool barrierConflict;
    private string? barrierFailPhase;
    private App.IDaySettlementStore Settlements => new Db.PostgresDaySettlementStore(pos);
    private App.DaySettlementRecovery Recovery => new(Settlements,new SettlementSources(this),TimeProvider.System,NullLogger<App.DaySettlementRecovery>.Instance);
    private App.DaySettlement SettlementApplication => new(Settlements,new Db.PostgresFinalizationStore(pos),Finalizations(),Approvals,Recovery,this,this,TimeProvider.System);
    private async Task<Domain.SettlementCommand> PrepareSettlement()
    {
        var command=await FinalizationCommand();
        var view=await Finalizations().PrepareAsync(organization,command,new("manager","token"),default);
        Assert.Equal("prepared",view.Status);
        return new(branch,Date,Guid.NewGuid(),view.Version,command.OperationId,command.ApprovalId,command.ReviewedApprovalVersion);
    }
    private Task<App.SettlementView> FinalizeSettlement(Domain.SettlementCommand command,string actor="manager")=>
        SettlementApplication.FinalizeAsync(organization,command,new(actor,"token"),Guid.NewGuid(),default);
    [ReportingDatabaseFact]
    public async Task Settlement_publication_references_original_seal_revisions_when_later_day_trading_advances_the_acquisition_revision()
    {
        var preparationCommand=await FinalizationCommand();
        var future=ORDER::NexaConnect.Services.Order.Domain.OrderAggregate.Create(Guid.NewGuid(),organization,branch,
            [new ORDER::NexaConnect.Services.Order.Domain.OrderLine(Guid.NewGuid(),"Rice",10,1,"kitchen")],"THB",restaurantId:restaurant);
        await new ORDER::NexaConnect.Services.Order.Infrastructure.Persistence.PostgresOrderRepository(order).SaveAsync(future,default);
        var prepared=await Finalizations().PrepareAsync(organization,preparationCommand,new("manager","token"),default);Assert.Equal("prepared",prepared.Status);
        var command=new Domain.SettlementCommand(branch,Date,Guid.NewGuid(),prepared.Version,preparationCommand.OperationId,preparationCommand.ApprovalId,preparationCommand.ReviewedApprovalVersion);
        var receipt=(await FinalizeSettlement(command)).Settlement!.Receipt!;
        var original=receipt.Snapshot.Seals!.Order;
        Assert.True(receipt.Sources.Single(s=>s.Source=="Order").Revision>original.SourceRevision);
        await using var q=pos.CreateCommand("SELECT payload::text FROM outbox_messages WHERE event_type='pos.branch-day-settled.v1'");
        var publication=System.Text.Json.JsonSerializer.Deserialize<BranchDaySettledV1>((string)(await q.ExecuteScalarAsync())!,new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        var source=publication.Sources.Single(s=>s.Source=="Order");Assert.Equal(original.SealId,source.SealId);
        Assert.Equal(original.RevisionEpoch,source.Epoch);Assert.Equal(original.SourceRevision,source.Revision);
    }
    private sealed class SettlementSources(DaySealPostgresTests owner):App.ISettlementSources
    {
        public async Task<App.SettlementSource[]> ExecuteAsync(App.SettlementState state,string phase,CancellationToken ct)
        {
            var result=new List<App.SettlementSource>();
            foreach(var service in new[]{"Order","Payment","POS"})
            {
                if(owner.barrierConflict&&service=="Payment"&&phase=="armed")throw new Domain.DayCloseConflictException("source_barrier_conflict");
                var request=new SourceBarrierRequest(new(state.Id,state.Command.OperationId,owner.SourceCommand(state.Preparation,service)),phase,phase=="armed"?null:state.DecisionId);
                var proof=await owner.Barrier(service,request,ct);
                if(owner.barrierFailAfter==service&&(owner.barrierFailPhase is null||owner.barrierFailPhase==phase))throw new HttpRequestException("response_lost");
                result.Add(new(service,proof));
            }
            return result.ToArray();
        }
    }
    private Task<SourceBarrierProof> Barrier(string owner,SourceBarrierRequest request,CancellationToken ct=default)=>owner switch
    {
        "Order"=>new ORDER::NexaConnect.Services.Order.Infrastructure.Persistence.PostgresOrderDayBarrierStore(order).ExecuteAsync(request,ct),
        "Payment"=>new PAYMENT::NexaConnect.Services.Payment.Infrastructure.PostgresPaymentDayBarrierStore(payment).ExecuteAsync(request,ct),
        _=>new PosDb.PostgresPOSDayBarrierStore(pos).ExecuteAsync(request,ct)
    };
    private static async Task<long> Count(NpgsqlDataSource db,string table)
    {await using var q=db.CreateCommand("SELECT count(*) FROM "+table);return (long)(await q.ExecuteScalarAsync())!;}

    [ReportingDatabaseFact]
    public async Task Settlement_commit_retains_exact_receipt_audit_and_one_publication_on_duplicate_requests()
    {
        var command=await PrepareSettlement();var first=(await FinalizeSettlement(command)).Settlement!;
        Assert.Equal("finalized",first.Status);Assert.NotNull(first.Receipt);Assert.Equal(first.Preparation.Approval.Snapshot,first.Receipt.Snapshot);
        Assert.Equal(first.Id,(await FinalizeSettlement(command)).Settlement!.Id);
        Assert.Equal(1,await Count(pos,"branch_day_settlement_receipts"));Assert.Equal(1,await Count(pos,"branch_day_settlement_decisions"));
        await using var publication=pos.CreateCommand("SELECT count(*) FROM outbox_messages WHERE event_type='pos.branch-day-settled.v1'");Assert.Equal(1L,await publication.ExecuteScalarAsync());
        Assert.All(first.Sources,s=>Assert.Equal("committed",s.Proof.Phase));
        Assert.Equal("finalized",(await Finalizations().ReadAsync(organization,branch,Date,new("manager","token"),default)).Status);
        await Assert.ThrowsAsync<Domain.DayCloseConflictException>(()=>Finalizations().CancelAsync(organization,new(branch,Date,command.PreparationOperationId),new("manager","token"),default));
        await Assert.ThrowsAsync<Domain.DayCloseConflictException>(()=>FinalizeSettlement(command with{ReviewedApprovalVersion=command.ReviewedApprovalVersion+1}));
        await Assert.ThrowsAsync<Domain.DayCloseConflictException>(()=>FinalizeSettlement(command,"second-manager"));
    }

    [ReportingDatabaseFact] public Task Settlement_restart_after_lost_order_arm_response()=>RestartAfterArm("Order");
    [ReportingDatabaseFact] public Task Settlement_restart_after_lost_payment_arm_response()=>RestartAfterArm("Payment");
    [ReportingDatabaseFact] public Task Settlement_restart_after_lost_pos_arm_response()=>RestartAfterArm("POS");
    private async Task RestartAfterArm(string source)
    {
        var command=await PrepareSettlement();barrierFailAfter=source;
        await Assert.ThrowsAsync<HttpRequestException>(()=>FinalizeSettlement(command));
        var intent=(await Settlements.ReadAsync(Day,default))!;Assert.Equal("arming",intent.Status);Assert.Null(intent.Receipt);
        allowed=false;barrierFailAfter=null;
        var recovered=await Recovery.RecoverAsync(intent,default);Assert.Equal("finalized",recovered.Status);Assert.Equal(intent.Id,recovered.Id);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>FinalizeSettlement(command));
        Assert.Equal(1,await Count(pos,"branch_day_settlement_receipts"));
    }

    [ReportingDatabaseFact] public Task Settlement_restart_after_lost_order_commit_response()=>RestartAfterCommit("Order");
    [ReportingDatabaseFact] public Task Settlement_restart_after_lost_payment_commit_response()=>RestartAfterCommit("Payment");
    [ReportingDatabaseFact] public Task Settlement_restart_after_lost_pos_commit_response()=>RestartAfterCommit("POS");
    private async Task RestartAfterCommit(string owner)
    {
        var command=await PrepareSettlement();barrierFailAfter=owner;barrierFailPhase="committed";
        await Assert.ThrowsAsync<HttpRequestException>(()=>FinalizeSettlement(command));
        var state=(await Settlements.ReadAsync(Day,default))!;Assert.Equal("committing",state.Status);Assert.NotNull(state.Receipt);
        var receipt=System.Text.Json.JsonSerializer.Serialize(state.Receipt);barrierFailAfter=null;
        Assert.Equal("finalized",(await Recovery.RecoverAsync(state,default)).Status);
        Assert.Equal(receipt,System.Text.Json.JsonSerializer.Serialize((await Settlements.ReadAsync(Day,default))!.Receipt));
        Assert.Equal(1,await Count(pos,"branch_day_settlement_receipts"));
    }

    [ReportingDatabaseFact]
    public async Task Commit_receipt_audit_and_outbox_rollback_together_before_durable_decision()
    {
        var command=await PrepareSettlement();barrierFailAfter="POS";
        await Assert.ThrowsAsync<HttpRequestException>(()=>FinalizeSettlement(command));barrierFailAfter=null;
        var state=(await Settlements.ReadAsync(Day,default))!;
        await Sql(pos,"CREATE FUNCTION fail_settlement_receipt() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'injected_settlement_failure'; END; $$; CREATE TRIGGER fail_settlement_receipt BEFORE INSERT ON branch_day_settlement_receipts FOR EACH ROW EXECUTE FUNCTION fail_settlement_receipt()");
        await Assert.ThrowsAsync<PostgresException>(()=>Recovery.RecoverAsync(state,default));
        Assert.Equal(0,await Count(pos,"branch_day_settlement_decisions"));Assert.Equal(0,await Count(pos,"branch_day_settlement_receipts"));
        Assert.Equal("arming",(await Settlements.ReadAsync(Day,default))!.Status);
        await Sql(pos,"DROP TRIGGER fail_settlement_receipt ON branch_day_settlement_receipts;DROP FUNCTION fail_settlement_receipt()");
        Assert.Equal("finalized",(await Recovery.RecoverAsync(state,default)).Status);
    }

    [ReportingDatabaseFact]
    public async Task An_aborted_attempt_remains_replayable_after_fresh_preparation_finalizes_the_day()
    {
        var first=await PrepareSettlement();barrierConflict=true;var aborted=(await FinalizeSettlement(first)).Settlement!;barrierConflict=false;
        await Finalizations().CancelAsync(organization,new(branch,Date,first.PreparationOperationId),new("manager","token"),default);
        var before=await Finalizations().ReadAsync(organization,branch,Date,new("manager","token"),default);
        var prepCommand=aborted.Preparation.Command with{OperationId=Guid.NewGuid(),ExpectedVersion=before.Version};
        var prepared=await Finalizations().PrepareAsync(organization,prepCommand,new("manager","token"),default);
        Assert.Equal("prepared",prepared.Status);
        var second=first with{OperationId=Guid.NewGuid(),PreparationOperationId=prepCommand.OperationId,ExpectedPreparationVersion=prepared.Version};
        Assert.Equal("finalized",(await FinalizeSettlement(second)).Settlement!.Status);
        Assert.Equal("aborted",(await FinalizeSettlement(first)).Settlement!.Status);
        Assert.Equal(second,(await Settlements.ReadAsync(Day,default))!.Command);
        Assert.Equal(2,await Count(pos,"branch_day_settlement_decisions"));Assert.Equal(1,await Count(pos,"branch_day_settlement_receipts"));
    }

    [ReportingDatabaseFact]
    public async Task Settlement_transport_excludes_private_authorization_actor_claim_and_recovery_metadata()
    {
        var command=await PrepareSettlement();var view=await FinalizeSettlement(command);
        using var payload=System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(view,new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
        var progress=payload.RootElement.GetProperty("settlement");
        foreach(var field in new[]{"subject","authorizationDecisionId","traceCorrelationId","preparation"})Assert.False(progress.TryGetProperty(field,out _));
        Assert.Equal(view.Settlement!.Id,progress.GetProperty("id").GetGuid());
    }

    [ReportingDatabaseFact]
    public async Task Settlement_permanent_arm_conflict_aborts_all_sources_without_receipt_or_publication()
    {
        var command=await PrepareSettlement();barrierConflict=true;
        var aborted=(await FinalizeSettlement(command)).Settlement!;
        Assert.Equal("aborted",aborted.Status);Assert.Null(aborted.Receipt);Assert.All(aborted.Sources,s=>Assert.Equal("aborted",s.Proof.Phase));
        Assert.Equal(0,await Count(pos,"branch_day_settlement_receipts"));
        foreach(var service in new[]{"Order","Payment","POS"})
        {
            var commandSource=SourceCommand(aborted.Preparation,service);
            var lease=await new PostgresWindowFence(service=="Order"?order:service=="Payment"?payment:pos).ReadAsync(Window,commandSource.OperationId,default);
            Assert.True(lease!.Cancelled);
            await Assert.ThrowsAsync<SnapshotOperationConflictException>(()=>Barrier(service,new(new(aborted.Id,command.OperationId,commandSource),"armed")));
        }
        Assert.Equal(aborted.Id,(await FinalizeSettlement(command)).Settlement!.Id);
    }

    [ReportingDatabaseFact]
    public async Task Committed_source_cannot_abort_cancel_or_change_its_binding_and_financial_writes_rollback()
    {
        var store=await Store();var drawer=await Drawer(store,Window.FromUtc.AddHours(1),true);
        var command=await PrepareSettlement();var state=(await FinalizeSettlement(command)).Settlement!;
        foreach(var service in new[]{"Order","Payment","POS"})
        {
            var sourceCommand=SourceCommand(state.Preparation,service);
            await Assert.ThrowsAsync<SnapshotOperationConflictException>(()=>Barrier(service,new(new(state.Id,command.OperationId,sourceCommand),"aborted",Guid.NewGuid())));
            var error=await Assert.ThrowsAsync<PostgresException>(()=>Fence(service,sourceCommand,true));Assert.Equal("PDS01",error.SqlState);
        }
        var count=await Count(pos,"cash_movements");
        var blocked=await Assert.ThrowsAsync<PostgresException>(()=>Sql(pos,"INSERT INTO cash_movements(id,cash_session_id,movement_type,amount,occurred_at_utc,recorded_by) VALUES($1,$2,'pay_in',10,now(),'test')",Guid.NewGuid(),drawer));
        Assert.Equal("PDS01",blocked.SqlState);Assert.Equal(count,await Count(pos,"cash_movements"));
        await Drawer(store,Window.ToUtc.AddHours(1),false);
    }

    private async Task<OrderManualTenderSettledV1> LateCash(Guid drawer)
    {
        await using var q=pos.CreateCommand("SELECT s.terminal_id FROM cash_sessions c JOIN shifts s ON s.id=c.shift_id WHERE c.id=$1");q.Parameters.AddWithValue(drawer);
        var terminal=(Guid)(await q.ExecuteScalarAsync())!;
        return new(Guid.NewGuid(),Guid.NewGuid(),Window.FromUtc.AddMinutes(90),organization,restaurant,branch,Guid.NewGuid(),Guid.NewGuid(),terminal,"cash",10,"THB");
    }
    [ReportingDatabaseFact]
    public async Task Committed_late_cash_delivery_is_custodied_once_without_mutating_drawer_or_receipt()
    {
        var drawer=await Drawer(await Store(),Window.FromUtc.AddHours(1),true);var command=await PrepareSettlement();var state=(await FinalizeSettlement(command)).Settlement!;
        var receipt=System.Text.Json.JsonSerializer.Serialize(state.Receipt);
        var message=await LateCash(drawer);var projection=new PosDb.LateAwareOrderSettlementStore(pos);
        Assert.Equal(POS::NexaConnect.Services.POS.Application.OrderSettlements.OrderSettlementProjectionStatus.LateCaptured,await projection.ProjectAsync(message,default));
        await projection.ProjectAsync(message,default);Assert.Equal(1,await Count(pos,"source_late_work"));Assert.Equal(1,await Count(pos,"source_late_work_links"));
        Assert.Equal(0,await Count(pos,"pos_order_settlements"));Assert.Equal(0,await Count(pos,"cash_movements"));
        await Assert.ThrowsAsync<SnapshotOperationConflictException>(()=>projection.ProjectAsync(message with{Amount=11},default));
        await Assert.ThrowsAsync<SnapshotOperationConflictException>(()=>projection.ProjectAsync(message with{BranchId=Guid.NewGuid()},default));
        Assert.Equal(receipt,System.Text.Json.JsonSerializer.Serialize((await Settlements.ReadAsync(Day,default))!.Receipt));
        var sourceProof=await new PosDb.PostgresPOSDayBarrierStore(pos).ReadAsync(Window,state.Id,default);Assert.Equal(1,sourceProof!.LateWorkCount);
    }

    [ReportingDatabaseFact]
    public async Task Armed_late_cash_is_held_until_abort_then_original_delivery_applies()
    {
        var drawer=await Drawer(await Store(),Window.FromUtc.AddHours(1),true);var command=await PrepareSettlement();barrierFailAfter="POS";
        await Assert.ThrowsAsync<HttpRequestException>(()=>FinalizeSettlement(command));
        var message=await LateCash(drawer);var projection=new PosDb.LateAwareOrderSettlementStore(pos);
        var held=await Assert.ThrowsAsync<PostgresException>(()=>projection.ProjectAsync(message,default));Assert.Equal("PDS01",held.SqlState);
        await Assert.ThrowsAsync<LateFinancialWorkHeldException>(()=>projection.ProjectAsync(message,default));
        Assert.Equal(1,await Count(pos,"source_late_work"));Assert.Equal(0,await Count(pos,"pos_order_settlements"));
        var intent=(await Settlements.ReadAsync(Day,default))!;var abort=await Settlements.DecideAsync(intent,false,[],DateTimeOffset.UtcNow,default);
        barrierFailAfter=null;Assert.Equal("aborted",(await Recovery.RecoverAsync(abort,default)).Status);
        Assert.Equal(POS::NexaConnect.Services.POS.Application.OrderSettlements.OrderSettlementProjectionStatus.Applied,await projection.ProjectAsync(message,default));
        Assert.Equal(1,await Count(pos,"pos_order_settlements"));Assert.Equal(1,await Count(pos,"source_late_work"));
    }

    [ReportingDatabaseFact]
    public async Task Webhook_barrier_hold_does_not_exhaust_delivery_and_committed_custody_completes_it()
    {
        var inbox=new PAYMENT::NexaConnect.Services.Payment.Infrastructure.Webhooks.PostgresOmiseWebhookInbox(payment);
        var id="evnt_test_"+Guid.NewGuid().ToString("N");await inbox.EnqueueAsync(id,Guid.NewGuid(),default);
        var first=await inbox.ClaimAsync(TimeSpan.FromMinutes(1),default);Assert.NotNull(first);
        await inbox.FinishAsync(first!,"barrier_held",TimeSpan.Zero,1,default);
        var resumed=await inbox.ClaimAsync(TimeSpan.FromMinutes(1),default);Assert.NotNull(resumed);Assert.Equal(2,resumed!.Attempts);
        await inbox.FinishAsync(resumed,"late_captured",TimeSpan.Zero,1,default);Assert.Null(await inbox.ClaimAsync(TimeSpan.FromMinutes(1),default));
        await using var q=payment.CreateCommand("SELECT status FROM omise_webhook_inbox WHERE event_id=$1");q.Parameters.AddWithValue(id);
        Assert.Equal("completed",await q.ExecuteScalarAsync());
    }

    [ReportingDatabaseFact]
    public async Task Settlement_decision_receipt_audit_and_late_history_reject_rewrites_and_downgrade()
    {
        var command=await PrepareSettlement();await FinalizeSettlement(command);
        foreach(var table in new[]{"branch_day_settlement_receipts","branch_day_settlement_decisions","branch_day_settlement_operations","branch_day_settlement_audit"})
        foreach(var operation in new[]{"DELETE FROM "+table,"TRUNCATE "+table})await Assert.ThrowsAsync<PostgresException>(()=>Sql(pos,operation));
        await Assert.ThrowsAsync<PostgresException>(()=>Sql(pos,File.ReadAllText(Path.Combine(Root(),"src/Tools/NexaConnect.DataMigration/Scripts/POS/0018_day_settlements/down.sql"))));
        foreach(var (service,db,migration) in new[]{("Order",order,"0018_day_barriers"),("Payment",payment,"0017_day_barriers"),("POS",pos,"0017_day_barriers")})
            await Assert.ThrowsAsync<PostgresException>(()=>Sql(db,File.ReadAllText(Path.Combine(Root(),"src/Tools/NexaConnect.DataMigration/Scripts",service,migration,"down.sql"))));
    }
}

