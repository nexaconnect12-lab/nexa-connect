extern alias POS;
extern alias ORDER;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PosApp=POS::NexaConnect.Services.POS.Application.DayClose;
using PosDomain=POS::NexaConnect.Services.POS.Domain.DayClose;
using PosAuth=POS::NexaConnect.Services.POS.Application.Shifts;
using ApprovalDb=POS::NexaConnect.Services.POS.Infrastructure.DayClose;
using Orders=ORDER::NexaConnect.Services.Order.Domain;
using OrderDb=ORDER::NexaConnect.Services.Order.Infrastructure.Persistence;
namespace NexaConnect.IntegrationTests;

public sealed partial class DaySealPostgresTests
{
    private PosApp.IApprovalSealEvidenceReader ApprovalEvidence=>new ApprovalDb.HttpDaySealEvidenceReader(this,new ApprovalDb.PostgresDaySealStore(pos),this,sealClock);
    private PosApp.DayCloseApproval Approvals=>new(new ApprovalDb.PostgresDayApprovalStore(pos),new ApprovalDb.PostgresDaySealStore(pos),ApprovalEvidence,
        this,this,sealClock,NullLogger<PosApp.DayCloseApproval>.Instance);
    private async Task<PosApp.PreparationView> SealReady(){var reviewed=await Reviewed();return await Sealing.SealAsync(organization,SealCommand(reviewed.Version),new("manager","token"),default);}
    private PosDomain.ApprovalCommand ApprovalCommand(long sealedVersion,long approvalVersion=0)=>new(branch,Date,Guid.NewGuid(),approvalVersion,sealedVersion,"review_complete");
    [ReportingDatabaseFact]
    public async Task Approval_exact_replay_restart_actor_conflict_and_immutable_ledger_are_durable()
    {
        var sealedDay=await SealReady();var command=ApprovalCommand(sealedDay.Version);
        var result=await Approvals.ApproveAsync(organization,command,new("manager","token"),default);
        Assert.Equal("approved",result.Status);Assert.Equal(1,result.Version);Assert.NotNull(result.ValidatedAtUtc);Assert.Equal(sealedDay.Snapshot!.Seals,result.Decision!.Snapshot.Seals);
        var replay=await Approvals.ApproveAsync(organization,command,new("manager","token"),default);Assert.Equal(result.Decision.ApprovalId,replay.OperationDecision!.ApprovalId);Assert.Equal(result.Version,replay.Version);
        await Assert.ThrowsAsync<PosDomain.DayCloseConflictException>(()=>Approvals.ApproveAsync(organization,command,new("other-manager","token"),default));
        await Assert.ThrowsAsync<PosDomain.DayCloseConflictException>(()=>Approvals.ApproveAsync(organization,command with{ReasonCode="review_after_changes"},new("manager","token"),default));
        foreach(var sql in new[]{"UPDATE branch_day_approval_decisions SET seal_version=seal_version+1","DELETE FROM branch_day_approval_decisions","TRUNCATE branch_day_approval_decisions","UPDATE branch_day_approval_audit SET action='approve'","DELETE FROM branch_day_approval_audit","TRUNCATE branch_day_approval_audit"})
            await Assert.ThrowsAsync<PostgresException>(()=>Sql(pos,sql));
        await Assert.ThrowsAsync<PostgresException>(()=>Sql(pos,File.ReadAllText(Path.Combine(Root(),"src/Tools/NexaConnect.DataMigration/Scripts/POS/0014_day_approvals/down.sql"))));
    }
    [ReportingDatabaseFact]
    public async Task Concurrent_managers_have_one_approval_winner_and_stale_versions_write_no_second_decision()
    {
        var sealedDay=await SealReady();
        async Task<PosApp.ApprovalView?> Attempt(string subject){try{return await Approvals.ApproveAsync(organization,ApprovalCommand(sealedDay.Version),new(subject,"token"),default);}catch(PosDomain.DayCloseConflictException){return null;}}
        var results=await Task.WhenAll(Attempt("manager"),Attempt("second-manager"));Assert.Single(results,x=>x is not null);
        var read=await Approvals.ReadAsync(organization,branch,Date,new("manager","token"),default);Assert.Single(read.History);
        await Assert.ThrowsAsync<PosDomain.DayCloseConflictException>(()=>Approvals.ApproveAsync(organization,ApprovalCommand(sealedDay.Version+1,read.Version),new("manager","token"),default));
    }
    [ReportingDatabaseFact]
    public async Task Next_day_changes_leave_approval_current_and_relevant_changes_supersede_without_rewriting_history()
    {
        var sealedDay=await SealReady();var result=await Approvals.ApproveAsync(organization,ApprovalCommand(sealedDay.Version),new("manager","token"),default);
        var order=Orders.OrderAggregate.Create(Guid.NewGuid(),organization,branch,[new Orders.OrderLine(Guid.NewGuid(),"Rice",10,1,"kitchen")],"THB",restaurantId:restaurant);
        await new OrderDb.PostgresOrderRepository(this.order).SaveAsync(order,default);
        Assert.Equal("approved",(await Approvals.ReadAsync(organization,branch,Date,new("manager","token"),default)).Status);
        await Sql(this.order,"UPDATE orders SET created_at_utc=$1 WHERE id=$2",Window.FromUtc.AddHours(1),order.Id);
        await Sealing.ReadAsync(organization,branch,Date,new("manager","token"),default); // Seal invalidation and approval supersession share the POS transaction.
        var changed=await Approvals.ReadAsync(organization,branch,Date,new("manager","token"),default);
        Assert.Equal("superseded",changed.Status);Assert.Null(changed.ValidatedAtUtc);Assert.Equal(result.Decision!.ApprovalId,changed.Decision!.ApprovalId);Assert.Equal(result.Decision.Snapshot.Seals,changed.Decision.Snapshot.Seals);
        await Sql(this.order,"UPDATE orders SET created_at_utc=$1 WHERE id=$2",Window.ToUtc.AddHours(1),order.Id);
        Assert.Equal("superseded",(await Approvals.ReadAsync(organization,branch,Date,new("manager","token"),default)).Status);
    }
    [ReportingDatabaseFact]
    public async Task Outage_clears_current_approval_and_unchanged_evidence_revalidation_preserves_the_original_decision()
    {
        var sealedDay=await SealReady();var approved=await Approvals.ApproveAsync(organization,ApprovalCommand(sealedDay.Version),new("manager","token"),default);
        fail=true;var unknown=await Approvals.ReadAsync(organization,branch,Date,new("manager","token"),default);
        Assert.Equal("unverified",unknown.Status);Assert.Null(unknown.ValidatedAtUtc);Assert.Null(unknown.SealVersion);
        fail=false;var restored=await Approvals.ReadAsync(organization,branch,Date,new("manager","token"),default);
        Assert.Equal("approved",restored.Status);Assert.Equal(approved.Decision!.ApprovalId,restored.Decision!.ApprovalId);Assert.Single(restored.History);Assert.Equal(3,restored.Version);
    }
    [ReportingDatabaseFact]
    public async Task Financial_change_between_preflight_and_commit_is_detected_on_the_next_approval_validation()
    {
        var sealedDay=await SealReady();var reader=new ApprovalReader(async(day,snapshot,token,ct)=>
        {
            var proof=await ApprovalEvidence.ReadRetainedAsync(day,snapshot,token,ct);
            var order=Orders.OrderAggregate.Create(Guid.NewGuid(),organization,branch,[new Orders.OrderLine(Guid.NewGuid(),"Rice",10,1,"kitchen")],"THB",restaurantId:restaurant);
            await new OrderDb.PostgresOrderRepository(this.order).SaveAsync(order,ct);await Sql(this.order,"UPDATE orders SET created_at_utc=$1 WHERE id=$2",Window.FromUtc.AddHours(1),order.Id);return proof;
        });
        var app=new PosApp.DayCloseApproval(new ApprovalDb.PostgresDayApprovalStore(pos),new ApprovalDb.PostgresDaySealStore(pos),reader,this,this,sealClock,NullLogger<PosApp.DayCloseApproval>.Instance);
        var approved=await app.ApproveAsync(organization,ApprovalCommand(sealedDay.Version),new("manager","token"),default);Assert.Equal("approved",approved.Status);
        var current=await Approvals.ReadAsync(organization,branch,Date,new("manager","token"),default);Assert.Equal("superseded",current.Status);Assert.Equal(approved.Decision!.ApprovalId,current.Decision!.ApprovalId);
    }
    [ReportingDatabaseFact]
    public async Task Concurrent_resealing_during_preflight_cannot_approve_another_version_or_create_source_seals()
    {
        var sealedDay=await SealReady();var reviewed=await Workflow.PrepareAsync(organization,Command(2),new("manager","token"),default);
        var reader=new ApprovalReader(async(day,snapshot,token,ct)=>
        {
            await new ApprovalDb.PostgresDaySealStore(pos).BeginAsync(day,SealCommand(reviewed.Version,sealedDay.Version),new("second-manager",Guid.NewGuid()),DateTimeOffset.UtcNow,ct);
            return await ApprovalEvidence.ReadRetainedAsync(day,snapshot,token,ct);
        });
        var app=new PosApp.DayCloseApproval(new ApprovalDb.PostgresDayApprovalStore(pos),new ApprovalDb.PostgresDaySealStore(pos),reader,this,this,sealClock,NullLogger<PosApp.DayCloseApproval>.Instance);
        await Assert.ThrowsAsync<PosDomain.DayCloseConflictException>(()=>app.ApproveAsync(organization,ApprovalCommand(sealedDay.Version),new("manager","token"),default));
        foreach(var db in new[]{order,payment,pos}){await using var q=db.CreateCommand("SELECT count(*) FROM source_day_seals");Assert.Equal(1L,await q.ExecuteScalarAsync());}
        await using var decisions=pos.CreateCommand("SELECT count(*) FROM branch_day_approval_decisions");Assert.Equal(0L,await decisions.ExecuteScalarAsync());
    }
    [ReportingDatabaseFact]
    public async Task Revocation_after_proof_and_reader_only_roles_cannot_commit_approval()
    {
        var sealedDay=await SealReady();var access=new ApprovalAccess(this){Approve=false};
        var app=new PosApp.DayCloseApproval(new ApprovalDb.PostgresDayApprovalStore(pos),new ApprovalDb.PostgresDaySealStore(pos),ApprovalEvidence,this,access,sealClock,NullLogger<PosApp.DayCloseApproval>.Instance);
        var read=await app.ReadAsync(organization,branch,Date,new("accountant","token"),default);Assert.False(read.CanApprove);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>app.ApproveAsync(organization,ApprovalCommand(sealedDay.Version),new("accountant","token"),default));
        access.Approve=true;var reader=new ApprovalReader(async(day,snapshot,token,ct)=>{var proof=await ApprovalEvidence.ReadRetainedAsync(day,snapshot,token,ct);access.Approve=false;return proof;});
        app=new(new ApprovalDb.PostgresDayApprovalStore(pos),new ApprovalDb.PostgresDaySealStore(pos),reader,this,access,sealClock,NullLogger<PosApp.DayCloseApproval>.Instance);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>app.ApproveAsync(organization,ApprovalCommand(sealedDay.Version),new("manager","token"),default));
        await using var q=pos.CreateCommand("SELECT count(*) FROM branch_day_approval_decisions");Assert.Equal(0L,await q.ExecuteScalarAsync());
    }
    [ReportingDatabaseFact]
    public async Task Failure_before_commit_rolls_back_decision_and_exact_retry_commits_once()
    {
        var sealedDay=await SealReady();var command=ApprovalCommand(sealedDay.Version);
        await Sql(pos,"CREATE FUNCTION approval_test_failure() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'test commit failure'; END $$; CREATE TRIGGER approval_test_failure BEFORE INSERT ON branch_day_approval_audit FOR EACH ROW EXECUTE FUNCTION approval_test_failure()");
        await Assert.ThrowsAsync<PostgresException>(()=>Approvals.ApproveAsync(organization,command,new("manager","token"),default));
        await using(var q=pos.CreateCommand("SELECT count(*) FROM branch_day_approval_decisions"))Assert.Equal(0L,await q.ExecuteScalarAsync());
        await Sql(pos,"DROP TRIGGER approval_test_failure ON branch_day_approval_audit; DROP FUNCTION approval_test_failure()");
        var result=await Approvals.ApproveAsync(organization,command,new("manager","token"),default);
        Assert.Single(result.History);Assert.Equal(command.OperationId,result.OperationDecision!.OperationId);
    }
    [ReportingDatabaseFact]
    public async Task Old_operation_replay_returns_original_decision_after_a_later_reseal_and_approval()
    {
        var firstSeal=await SealReady();var original=ApprovalCommand(firstSeal.Version);
        var first=await Approvals.ApproveAsync(organization,original,new("manager","token"),default);
        var reviewed=await Workflow.PrepareAsync(organization,Command(2),new("manager","token"),default);
        var secondSeal=await Sealing.SealAsync(organization,SealCommand(reviewed.Version,firstSeal.Version),new("manager","token"),default);
        var superseded=await Approvals.ReadAsync(organization,branch,Date,new("manager","token"),default);
        var second=await Approvals.ApproveAsync(organization,ApprovalCommand(secondSeal.Version,superseded.Version) with{ReasonCode="review_after_changes"},new("manager","token"),default);
        var replay=await Approvals.ApproveAsync(organization,original,new("manager","token"),default);
        Assert.Equal(first.Decision!.ApprovalId,replay.OperationDecision!.ApprovalId);
        Assert.Equal(second.Decision!.ApprovalId,replay.Decision!.ApprovalId);Assert.Equal(2,replay.History.Length);
    }
    private sealed class ApprovalReader(Func<PosDomain.DayIdentity,PosDomain.DayEvidence,string,CancellationToken,Task<PosDomain.DayEvidence>> read):PosApp.IApprovalSealEvidenceReader
    {public Task<PosDomain.DayEvidence> ReadRetainedAsync(PosDomain.DayIdentity day,PosDomain.DayEvidence snapshot,string token,CancellationToken ct)=>read(day,snapshot,token,ct);}
    private sealed class ApprovalAccess(DaySealPostgresTests owner):PosAuth.IAuthorizationDecisionClient
    {
        public bool Approve=true;
        public Task<PosAuth.AuthorizationDecision> DecideAsync(PosAuth.PosUserContext user,PosAuth.RestaurantAuthorizationScope scope,string permission,CancellationToken ct)=>
            Task.FromResult(new PosAuth.AuthorizationDecision(Guid.NewGuid(),owner.allowed&&(permission!=PosApp.DayCloseApproval.ApprovePermission||Approve),null));
    }
}
