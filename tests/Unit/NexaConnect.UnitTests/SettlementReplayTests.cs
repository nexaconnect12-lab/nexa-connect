using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.POS.Application.DayClose;
namespace NexaConnect.UnitTests;

public sealed class SettlementReplayTests
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private static (SettlementReplayRequest Request,SettlementReplayEvent Event) Sample()
    {
        var date=DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2));var r=new SettlementReplayRequest(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),date,date.AddDays(1),1);
        var e=new BranchDaySettledV1(Guid.NewGuid(),Guid.NewGuid(),DateTimeOffset.UtcNow.AddDays(-1),Guid.NewGuid(),r.OrganizationId,r.RestaurantId,r.BranchId,date,Guid.NewGuid(),1,"THB",10,0,10,0,[],[]);
        return(r,new(e.EventId,1,JsonSerializer.Serialize(e,Json)));
    }
    [Fact]
    public async Task Preview_is_read_only_and_raw_payload_bytes_scope_and_limit_bind_manifest()
    {
        var(r,e)=Sample();var store=new Store([e]);var transport=new Transport();var app=new SettlementReplay(store,transport);
        var plan=await app.PreviewAsync(r,default);Assert.Equal(1,plan.Count);Assert.Equal(0,store.Begins);Assert.Empty(transport.Events);
        Assert.NotEqual(plan.Manifest,SettlementReplay.Plan(r,[e with{Payload=e.Payload+" "}]).Manifest);
        Assert.NotEqual(plan.Manifest,SettlementReplay.Plan(r with{Limit=2},[e]).Manifest);Assert.DoesNotContain(e.Payload,e.ToString());
    }
    [Fact]
    public async Task Changed_selection_precedes_intent_or_transport_and_interrupted_publish_preserves_exact_identity_on_retry()
    {
        var(r,e)=Sample();var store=new Store([e]);var transport=new Transport{Fail=true};var app=new SettlementReplay(store,transport);var plan=await app.PreviewAsync(r,default);
        await Assert.ThrowsAsync<SettlementReplaySelectionChangedException>(()=>app.ExecuteAsync(r,Guid.NewGuid(),"retry",new('A',64),default));Assert.Empty(store.Attempts);Assert.Empty(transport.Events);
        var interrupted=await Assert.ThrowsAsync<SettlementReplayInterruptedException>(()=>app.ExecuteAsync(r,Guid.NewGuid(),"retry",plan.Manifest,default));Assert.NotEqual(Guid.Empty,interrupted.RunId);
        Assert.Equal("started",Assert.Single(store.Attempts));transport.Fail=false;
        await app.ExecuteAsync(r,Guid.NewGuid(),"retry",plan.Manifest,default);Assert.Equal(transport.Events[0],transport.Events[1]);Assert.Equal(new[]{"started","started","confirmed"},store.Attempts);
    }
    [Theory]
    [InlineData("scope")][InlineData("range")][InlineData("version")][InlineData("duplicate")][InlineData("unknown")][InlineData("limit")]
    public void Invalid_or_unbounded_original_selection_is_rejected(string kind)
    {
        var(r,e)=Sample();var original=JsonSerializer.Deserialize<BranchDaySettledV1>(e.Payload,Json)!;
        var events=new[]{e};
        switch(kind)
        {
            case "scope":e=e with{Payload=JsonSerializer.Serialize(original with{BranchId=Guid.NewGuid()},Json)};break;
            case "range":e=e with{Payload=JsonSerializer.Serialize(original with{BusinessDate=r.ToDate},Json)};break;
            case "version":e=e with{ContractVersion=3};break;
            case "duplicate":events=[e,e];r=r with{Limit=2};break;
            case "unknown":e=e with{Payload=e.Payload[..^1]+",\"extra\":true}"};break;
            default:r=r with{Limit=0};break;
        }
        if(kind!="duplicate")events=[e];Assert.ThrowsAny<Exception>(()=>SettlementReplay.Plan(r,events));
    }
    [Theory]
    [InlineData("operator")][InlineData("reason")][InlineData("hash")]
    public async Task Invalid_execution_attribution_does_not_read_or_write_source(string kind)
    {
        var(r,e)=Sample();var store=new Store([e]);var app=new SettlementReplay(store,new Transport());
        await Assert.ThrowsAsync<ArgumentException>(()=>app.ExecuteAsync(r,kind=="operator"?Guid.Empty:Guid.NewGuid(),kind=="reason"?"other":"retry",kind=="hash"?"bad":new('A',64),default));Assert.Equal(0,store.Begins);
    }
    private sealed class Store(SettlementReplayEvent[] selected):ISettlementReplayStore
    {
        public int Begins;public List<string> Attempts=[];
        public Task<IReadOnlyList<SettlementReplayEvent>> SelectAsync(SettlementReplayRequest r,CancellationToken ct)=>Task.FromResult<IReadOnlyList<SettlementReplayEvent>>(selected);
        public Task<IReadOnlyList<SettlementReplayEvent>> BeginAsync(Guid run,SettlementReplayRequest r,Guid op,string reason,string hash,CancellationToken ct)
        {if(SettlementReplay.Plan(r,selected).Manifest!=hash)throw new SettlementReplaySelectionChangedException();Begins++;return Task.FromResult<IReadOnlyList<SettlementReplayEvent>>(selected);}
        public Task AppendAsync(Guid run,Guid id,string outcome,CancellationToken ct){Attempts.Add(outcome);return Task.CompletedTask;}
    }
    private sealed class Transport:ISettlementReplayTransport
    {public bool Fail;public List<SettlementReplayEvent> Events=[];public Task PublishAsync(SettlementReplayEvent e,CancellationToken ct){Events.Add(e);if(Fail)throw new HttpRequestException();return Task.CompletedTask;}}
}
