using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.POS.Domain.DayClose;
using NexaConnect.Services.Reporting.Application;

namespace NexaConnect.UnitTests;

public sealed class DayCutoffTests
{
    private static readonly DateTimeOffset Now=DateTimeOffset.UtcNow;
    private static readonly EndOfDayWindow Window=new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Now.AddDays(-2),Now.AddDays(-1));
    private static CutoffReference Reference()=>new(Guid.NewGuid(),1,new('a',64));
    [Fact]
    public void Ordinary_preparation_evidence_cannot_become_a_ready_cutoff()
    {
        var day=new DayIdentity(Window.OrganizationId,Window.RestaurantId,Window.BranchId,new(2026,9,1));
        var aggregate=BranchDayCutoff.New(day,Now);var claim=Guid.NewGuid();
        aggregate.Begin(new(day.BranchId,day.BusinessDate,Guid.NewGuid(),0,"routine_close"),"manager",claim,Now,false);
        aggregate.Complete(claim,DayClosePreparationTests.Evidence(),Now);
        Assert.Equal("blocked",aggregate.Export().Status);Assert.Null(aggregate.Export().Snapshot);
    }
    [Theory][InlineData(false,0,"cutoff_superseded")][InlineData(true,1,"cutoff_financial_gaps")]
    public void Sources_or_financial_gaps_block_an_otherwise_ready_cutoff(bool current,int gaps,string blocker)
    {
        var evidence=DayClosePreparationTests.Evidence() with{Cutoff=new(Reference(),Reference(),Reference(),Guid.NewGuid(),current,gaps)};
        Assert.Contains(blocker,evidence.Blockers());
    }
    [Fact]
    public void New_check_identity_preserves_readiness_but_source_generation_does_not()
    {
        var evidence=DayClosePreparationTests.Evidence() with{Cutoff=new(Reference(),Reference(),Reference(),Guid.NewGuid(),true,0)};
        Assert.True(evidence.SameEvidence(evidence with{Cutoff=evidence.Cutoff! with{CheckId=Guid.NewGuid()}}));
        Assert.False(evidence.SameEvidence(evidence with{Cutoff=evidence.Cutoff! with{Order=evidence.Cutoff.Order with{Generation=2}}}));
        Assert.False((evidence with{Cutoff=evidence.Cutoff! with{Order=evidence.Cutoff.Order with{ManifestId=Guid.Empty}}}).IsValid(new(2026,9,3,0,0,0,TimeSpan.Zero)));
    }
    [Fact]
    public async Task Reconciliation_checks_current_sources_after_the_reporting_snapshot()
    {
        var fixture=new Fixture();var result=await fixture.App.CheckAsync(fixture.Command,"Bearer manager",default);
        Assert.Equal("observed_complete",result.Status);Assert.True(result.SourcesCurrent);Assert.Equal(2,fixture.OrderReads);
        fixture.OrderReads=0;fixture.Drift=true;
        result=await fixture.App.CheckAsync(fixture.Command,"Bearer manager",default);
        Assert.Equal("sources_changed",result.Status);Assert.False(result.SourcesCurrent);
    }
    [Fact]
    public async Task Permission_revocation_or_wrong_manifest_scope_cannot_reach_projection_inventory()
    {
        var fixture=new Fixture{Allowed=false};
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>fixture.App.CheckAsync(fixture.Command,"Bearer manager",default));
        Assert.Equal(0,fixture.Checks);Assert.Equal(0,fixture.OrderReads);
        fixture.Allowed=true;fixture.WrongScope=true;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.App.CheckAsync(fixture.Command,"Bearer manager",default));Assert.Equal(0,fixture.Checks);
    }
    private sealed class Fixture:ICutoffSources,IReportingCustomerAuthorizer,IFinancialCompletenessRepository
    {
        private readonly Guid order=Guid.NewGuid(),payment=Guid.NewGuid();
        public bool Allowed=true,Drift,WrongScope;public int OrderReads,Checks;
        public CutoffReconciliationCommand Command=>new(Window,order,payment);
        public DayCutoffReconciliation App=>new(this,this,this);
        public Task<bool> IsGrantedAsync(Guid org,Guid? branch,string permission,string bearer,CancellationToken ct)=>Task.FromResult(Allowed);
        public Task<SourceCutoffRead<OrderDaySummary>> OrderAsync(EndOfDayWindow window,Guid id,string bearer,CancellationToken ct)
        {
            OrderReads++;var w=WrongScope?window with{OrganizationId=Guid.NewGuid()}:window;
            return Task.FromResult(new SourceCutoffRead<OrderDaySummary>(new(order,Guid.NewGuid(),1,w,Now,new('a',64),new(w,Now,0,0,0,0,[],[]),[],[]),!(Drift&&OrderReads>1)));
        }
        public Task<SourceCutoffRead<PaymentDaySummary>> PaymentAsync(EndOfDayWindow w,Guid id,string bearer,CancellationToken ct)=>
            Task.FromResult(new SourceCutoffRead<PaymentDaySummary>(new(payment,Guid.NewGuid(),1,w,Now,new('b',64),new(w,Now,0,0,0,0,[]),[],[]),true));
        public Task<FinancialCompletenessObservation> CheckAsync(FinancialCompletenessSource source,CancellationToken ct)
        {Checks++;return Task.FromResult(FinancialCompletenessEvaluator.Evaluate(source,new([],[],[],new Dictionary<Guid,string>(),new Dictionary<Guid,string>())));}
        public Task RecordAsync(FinancialCompletenessObservation value,string actor,CancellationToken ct)=>throw new NotSupportedException();
        public Task<FinancialCompletenessObservation?> LatestAsync(ReportingRange range,CancellationToken ct)=>throw new NotSupportedException();
    }
}
