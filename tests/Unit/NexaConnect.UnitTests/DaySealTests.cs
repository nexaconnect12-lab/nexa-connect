using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.POS.Domain.DayClose;
using NexaConnect.Services.Reporting.Application;
namespace NexaConnect.UnitTests;

public sealed class DaySealTests
{
    private static readonly DateTimeOffset Now=new(2026,9,3,0,0,0,TimeSpan.Zero);
    private static DayEvidence Reviewed()
    {
        static CutoffReference Ref()=>new(Guid.NewGuid(),1,new('a',64),Guid.NewGuid(),0);
        return DayClosePreparationTests.Evidence() with{Cutoff=new(Ref(),Ref(),Ref(),Guid.NewGuid(),true,0,true,2)};
    }
    private static DayEvidence Sealed(DayEvidence value)
    {
        static DaySealReference Ref(CutoffReference r)=>new(Guid.NewGuid(),r.ManifestId,r.RevisionEpoch!.Value,r.SourceRevision!.Value);
        return value with{Seals=new(Ref(value.Cutoff!.Order),Ref(value.Cutoff.Payment),Ref(value.Cutoff.Pos),0,true,true)};
    }
    [Fact]
    public void Sealing_requires_reviewed_evidence_and_resume_preserves_the_pinned_cutoff()
    {
        var day=new DayIdentity(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new(2026,9,1));
        var aggregate=BranchDaySeal.New(day,Now);var claim=Guid.NewGuid();
        var command=new PreparationCommand(day.BranchId,day.BusinessDate,Guid.NewGuid(),0,"routine_close",2);
        Assert.Throws<DayCloseConflictException>(()=>aggregate.Begin(command,"manager",claim,Now,false,null));
        var reviewed=Reviewed();aggregate.Begin(command,"manager",claim,Now,false,reviewed);
        Assert.True(reviewed.SameEvidence(aggregate.Export().Snapshot!));
        Assert.Throws<DayCloseConflictException>(()=>aggregate.Begin(command with{OperationId=Guid.NewGuid(),ExpectedVersion=99},"manager",Guid.NewGuid(),Now.AddSeconds(31),false,Reviewed()));
        Assert.True(reviewed.SameEvidence(aggregate.Export().Snapshot!));
        var restarted=BranchDaySeal.Restore(aggregate.Export());var resumed=Guid.NewGuid();
        restarted.Begin(command,"manager",resumed,Now.AddSeconds(31),true,Reviewed());
        Assert.True(reviewed.SameEvidence(restarted.Export().Snapshot!));
        Assert.Throws<DayCloseConflictException>(()=>restarted.Complete(claim,Sealed(reviewed),Now.AddSeconds(32)));
        restarted.Complete(resumed,Sealed(reviewed),Now.AddSeconds(32));Assert.Equal("ready_for_review",restarted.Export().Status);
    }
    [Fact]
    public void Late_changes_block_sealed_readiness_without_rewriting_reviewed_seals()
    {
        var day=new DayIdentity(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new(2026,9,1));
        var aggregate=BranchDaySeal.New(day,Now);var claim=Guid.NewGuid();var reviewed=Reviewed();var evidence=Sealed(reviewed);
        aggregate.Begin(new(day.BranchId,day.BusinessDate,Guid.NewGuid(),0,"routine_close",2),"manager",claim,Now,false,reviewed);
        aggregate.Complete(claim,evidence,Now);
        Assert.True(aggregate.Validate(evidence with{Seals=evidence.Seals! with{PendingChanges=3}},Now.AddSeconds(1)));
        var state=aggregate.Export();Assert.Equal("blocked",state.Status);Assert.Contains("sealed_changes_pending",state.Blockers);
        Assert.Equal(evidence.Seals,state.Snapshot!.Seals);Assert.Equal(3,state.PendingSealChanges);
    }
    [Fact]
    public void Unsealed_mismatched_or_unproven_evidence_cannot_be_ready()
    {
        var reviewed=Reviewed();var sealedEvidence=Sealed(reviewed);
        Assert.False((sealedEvidence with{Seals=sealedEvidence.Seals! with{Order=sealedEvidence.Seals.Order with{ManifestId=Guid.NewGuid()}}}).IsValid(Now));
        Assert.Contains("seal_journal_unavailable",(sealedEvidence with{Seals=sealedEvidence.Seals! with{JournalComplete=false}}).Blockers());
        Assert.Contains("seal_delivery_unproven",(sealedEvidence with{Seals=sealedEvidence.Seals! with{DeliveryComplete=false}}).Blockers());
        var day=new DayIdentity(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new(2026,9,1));
        Assert.Throws<ArgumentException>(()=>BranchDaySeal.Restore(new(day,2,"ready_for_review",reviewed,[],Now)));
        var aggregate=BranchDaySeal.New(day,Now);var claim=Guid.NewGuid();
        aggregate.Begin(new(day.BranchId,day.BusinessDate,Guid.NewGuid(),0,"routine_close",2),"manager",claim,Now,false,reviewed);
        aggregate.Complete(claim,reviewed,Now);Assert.Equal("blocked",aggregate.Export().Status);
    }
    [Fact]
    public void Each_source_domain_rejects_revision_conflicts_and_operational_blockers()
    {
        var id=Guid.NewGuid();var epoch=Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(()=>NexaConnect.Services.Order.Domain.FinancialDaySeal.Retain(id,epoch,1,id,epoch,2,false,true,0));
        Assert.Throws<InvalidOperationException>(()=>NexaConnect.Services.Payment.Domain.FinancialDaySeal.Retain(id,epoch,1,id,epoch,1,true,true,1));
        Assert.Throws<InvalidOperationException>(()=>NexaConnect.Services.POS.Domain.FinancialDaySeal.Retain(id,epoch,1,id,epoch,1,true,false,0));
        Assert.Equal(id,NexaConnect.Services.Order.Domain.FinancialDaySeal.Retain(id,epoch,1,id,epoch,1,true,true,0).ManifestId);
    }
    [Fact]
    public async Task Sealed_reconciliation_checks_selected_events_even_after_journaled_changes_but_denial_precedes_source_reads()
    {
        var fixture=new Fixture();var result=await fixture.App.CheckAsync(fixture.Command,"Bearer manager",default);
        Assert.True(result.DeliveryComplete);Assert.Equal(fixture.Order,result.OrderSealId);
        fixture.Journal=false;result=await fixture.App.CheckAsync(fixture.Command,"Bearer manager",default);Assert.False(result.DeliveryComplete);
        fixture.Reads=0;fixture.Allowed=false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>fixture.App.CheckAsync(fixture.Command,"Bearer manager",default));Assert.Equal(0,fixture.Reads);
    }
    private sealed class Fixture:ISealedSources,IReportingCustomerAuthorizer,IFinancialCompletenessRepository
    {
        private readonly DateTimeOffset now=DateTimeOffset.UtcNow;
        private readonly Guid org=Guid.NewGuid(),restaurant=Guid.NewGuid(),branch=Guid.NewGuid(),payment=Guid.NewGuid(),orderManifest=Guid.NewGuid(),paymentManifest=Guid.NewGuid(),epoch=Guid.NewGuid();
        public Guid Order=Guid.NewGuid();public bool Allowed=true,Journal=true;public int Reads;
        private EndOfDayWindow Window=>new(org,restaurant,branch,now.AddDays(-2),now.AddDays(-1));
        public SealedReconciliationCommand Command=>new(Window,Order,payment);
        public SealedDayReconciliation App=>new(this,this,this);
        public Task<SourceSealRead<OrderDaySummary>> OrderSealAsync(EndOfDayWindow w,Guid id,string bearer,CancellationToken ct)
        {
            Reads++;
            return Task.FromResult(new SourceSealRead<OrderDaySummary>(new(Order,Order,w,orderManifest,new(epoch,0),now),
                new(orderManifest,orderManifest,1,w,now,new('a',64),new(w,now,0,0,0,0,[],[]),[],[],SourceRevision:new(epoch,0),EvidenceProtocolVersion:2),2,Journal,[],true));
        }
        public Task<SourceSealRead<PaymentDaySummary>> PaymentSealAsync(EndOfDayWindow w,Guid id,string bearer,CancellationToken ct)=>Task.FromResult(
            new SourceSealRead<PaymentDaySummary>(new(payment,payment,w,paymentManifest,new(epoch,0),now),
                new(paymentManifest,paymentManifest,1,w,now,new('b',64),new(w,now,0,0,0,0,[]),[],[],SourceRevision:new(epoch,0),EvidenceProtocolVersion:2),0,Journal,[],false));
        public Task<bool> IsGrantedAsync(Guid org,Guid? branch,string permission,string bearer,CancellationToken ct)=>Task.FromResult(Allowed);
        public Task<FinancialCompletenessObservation> CheckAsync(FinancialCompletenessSource s,CancellationToken ct)=>Task.FromResult(FinancialCompletenessEvaluator.Evaluate(s,new([],[],[],new Dictionary<Guid,string>(),new Dictionary<Guid,string>())));
        public Task RecordAsync(FinancialCompletenessObservation v,string actor,CancellationToken ct)=>throw new NotSupportedException();
        public Task<FinancialCompletenessObservation?> LatestAsync(ReportingRange r,CancellationToken ct)=>throw new NotSupportedException();
    }
}
