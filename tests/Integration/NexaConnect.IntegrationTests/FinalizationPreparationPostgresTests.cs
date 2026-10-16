extern alias POS;
extern alias ORDER;
extern alias PAYMENT;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Infrastructure.Persistence;
using FApp=POS::NexaConnect.Services.POS.Application.DayClose;
using FDomain=POS::NexaConnect.Services.POS.Domain.DayClose;
using FDb=POS::NexaConnect.Services.POS.Infrastructure.DayClose;
using FOrder=ORDER::NexaConnect.Services.Order.Infrastructure.Persistence;
using FPos=POS::NexaConnect.Services.POS.Infrastructure.Persistence;
using FPayment=PAYMENT::NexaConnect.Services.Payment.Infrastructure;
namespace NexaConnect.IntegrationTests;
public sealed partial class DaySealPostgresTests
{
    private string? fenceFailAfterOwner;
    private Func<Task>? afterAllFences;
    private TaskCompletionSource? fencePaused,fenceContinue;
    private FApp.DayFinalizationPreparation Finalizations(TimeProvider? clock=null)=>new(new FDb.PostgresFinalizationStore(pos),new FDb.HttpFinalizationSources(this),Approvals,this,this,clock??TimeProvider.System,NullLogger<FApp.DayFinalizationPreparation>.Instance);
    private async Task<FDomain.FinalizationCommand> FinalizationCommand()
    {
        var seal=await SealReady();var approval=await Approvals.ApproveAsync(organization,ApprovalCommand(seal.Version),new("manager","token"),default);
        return new(branch,Date,Guid.NewGuid(),0,approval.Decision!.ApprovalId,approval.Version);
    }
    private SourceFenceCommand SourceCommand(FDomain.FinalizationState state,string owner)=>new(state.Command.OperationId,Window,state.Approval.ApprovalId,
        owner=="Order"?state.Approval.Snapshot.Seals!.Order.SealId:owner=="Payment"?state.Approval.Snapshot.Seals!.Payment.SealId:state.Approval.Snapshot.Seals!.Pos.SealId,state.ExpiresAtUtc);
    private Task<SourceDayFence> Fence(string owner,SourceFenceCommand command,bool cancel=false,string actor="manager")=>owner switch
    {
        "Order"=>new FOrder.PostgresOrderDayFenceStore(order).ExecuteAsync(command,actor,cancel,default),
        "Payment"=>new FPayment.PostgresPaymentDayFenceStore(payment).ExecuteAsync(command,actor,cancel,default),
        _=>new FPos.PostgresPosDayFenceStore(pos).ExecuteAsync(command,actor,cancel,default)
    };
    private async Task<HttpResponseMessage> HandleFences(string owner,HttpRequestMessage request,CancellationToken ct)
    {
        SourceDayFence? result;
        if(request.Method==HttpMethod.Post)
        {
            var command=(await request.Content!.ReadFromJsonAsync<SourceFenceCommand>(cancellationToken:ct))!;
            result=await Fence(owner,command,request.RequestUri!.AbsolutePath.EndsWith("/cancel"));
            if(owner=="POS"&&!request.RequestUri.AbsolutePath.EndsWith("/cancel")&&afterAllFences is not null)await afterAllFences();
            if(owner=="Order"&&!request.RequestUri.AbsolutePath.EndsWith("/cancel")&&fencePaused is not null){fencePaused.TrySetResult();await fenceContinue!.Task.WaitAsync(ct);}
            if(owner==fenceFailAfterOwner)return new(HttpStatusCode.ServiceUnavailable);
        }
        else result=await new PostgresWindowFence(owner=="Order"?order:owner=="Payment"?payment:pos).ReadAsync(Window,Guid.Parse(request.RequestUri!.AbsolutePath.Split('/').Last()),ct);
        return result is null?new(HttpStatusCode.NotFound):new(HttpStatusCode.OK){Content=JsonContent.Create(result)};
    }
    [ReportingDatabaseFact]
    public async Task Finalization_preparation_retains_three_fences_and_cancel_releases_them_without_finalizing()
    {
        var command=await FinalizationCommand();var result=await Finalizations().PrepareAsync(organization,command,new("manager","token"),default);
        Assert.Equal("prepared",result.Status);Assert.Equal(3,result.Sources.Length);Assert.NotNull(result.ValidatedAtUtc);
        Assert.Equal("prepared",(await Finalizations().ReadAsync(organization,branch,Date,new("manager","token"),default)).Status);
        Assert.Equal(result.PendingCommand,(await Finalizations().PrepareAsync(organization,command,new("manager","token"),default)).PendingCommand);
        var cancelled=await Finalizations().CancelAsync(organization,new(branch,Date,command.OperationId),new("second-manager","token"),default);Assert.Equal("cancelled",cancelled.Status);Assert.All(cancelled.Sources,x=>Assert.True(x.Cancelled));
        Assert.Equal(3,(await Finalizations().ReadAsync(organization,branch,Date,new("manager","token"),default)).Sources.Length);
        Assert.Equal("cancelled",(await Finalizations().PrepareAsync(organization,command,new("manager","token"),default)).Status);
        foreach(var db in new[]{order,payment,pos})foreach(var sql in new[]{"UPDATE source_day_fence_audit SET actor='changed'","DELETE FROM source_day_fence_audit","TRUNCATE source_day_fence_audit"})await Assert.ThrowsAsync<PostgresException>(()=>Sql(db,sql));
        foreach(var db in new[]{order,payment,pos})foreach(var sql in new[]{"UPDATE source_day_fences SET cancelled=false","UPDATE source_day_fences SET expires_at_utc=expires_at_utc+interval '1 hour'","DELETE FROM source_day_fences","TRUNCATE source_day_fences"})await Assert.ThrowsAsync<PostgresException>(()=>Sql(db,sql));
        foreach(var sql in new[]{"UPDATE branch_day_finalization_audit SET subject_id='changed'","DELETE FROM branch_day_finalization_operations","TRUNCATE branch_day_finalization_audit"})await Assert.ThrowsAsync<PostgresException>(()=>Sql(pos,sql));
    }
    [ReportingDatabaseFact]
    public async Task Source_fences_block_historical_writes_but_allow_next_day_trading_and_rollback_revisions()
    {
        var store=await Store();var historicalDrawer=await Drawer(store,Window.FromUtc.AddHours(1),true);var command=await FinalizationCommand();
        Assert.Equal("prepared",(await Finalizations().PrepareAsync(organization,command,new("manager","token"),default)).Status);
        var aggregate=ORDER::NexaConnect.Services.Order.Domain.OrderAggregate.Create(Guid.NewGuid(),organization,branch,[new ORDER::NexaConnect.Services.Order.Domain.OrderLine(Guid.NewGuid(),"Rice",10,1,"kitchen")],"THB",restaurantId:restaurant);
        await new FOrder.PostgresOrderRepository(order).SaveAsync(aggregate,default);
        var intent=new FPayment.PostgresPaymentIntents(payment,Microsoft.Extensions.Options.Options.Create(new PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.PaymentProviderOptions())).Create(organization,new(restaurant,branch,Guid.NewGuid(),Guid.NewGuid().ToString(),100,"THB","card"),new PAYMENT::NexaConnect.Services.Payment.Application.Intents.PaymentMutationContext("manager",Guid.NewGuid()));
        await Drawer(store,Window.ToUtc.AddHours(1),false);
        foreach(var(db,sql,id)in new[]{(order,"UPDATE orders SET created_at_utc=$1 WHERE id=$2",aggregate.Id),(payment,"UPDATE payment_intents SET created_at_utc=$1 WHERE id=$2",intent.Id)})
        {
            await using var before=db.CreateCommand("SELECT revision FROM source_financial_revisions");var revision=await before.ExecuteScalarAsync();
            var error=await Assert.ThrowsAsync<PostgresException>(()=>Sql(db,sql,Window.FromUtc.AddHours(1),id));Assert.Equal("financial_day_fenced",error.MessageText);Assert.Equal(revision,await before.ExecuteScalarAsync());
        }
        var cashError=await Assert.ThrowsAsync<PostgresException>(()=>Sql(pos,"INSERT INTO cash_movements(id,cash_session_id,movement_type,amount,occurred_at_utc,recorded_by) VALUES($1,$2,'pay_in',10,now(),'manager')",Guid.NewGuid(),historicalDrawer));Assert.Equal("financial_day_fenced",cashError.MessageText);
        await using var terminalQuery=pos.CreateCommand("SELECT s.terminal_id FROM cash_sessions c JOIN shifts s ON s.id=c.shift_id WHERE c.id=$1");terminalQuery.Parameters.AddWithValue(historicalDrawer);
        var terminal=(Guid)(await terminalQuery.ExecuteScalarAsync())!;
        var late=new OrderManualTenderSettledV1(Guid.NewGuid(),Guid.NewGuid(),Window.FromUtc.AddMinutes(90),organization,restaurant,branch,Guid.NewGuid(),Guid.NewGuid(),terminal,"cash",10,"THB");
        var projection=new FPos.PostgresOrderSettlementProjectionStore(pos);
        var deferred=await Assert.ThrowsAsync<PostgresException>(()=>projection.ProjectAsync(late,default));Assert.Equal("financial_day_fenced",deferred.MessageText);
        await using(var ledger=pos.CreateCommand("SELECT count(*) FROM pos_order_settlements"))Assert.Equal(0L,await ledger.ExecuteScalarAsync());
        Assert.Equal("prepared",(await Finalizations().ReadAsync(organization,branch,Date,new("manager","token"),default)).Status);
        await Finalizations().CancelAsync(organization,new(branch,Date,command.OperationId),new("manager","token"),default);
        Assert.Equal(POS::NexaConnect.Services.POS.Application.OrderSettlements.OrderSettlementProjectionStatus.Applied,await projection.ProjectAsync(late,default));
        Assert.Equal(POS::NexaConnect.Services.POS.Application.OrderSettlements.OrderSettlementProjectionStatus.Replayed,await projection.ProjectAsync(late,default));
        await Sql(order,"UPDATE orders SET created_at_utc=$1 WHERE id=$2",Window.FromUtc.AddHours(1),aggregate.Id);
        Assert.Equal("superseded",(await Approvals.ReadAsync(organization,branch,Date,new("manager","token"),default)).Status);
    }
    [ReportingDatabaseFact]
    public async Task Partial_response_loss_resumes_exact_fences_without_extending_expiry_or_duplicating_audit()
    {
        var command=await FinalizationCommand();fenceFailAfterOwner="Order";
        var blocked=await Finalizations().PrepareAsync(organization,command,new("manager","token"),default);Assert.Equal("blocked",blocked.Status);
        Assert.Single((await Finalizations().ReadAsync(organization,branch,Date,new("manager","token"),default)).Sources);
        fenceFailAfterOwner=null;var resumed=await Finalizations().PrepareAsync(organization,command,new("manager","token"),default);Assert.Equal("prepared",resumed.Status);Assert.Equal(blocked.ExpiresAtUtc,resumed.ExpiresAtUtc);
        foreach(var db in new[]{order,payment,pos}){await using var q=db.CreateCommand("SELECT count(*) FROM source_day_fence_audit WHERE action='acquire'");Assert.Equal(1L,await q.ExecuteScalarAsync());}
    }
    [ReportingDatabaseFact]
    public async Task Cancellation_tombstones_prevent_delayed_acquisition_and_stale_completion()
    {
        var command=await FinalizationCommand();fencePaused=new(TaskCreationOptions.RunContinuationsAsynchronously);fenceContinue=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var preparing=Finalizations().PrepareAsync(organization,command,new("manager","token"),default);
        var arrival=await Task.WhenAny(fencePaused.Task,preparing,Task.Delay(10000));
        if(arrival==preparing)Assert.Fail("Preparation completed before source barrier: "+(await preparing).Status);
        await fencePaused.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("cancelled",(await Finalizations().CancelAsync(organization,new(branch,Date,command.OperationId),new("second-manager","token"),default)).Status);
        fenceContinue.TrySetResult();await Assert.ThrowsAsync<FDomain.DayCloseConflictException>(()=>preparing);
        var current=await new FDb.PostgresFinalizationStore(pos).ReadAsync(Day,default);Assert.Equal("cancelled",current!.Status);
        foreach(var owner in new[]{"Order","Payment","POS"})Assert.False((await Fence(owner,SourceCommand(current,owner))).Active);
    }
    [ReportingDatabaseFact]
    public async Task Expired_source_lease_releases_writes_and_cannot_be_reactivated_by_replay()
    {
        var command=await FinalizationCommand();var approval=await Approvals.ReadAsync(organization,branch,Date,new("manager","token"),default);
        var sourceCommand=new SourceFenceCommand(command.OperationId,Window,command.ApprovalId,approval.Decision!.Snapshot.Seals!.Order.SealId,DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.True((await Fence("Order",sourceCommand)).Active);await Task.Delay(1100);Assert.False((await Fence("Order",sourceCommand)).Active);
        await Sql(order,"INSERT INTO orders(id,organization_id,restaurant_id,branch_id,status,total_amount,currency,created_at_utc,updated_at_utc,concurrency_version,channel,service_type,order_number,subtotal_amount,created_by,updated_by) VALUES($1,$2,$3,$4,'draft',0,'THB',$5,now(),1,'pos','dine_in','fence-test',0,'manager','manager')",Guid.NewGuid(),organization,restaurant,branch,Window.FromUtc.AddHours(1));
    }
    [ReportingDatabaseFact]
    public async Task Repeatable_read_writer_with_pre_fence_snapshot_cannot_bypass_a_new_fence()
    {
        var command=await FinalizationCommand();var approval=await Approvals.ReadAsync(organization,branch,Date,new("manager","token"),default);
        await using var c=await order.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        await using(var read=new NpgsqlCommand("SELECT count(*) FROM source_financial_revisions",c,tx))await read.ExecuteScalarAsync();
        await Fence("Order",new(command.OperationId,Window,command.ApprovalId,approval.Decision!.Snapshot.Seals!.Order.SealId,DateTimeOffset.UtcNow.AddMinutes(2)));
        await using var writer=new NpgsqlCommand("INSERT INTO orders(id,organization_id,restaurant_id,branch_id,status,total_amount,currency,created_at_utc,updated_at_utc,concurrency_version,channel,service_type,order_number,subtotal_amount,created_by,updated_by) VALUES($1,$2,$3,$4,'draft',0,'THB',$5,now(),1,'pos','dine_in','fence-test',0,'manager','manager')",c,tx);
        foreach(var value in new object[]{Guid.NewGuid(),organization,restaurant,branch,Window.FromUtc.AddHours(1)})writer.Parameters.AddWithValue(value);
        var error=await Assert.ThrowsAsync<PostgresException>(()=>writer.ExecuteNonQueryAsync());Assert.Equal("40001",error.SqlState);
    }
    [ReportingDatabaseFact]
    public async Task Concurrent_managers_changed_replay_and_revoked_permission_cannot_prepare_a_second_operation()
    {
        var command=await FinalizationCommand();
        async Task<FApp.FinalizationView?> Attempt(string subject,Guid operation){try{return await Finalizations().PrepareAsync(organization,command with{OperationId=operation},new(subject,"token"),default);}catch(FDomain.DayCloseConflictException){return null;}}
        var results=await Task.WhenAll(Attempt("manager",command.OperationId),Attempt("second-manager",Guid.NewGuid()));Assert.Single(results,x=>x?.Status=="prepared");
        var current=await new FDb.PostgresFinalizationStore(pos).ReadAsync(Day,default);
        await Assert.ThrowsAsync<FDomain.DayCloseConflictException>(()=>Finalizations().PrepareAsync(organization,current!.Command with{ReviewedApprovalVersion=2},new(current.Subject,"token"),default));
        allowed=false;await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Finalizations().PrepareAsync(organization,current!.Command,new(current.Subject,"token"),default));
    }
    [ReportingDatabaseFact]
    public async Task Dependency_outage_clears_prepared_status_and_same_operation_can_restore_proof()
    {
        var command=await FinalizationCommand();var first=await Finalizations().PrepareAsync(organization,command,new("manager","token"),default);Assert.Equal("prepared",first.Status);
        fail=true;var unknown=await Finalizations().ReadAsync(organization,branch,Date,new("manager","token"),default);Assert.Equal("blocked",unknown.Status);Assert.Null(unknown.ValidatedAtUtc);
        fail=false;var restored=await Finalizations().PrepareAsync(organization,command,new("manager","token"),default);Assert.Equal("prepared",restored.Status);Assert.Equal(first.ExpiresAtUtc,restored.ExpiresAtUtc);
    }
    [ReportingDatabaseFact]
    public async Task Restart_after_partial_acquisition_resumes_expired_claim_and_preserves_original_expiry()
    {
        var command=await FinalizationCommand();var approval=await Approvals.ReadAsync(organization,branch,Date,new("manager","token"),default);
        var now=DateTimeOffset.UtcNow;var persisted=await new FDb.PostgresFinalizationStore(pos).BeginAsync(Day,command,approval,new("manager",Guid.NewGuid()),now,default);
        await Fence("Order",SourceCommand(persisted,"Order"));
        await Assert.ThrowsAsync<FDomain.DayCloseConflictException>(()=>Finalizations(new FenceClock(now.AddSeconds(29))).PrepareAsync(organization,command,new("manager","token"),default));
        var recovered=await Finalizations(new FenceClock(now.AddSeconds(31))).PrepareAsync(organization,command,new("manager","token"),default);
        Assert.Equal("prepared",recovered.Status);Assert.Equal(persisted.ExpiresAtUtc,recovered.ExpiresAtUtc);
        await Assert.ThrowsAsync<FDomain.DayCloseConflictException>(()=>new FDb.PostgresFinalizationStore(pos).CompleteAsync(persisted,approval,[],new("manager",Guid.NewGuid()),now.AddSeconds(32),false,default));
    }
    [ReportingDatabaseFact]
    public async Task Permission_revocation_after_acquisition_never_records_prepared_success()
    {
        var command=await FinalizationCommand();afterAllFences=()=>{allowed=false;return Task.CompletedTask;};
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Finalizations().PrepareAsync(organization,command,new("manager","token"),default));
        var state=await new FDb.PostgresFinalizationStore(pos).ReadAsync(Day,default);Assert.Equal("blocked",state!.Status);Assert.Null(state.ValidatedAtUtc);
    }
    [ReportingDatabaseFact]
    public async Task Concurrent_seal_replacement_during_acquisition_blocks_preparation_and_preserves_the_bound_approval()
    {
        var command=await FinalizationCommand();var original=await Approvals.ReadAsync(organization,branch,Date,new("manager","token"),default);
        afterAllFences=async()=>{var reviewed=await Workflow.PrepareAsync(organization,Command(2),new("manager","token"),default);await Sealing.SealAsync(organization,SealCommand(reviewed.Version,original.Decision!.SealVersion),new("manager","token"),default);};
        var result=await Finalizations().PrepareAsync(organization,command,new("manager","token"),default);Assert.Equal("blocked",result.Status);Assert.Equal(command.ApprovalId,result.ApprovalId);
        Assert.Equal("superseded",(await Approvals.ReadAsync(organization,branch,Date,new("manager","token"),default)).Status);
    }
    [ReportingDatabaseFact]
    public async Task Prepared_validity_expires_and_fence_history_refuses_downgrade()
    {
        var command=await FinalizationCommand();var result=await Finalizations().PrepareAsync(organization,command,new("manager","token"),default);
        var expired=await Finalizations(new FenceClock(result.ExpiresAtUtc!.Value)).ReadAsync(organization,branch,Date,new("manager","token"),default);Assert.Equal("expired",expired.Status);Assert.Null(expired.ValidatedAtUtc);
        foreach(var(owner,db,version)in new[]{("Order",order,"0017_day_fences"),("Payment",payment,"0016_day_fences"),("POS",pos,"0015_day_fences")})
            await Assert.ThrowsAsync<PostgresException>(()=>Sql(db,File.ReadAllText(Path.Combine(Root(),"src/Tools/NexaConnect.DataMigration/Scripts",owner,version,"down.sql"))));
        await Assert.ThrowsAsync<PostgresException>(()=>Sql(pos,File.ReadAllText(Path.Combine(Root(),"src/Tools/NexaConnect.DataMigration/Scripts/POS/0016_finalization_preparations/down.sql"))));
    }
    private sealed class FenceClock(DateTimeOffset now):TimeProvider{public override DateTimeOffset GetUtcNow()=>now;}
    [ReportingDatabaseFact]
    public async Task Database_write_backstops_match_each_owning_domain_financial_selection()
    {
        var old=Window.FromUtc.AddDays(-1);var next=Window.ToUtc.AddHours(1);var inside=Window.FromUtc.AddHours(1);var id=Guid.NewGuid();
        async Task Match(NpgsqlDataSource db,string kind,object? before,object? after,bool uncertain,bool allowed)
        {
            var json=System.Text.Json.JsonSerializer.Serialize(new{version=2,kind,recordId=id,before,after,ownershipUncertain=uncertain},new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            await using var q=db.CreateCommand("SELECT source_change_blocks_fence($1::jsonb,$2,$3)");q.Parameters.AddWithValue(json);q.Parameters.AddWithValue(Window.FromUtc);q.Parameters.AddWithValue(Window.ToUtc);
            Assert.Equal(!allowed,await q.ExecuteScalarAsync());
        }
        foreach(var(status,created,paid,receipt)in new[]{("draft",old,(DateTimeOffset?)null,false),("draft",next,(DateTimeOffset?)null,false),("cancelled",old,(DateTimeOffset?)null,false),("completed",old,(DateTimeOffset?)inside,true),("completed",old,(DateTimeOffset?)next,true),("completed",inside,(DateTimeOffset?)next,true),("completed",inside,(DateTimeOffset?)null,false)})
        {
            var record=new ORDER::NexaConnect.Services.Order.Domain.FinancialRecord(id,status,1,created,paid,receipt);
            await Match(order,"orders",null,record,false,ORDER::NexaConnect.Services.Order.Domain.FinancialDayFence.AllowsMutation(Window.FromUtc,Window.ToUtc,null,record,false,"orders"));
        }
        foreach(var(kind,status,created,completed,receipt)in new[]{("payment_intents","captured",old,(DateTimeOffset?)null,false),("payment_intents","pending",old,(DateTimeOffset?)null,false),("payment_intents","pending",next,(DateTimeOffset?)null,false),("refunds","failed",old,(DateTimeOffset?)null,false),("refunds","processing",old,(DateTimeOffset?)null,false),("refunds","completed",old,(DateTimeOffset?)inside,true),("refunds","completed",old,(DateTimeOffset?)next,true),("refunds","completed",old,(DateTimeOffset?)null,false)})
        {
            var record=new PAYMENT::NexaConnect.Services.Payment.Domain.FinancialRecord(id,status,1,created,completed,receipt);
            await Match(payment,kind,null,record,false,PAYMENT::NexaConnect.Services.Payment.Domain.FinancialDayFence.AllowsMutation(Window.FromUtc,Window.ToUtc,null,record,false,kind));
        }
        foreach(var(kind,status,opened,closed,variance,review,reviewed)in new[]{("shifts","open",old,(DateTimeOffset?)null,(bool?)null,(string?)null,(long?)null),("shifts","open",next,(DateTimeOffset?)null,(bool?)null,(string?)null,(long?)null),("shifts","closed",old,(DateTimeOffset?)inside,(bool?)null,(string?)null,(long?)null),("cash_sessions","closed",old,(DateTimeOffset?)inside,(bool?)false,(string?)null,(long?)null),("cash_sessions","closed",old,(DateTimeOffset?)old,(bool?)false,(string?)null,(long?)null),("cash_sessions","closed",old,(DateTimeOffset?)old,(bool?)true,(string?)"approved",(long?)1),("cash_sessions","closed",old,(DateTimeOffset?)old,(bool?)true,(string?)"approved",(long?)0)})
        {
            var record=new POS::NexaConnect.Services.POS.Domain.FinancialRecord(id,status,1,opened,closed,variance,review,reviewed);
            await Match(pos,kind,null,record,false,POS::NexaConnect.Services.POS.Domain.FinancialDayFence.AllowsMutation(Window.FromUtc,Window.ToUtc,null,record,false,kind));
        }
        foreach(var db in new[]{order,payment,pos})await Match(db,"unknown",null,null,true,false);
    }
}
