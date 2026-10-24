using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.Reporting.Application;

namespace NexaConnect.UnitTests;

public sealed class EndOfDayDraftTests
{
    private static readonly Guid Org=Guid.NewGuid(), Restaurant=Guid.NewGuid(), Branch=Guid.NewGuid();
    private static readonly DateTimeOffset Now=new(2026,9,3,0,0,0,TimeSpan.Zero);
    private static BranchBusinessCalendar Calendar(string zone="Asia/Bangkok")=>new(Org,Restaurant,Branch,zone,"THB");
    [Theory]
    [InlineData("Asia/Bangkok",2026,9,1,24,"2026-08-31T17:00:00Z")]
    [InlineData("America/New_York",2026,3,8,23,"2026-03-08T05:00:00Z")]
    [InlineData("America/New_York",2025,11,2,25,"2025-11-02T04:00:00Z")]
    public void Configured_timezone_defines_half_open_day(string zone,int year,int month,int day,int hours,string start)
    {
        var window=EndOfDayDraft.Window(Org,Calendar(zone),new(year,month,day),Now);
        Assert.Equal(DateTimeOffset.Parse(start),window.FromUtc);Assert.Equal(TimeSpan.FromHours(hours),window.ToUtc-window.FromUtc);
    }
    [Fact] public void Open_dates_bad_midnight_transitions_and_foreign_tenants_are_rejected()
    {
        Assert.Throws<ArgumentException>(()=>EndOfDayDraft.Window(Org,Calendar(),new(2026,9,3),Now));
        Assert.Throws<ArgumentException>(()=>EndOfDayDraft.Window(Org,Calendar("America/Havana"),new(2026,3,8),Now));
        Assert.Throws<ArgumentException>(()=>EndOfDayDraft.Window(Org,Calendar("Pacific/Apia"),new(2011,12,30),Now));
        Assert.Throws<UnauthorizedAccessException>(()=>EndOfDayDraft.Window(Guid.NewGuid(),Calendar(),new(2026,9,1),Now));
        Assert.Throws<InvalidOperationException>(()=>EndOfDayDraft.Window(Org,Calendar("invalid-zone"),new(2026,9,1),Now));
    }
    [Fact] public async Task Authoritative_totals_and_unresolved_work_are_explicit_despite_projection_delay()
    {
        var f=new Fixture(); var report=await f.Query.ReadAsync(Org,Branch,new(2026,9,1),"Bearer customer",default);
        Assert.Equal("draft",report.Status);Assert.Equal(100,report.GrossSales);Assert.Equal(25,report.CompletedRefunds);Assert.Equal(75,report.NetSales);
        Assert.Equal(100,Assert.Single(report.Tenders).Amount);Assert.Equal(2,report.CashVariance);
        foreach(var issue in new[]{"projection_totals_differ","financial_evidence_not_checked","open_shifts","unresolved_payments","unresolved_refunds","unresolved_orders","pending_cash_reviews","missing_source_evidence","cash_variance"})
            Assert.Contains(issue,report.Issues);
        f.Projection=new(1,100,100,25,75,"THB",Now);
        Assert.DoesNotContain("projection_totals_differ",(await f.Query.ReadAsync(Org,Branch,new(2026,9,1),"Bearer customer",default)).Issues);
    }
    [Fact] public async Task Denial_precedes_source_reads_and_failure_never_returns_partial_totals()
    {
        var f=new Fixture{Allowed=false};
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Query.ReadAsync(Org,Branch,new(2026,9,1),"Bearer customer",default));Assert.Equal(0,f.SourceReads);
        f.Allowed=true;f.Fail=true;
        await Assert.ThrowsAsync<HttpRequestException>(()=>f.Query.ReadAsync(Org,Branch,new(2026,9,1),"Bearer customer",default));
    }
    [Fact] public async Task Mismatched_source_scope_and_mixed_currencies_fail_closed()
    {
        var f=new Fixture{WrongScope=true};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Query.ReadAsync(Org,Branch,new(2026,9,1),"Bearer customer",default));
        f.WrongScope=false;f.Currency="USD";
        await Assert.ThrowsAsync<MixedReportingCurrencyException>(()=>f.Query.ReadAsync(Org,Branch,new(2026,9,1),"Bearer customer",default));
    }
    [Fact] public async Task Historical_checks_never_certify_new_source_observations()
    {
        var f=new Fixture();var window=EndOfDayDraft.Window(Org,Calendar(),new(2026,9,1),Now);
        f.Check=new(Guid.NewGuid(),new(Org,Branch,window.FromUtc,window.ToUtc),"observed_complete",Now.AddHours(-1),Now.AddHours(-2),Now.AddHours(-2),"hash",0,0,0,0,new(1,1,0,0,0),new(1,1,0,0,0),new(1,1,0,0,0));
        var result=await f.Query.ReadAsync(Org,Branch,new(2026,9,1),"Bearer customer",default);
        Assert.Equal("draft",result.Status);Assert.Contains("recorded_check_is_historical",result.Issues);Assert.Same(f.Check,result.RecordedCompleteness);
    }
    private sealed class Clock:TimeProvider{public override DateTimeOffset GetUtcNow()=>Now;}
    private sealed class Fixture:IEndOfDaySources,IReportingCustomerAuthorizer,IReportingReadRepository,IFinancialCompletenessRepository
    {
        public bool Allowed=true,Fail,WrongScope;public int SourceReads;public string Currency="THB";
        public DashboardSummary Projection=new(0,0,0,0,0,null,null);public FinancialCompletenessObservation? Check;
        public EndOfDayDraft Query=>new(this,this,this,this,new Clock());
        public Task<bool> IsGrantedAsync(Guid org,Guid? branch,string permission,string bearer,CancellationToken ct)
        {Assert.Equal("reporting.sales.read",permission);return Task.FromResult(Allowed&&org==Org&&branch==Branch);}
        public Task<BranchBusinessCalendar?> CalendarAsync(Guid branch,CancellationToken ct){SourceReads++;return Task.FromResult<BranchBusinessCalendar?>(Calendar());}
        public Task<OrderDaySummary> OrderAsync(EndOfDayWindow w,string bearer,CancellationToken ct)=>Task.FromResult(new OrderDaySummary(WrongScope?w with{OrganizationId=Guid.NewGuid()}:w,Now,100,1,1,1,[new("cash",Currency,100)],[Currency]));
        public Task<PaymentDaySummary> PaymentAsync(EndOfDayWindow w,string bearer,CancellationToken ct)
        {if(Fail)throw new HttpRequestException("dependency");return Task.FromResult(new PaymentDaySummary(w,Now,25,1,1,0,["THB"]));}
        public Task<PosDaySummary> PosAsync(EndOfDayWindow w,string bearer,CancellationToken ct)=>Task.FromResult(new PosDaySummary(w,Now,1,1,1,2,["THB"]));
        public Task<DashboardSummary> DashboardAsync(ReportingRange range,CancellationToken ct)=>Task.FromResult(Projection);
        public Task<SalesReport> SalesAsync(ReportingRange range,CancellationToken ct)=>throw new NotSupportedException();
        public Task<FinancialCompletenessObservation?> LatestAsync(ReportingRange range,CancellationToken ct)=>Task.FromResult(Check);
        public Task<FinancialCompletenessObservation> CheckAsync(FinancialCompletenessSource source,CancellationToken ct)=>throw new NotSupportedException();
        public Task RecordAsync(FinancialCompletenessObservation value,string actor,CancellationToken ct)=>throw new NotSupportedException();
    }
}
