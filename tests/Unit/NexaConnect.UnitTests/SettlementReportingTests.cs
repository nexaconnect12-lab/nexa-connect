using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.Reporting.Application;
namespace NexaConnect.UnitTests;

public sealed class SettlementReportingTests
{
    private static BranchDaySettledV2 Event()
    {
        var start=new DateTimeOffset(DateTime.UtcNow.Date,TimeSpan.Zero).AddDays(-2);var epoch=Guid.NewGuid();
        var v1=new BranchDaySettledV1(Guid.NewGuid(),Guid.NewGuid(),start.AddDays(1).AddHours(1),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),
            DateOnly.FromDateTime(start.Date),Guid.NewGuid(),1,"THB",10,2,8,0,[new("cash","THB",10)],
            [new("Order",Guid.NewGuid(),Guid.NewGuid(),1),new("Payment",Guid.NewGuid(),Guid.NewGuid(),1),new("POS",Guid.NewGuid(),epoch,3)]);
        return new(v1.EventId,v1.CorrelationId,v1.OccurredAtUtc,v1,Guid.NewGuid(),start,start.AddDays(1),new(Guid.NewGuid(),epoch,3,new('a',64),1,1,0,0,0,-10,
            [new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),v1.BusinessDate,start.AddHours(1),-10)]));
    }
    [Fact]
    public void Legacy_is_not_enriched_and_current_preserves_exact_financial_and_correction_provenance()
    {
        var e=Event();var legacy=SettlementReporting.Translate(new(e.Settlement,null));var current=SettlementReporting.Translate(new(null,e));
        Assert.Equal(1,legacy.ContractVersion);Assert.Null(legacy.Corrections);Assert.Null(legacy.FromUtc);
        Assert.Equal(2,current.ContractVersion);Assert.Equal(e.Corrections.Items.Single().OriginalSettlementId,current.Corrections!.Items.Single().OriginalSettlementId);
        Assert.Equal(-10,current.Corrections.Adjustment);
        var normalized=e with{OccurredAtUtc=e.OccurredAtUtc.ToOffset(TimeSpan.FromHours(7)),Settlement=e.Settlement with{OccurredAtUtc=e.OccurredAtUtc.ToOffset(TimeSpan.FromHours(7)),GrossSales=10.0000m},
            Corrections=e.Corrections with{Adjustment=-10.0000m}};
        Assert.Equal(FinancialCompleteness.Hash(current),FinancialCompleteness.Hash(SettlementReporting.Translate(new(null,normalized))));
    }
    [Theory]
    [InlineData("envelope")][InlineData("net")][InlineData("currency")][InlineData("source")][InlineData("scope")][InlineData("count")][InlineData("revision")][InlineData("amount")][InlineData("time")][InlineData("null-item")]
    public void Invalid_financial_or_correction_proof_fails_closed(string kind)
    {
        var e=Event();e=kind switch{
            "envelope"=>e with{EventId=Guid.NewGuid()},"net"=>e with{Settlement=e.Settlement with{NetSales=9}},
            "currency"=>e with{Settlement=e.Settlement with{Currency="USD"}},"source"=>e with{Settlement=e.Settlement with{Sources=[]}},
            "scope"=>e with{Settlement=e.Settlement with{OrganizationId=Guid.Empty}},"count"=>e with{Corrections=e.Corrections with{Missing=1}},
            "revision"=>e with{Corrections=e.Corrections with{SourceRevision=4}},"amount"=>e with{Corrections=e.Corrections with{Adjustment=-9}},
            "time"=>e with{Corrections=e.Corrections with{Items=[e.Corrections.Items[0] with{PostedAtUtc=e.ToUtc}]}},
            _=>e with{Corrections=e.Corrections with{Items=[null!]}}};
        Assert.Throws<ArgumentException>(()=>SettlementReporting.Translate(new(null,e)));
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Authorization_denial_cannot_read_source_or_projection(bool export)
    {
        var ports=new Ports();var e=Event();var reports=new SettlementReporting(ports,ports,ports);
        var range=new SettlementReportRange(e.Settlement.OrganizationId,e.Settlement.BranchId,e.Settlement.BusinessDate,e.Settlement.BusinessDate.AddDays(1));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async()=>{if(export)await reports.ExportAsync(range,"Bearer token",default);else await reports.ReadAsync(range,"Bearer token",default);});
        Assert.Equal(0,ports.Reads);
    }
    private sealed class Ports:ISettlementFactRepository,ISettlementSource,IReportingCustomerAuthorizer
    {
        public int Reads;
        public Task<bool> IsGrantedAsync(Guid org,Guid? branch,string permission,string authorization,CancellationToken ct)=>Task.FromResult(false);
        public Task<bool> ProjectAsync(NexaConnect.Services.Reporting.Domain.SettlementFact fact,CancellationToken ct)=>throw new NotImplementedException();
        public Task<SettlementReport> CompareAsync(SettlementManifest manifest,CancellationToken ct){Reads++;throw new NotImplementedException();}
        public Task<SettlementManifest> ReadAsync(SettlementReportRange range,string authorization,CancellationToken ct){Reads++;throw new NotImplementedException();}
    }
}
