using Microsoft.Extensions.Logging.Abstractions;
using NexaConnect.Services.POS.Domain.DayClose;
using NexaConnect.Services.POS.Application.DayClose;
using NexaConnect.Services.POS.Application.Shifts;
namespace NexaConnect.UnitTests;

public sealed class DayClosePreparationTests
{
    private static readonly DayIdentity Day=new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new(2026,9,1));
    private static readonly DateTimeOffset Now=new(2026,9,3,0,0,0,TimeSpan.Zero);
    private static PreparationCommand Command(long version=0)=>new(Day.BranchId,Day.BusinessDate,Guid.NewGuid(),version,"routine_close");
    public static DayEvidence Evidence()=>new("UTC","THB",Now.AddDays(-2),Now.AddDays(-1),100,25,75,0,[new("cash","THB",100)],new('a',64),new('b',64),new('c',64),0,0,0,0,0,0,["recorded_check_is_historical"],Now);
    [Fact] public void Preparation_is_fenced_and_explicitly_refreshed_after_same_total_identity_changes()
    {
        var aggregate=BranchDayClose.New(Day,Now);var claim=Guid.NewGuid();aggregate.Begin(Command(),"manager",claim,Now,false);
        Assert.Equal("preparing",aggregate.Export().Status);Assert.Throws<DayCloseConflictException>(()=>aggregate.Complete(Guid.NewGuid(),Evidence(),Now));
        aggregate.Complete(claim,Evidence(),Now);Assert.Equal(2,aggregate.Export().Version);Assert.Equal("ready_for_review",aggregate.Export().Status);
        Assert.False(aggregate.Validate(Evidence() with{ObservedAtUtc=Now.AddSeconds(1)},Now.AddSeconds(1)));
        Assert.True(aggregate.Validate(Evidence() with{OrderVersion=new('d',64)},Now.AddSeconds(1)));
        var blocked=aggregate.Export();Assert.Equal("blocked",blocked.Status);Assert.Equal(3,blocked.Version);
        Assert.Contains("source_evidence_changed",blocked.Blockers);Assert.Equal(new('a',64),blocked.Snapshot!.OrderVersion);
        Assert.Throws<DayCloseConflictException>(()=>aggregate.Begin(Command(2),"manager",Guid.NewGuid(),Now,false));
        var next=Guid.NewGuid();aggregate.Begin(Command(3),"manager",next,Now,false);aggregate.Complete(next,Evidence() with{OrderVersion=new('d',64)},Now);Assert.Equal("ready_for_review",aggregate.Export().Status);
    }
    [Theory][InlineData("unresolved_orders")][InlineData("unresolved_payments")][InlineData("unresolved_refunds")][InlineData("open_shifts")][InlineData("open_cash_sessions")][InlineData("pending_cash_reviews")][InlineData("missing_source_evidence")][InlineData("projection_totals_differ")][InlineData("financial_evidence_not_checked")][InlineData("recorded_financial_gaps")][InlineData("new_unknown_issue")]
    public void Every_blocker_including_unknown_codes_fails_closed(string issue)=>Assert.Contains(issue,(Evidence() with{Issues=[issue]}).Blockers());
    [Fact] public void Approved_variance_is_retained_but_pending_review_and_missing_fingerprint_block()
    {
        Assert.Empty((Evidence() with{CashVariance=-5,Issues=["cash_variance","recorded_check_is_historical"]}).Blockers());
        Assert.Contains("cash_variance",(Evidence() with{CashVariance=-5,PendingCashReviews=1,Issues=["cash_variance"]}).Blockers());
        Assert.Contains("source_evidence_unavailable",(Evidence() with{PosVersion=null}).Blockers());
        Assert.Contains("open_shifts",(Evidence() with{OpenShifts=1,Issues=[]}).Blockers());
    }
    [Fact] public void Restart_resume_requires_expiry_and_fences_old_claims()
    {
        var aggregate=BranchDayClose.New(Day,Now);var command=Command();var old=Guid.NewGuid();aggregate.Begin(command,"manager",old,Now,false);
        var restored=BranchDayClose.Restore(aggregate.Export());Assert.Throws<DayCloseConflictException>(()=>restored.Begin(command,"manager",Guid.NewGuid(),Now.AddSeconds(29),true));
        var fresh=Guid.NewGuid();restored.Begin(command,"manager",fresh,Now.AddSeconds(31),true);
        Assert.Throws<DayCloseConflictException>(()=>restored.Complete(old,Evidence(),Now.AddSeconds(31)));
        restored.Complete(fresh,null,Now.AddSeconds(31));Assert.Equal("blocked",restored.Export().Status);Assert.Contains("source_unavailable",restored.Export().Blockers);
    }
    [Fact] public void Exported_evidence_cannot_mutate_the_aggregate()
    {
        var aggregate=BranchDayClose.New(Day,Now);var claim=Guid.NewGuid();aggregate.Begin(Command(),"manager",claim,Now,false);var input=Evidence();aggregate.Complete(claim,input,Now);
        input.Issues[0]="unknown";var exported=aggregate.Export();exported.Snapshot!.Tenders[0]=new("cash","THB",999);exported.Blockers.ToList().Add("bad");
        Assert.Equal(100,aggregate.Export().Snapshot!.Tenders[0].Amount);Assert.DoesNotContain("unknown",aggregate.Export().Snapshot!.Issues);
    }
    [Fact] public async Task Live_authorization_and_hierarchy_are_required_before_any_storage_or_evidence()
    {
        var f=new Fixture{Granted=false};await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.App.PrepareAsync(Day.OrganizationId,Command(),new("manager","token"),default));Assert.Equal(0,f.StorageCalls);Assert.Equal(0,f.EvidenceCalls);
        f.Granted=true;await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.App.ReadAsync(Guid.NewGuid(),Day.BranchId,Day.BusinessDate,new("manager","token"),default));Assert.Equal(0,f.StorageCalls);
        f.PrepareGranted=false;var view=await f.App.ReadAsync(Day.OrganizationId,Day.BranchId,Day.BusinessDate,new("accountant","token"),default);Assert.False(view.CanPrepare);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.App.PrepareAsync(Day.OrganizationId,Command(),new("accountant","token"),default));Assert.Equal(1,f.StorageCalls);
    }
    [Fact] public async Task Failure_blocks_persisted_readiness_and_cancellation_never_returns_a_saved_ready_result()
    {
        var f=new Fixture();var command=Command();var first=await f.App.PrepareAsync(Day.OrganizationId,command,new("manager","token"),default);Assert.Equal("ready_for_review",first.Status);
        f.Fail=true;var fresh=await f.App.ReadAsync(Day.OrganizationId,Day.BranchId,Day.BusinessDate,new("manager","token"),default);
        Assert.Equal("blocked",fresh.Status);Assert.Contains("source_unavailable",fresh.Blockers);Assert.NotNull(fresh.Snapshot);
        f.Fail=false;using var cancelled=new CancellationTokenSource();f.CancelSource=cancelled;await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.App.PrepareAsync(Day.OrganizationId,Command(fresh.Version),new("manager","token"),cancelled.Token));Assert.Equal("preparing",f.State!.Status);
    }
    [Theory][InlineData("net")][InlineData("count")][InlineData("open_window")][InlineData("currency")][InlineData("future_observation")]
    public void Domain_never_accepts_invalid_financial_calendar_or_observation_evidence(string fault)
    {
        var evidence=fault switch{"net"=>Evidence() with{NetSales=999},"count"=>Evidence() with{OpenShifts=-1},"open_window"=>Evidence() with{ToUtc=Now.AddDays(1)},"currency"=>Evidence() with{Currency="USD"},_=>Evidence() with{ObservedAtUtc=Now.AddSeconds(1)}};
        var aggregate=BranchDayClose.New(Day,Now);var claim=Guid.NewGuid();aggregate.Begin(Command(),"manager",claim,Now,false);aggregate.Complete(claim,evidence,Now);
        Assert.Equal("blocked",aggregate.Export().Status);Assert.Null(aggregate.Export().Snapshot);Assert.Contains("source_unavailable",aggregate.Export().Blockers);
    }
    private sealed class Fixture:IDayCloseStore,IDayCloseEvidenceReader,IRestaurantScopeReader,IAuthorizationDecisionClient
    {
        public bool Granted=true,PrepareGranted=true,Fail;public CancellationTokenSource? CancelSource;public int StorageCalls,EvidenceCalls;public PreparationState? State;
        public DayClosePreparation App=>new(this,this,this,this,new Clock(),NullLogger<DayClosePreparation>.Instance);
        public Task<RestaurantAuthorizationScope> GetAsync(Guid branch,CancellationToken ct)=>Task.FromResult(new RestaurantAuthorizationScope(Day.OrganizationId,Day.RestaurantId,Day.BranchId));
        public Task<AuthorizationDecision> DecideAsync(PosUserContext user,RestaurantAuthorizationScope scope,string permission,CancellationToken ct)=>Task.FromResult(new AuthorizationDecision(Guid.NewGuid(),Granted&&(permission!=DayClosePreparation.PreparePermission||PrepareGranted),null));
        public Task<DayEvidence> ReadAsync(DayIdentity day,string token,CancellationToken ct){EvidenceCalls++;if(CancelSource is not null){CancelSource.Cancel();ct.ThrowIfCancellationRequested();}if(Fail)throw new HttpRequestException("restricted");return Task.FromResult(Evidence());}
        public Task<PreparationState?> ReadAsync(DayIdentity day,CancellationToken ct){StorageCalls++;return Task.FromResult(State);}
        public Task<PreparationLease> BeginAsync(DayIdentity day,PreparationCommand command,PreparationActor actor,DateTimeOffset now,CancellationToken ct){StorageCalls++;var aggregate=State is null?BranchDayClose.New(day,now):BranchDayClose.Restore(State);var claim=Guid.NewGuid();aggregate.Begin(command,actor.Subject,claim,now,false);State=aggregate.Export();return Task.FromResult(new PreparationLease(State,claim));}
        public Task<PreparationState> CompleteAsync(DayIdentity day,Guid claim,DayEvidence? evidence,PreparationActor actor,DateTimeOffset now,CancellationToken ct){var aggregate=BranchDayClose.Restore(State!);aggregate.Complete(claim,evidence,now);State=aggregate.Export();return Task.FromResult(State);}
        public Task<PreparationState> ValidateAsync(DayIdentity day,long expectedVersion,DayEvidence? evidence,PreparationActor actor,DateTimeOffset now,CancellationToken ct){var aggregate=BranchDayClose.Restore(State!);aggregate.Validate(evidence,now);State=aggregate.Export();return Task.FromResult(State);}
    }
    private sealed class Clock:TimeProvider{public override DateTimeOffset GetUtcNow()=>Now;}
}
