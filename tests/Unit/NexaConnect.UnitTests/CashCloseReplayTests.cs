using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.POS.Application.CashReviews;

namespace NexaConnect.UnitTests;

public sealed class CashCloseReplayTests
{
    private static (CashCloseReplayRequest Request, Store Store, Transport Transport) Fixture()
    {
        var request = new CashCloseReplayRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, 10);
        var e = new PosCashCloseSnapshotV1(Guid.NewGuid(), Guid.NewGuid(), request.FromUtc.AddHours(1), request.OrganizationId,
            Guid.NewGuid(), request.BranchId, request.StoreId, Guid.NewGuid(), Guid.NewGuid(), request.FromUtc,
            "THB", 100, 95, -5, 1, 2, 0, "review_required");
        return (request, new Store { Events = [new(e.EventId, JsonSerializer.Serialize(e))] }, new Transport());
    }
    [Fact]
    public async Task Preview_is_read_only_and_execution_preserves_original_payload_with_audit_before_send()
    {
        var (r,s,t) = Fixture(); var service = new CashCloseReplay(s,t);
        var plan = await service.PreviewAsync(r,default);
        Assert.Equal(1, plan.Count); Assert.Empty(s.Audit); Assert.Empty(t.Sent);
        t.BeforeSend = () => Assert.Equal(new[] { "run", "started" }, s.Audit);
        await service.ExecuteAsync(r,Guid.NewGuid(),"rebuild",plan.Manifest,default);
        Assert.Equal(s.Events,t.Sent); Assert.Equal(new[] { "run", "started", "confirmed" }, s.Audit);
    }
    [Fact]
    public async Task Changed_payload_or_scope_requires_fresh_preview()
    {
        var (r,s,t) = Fixture(); var service = new CashCloseReplay(s,t); var plan = await service.PreviewAsync(r,default);
        s.Events = [s.Events[0] with { Payload = s.Events[0].Payload + " " }];
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(r,Guid.NewGuid(),"retry",plan.Manifest,default));
        Assert.Empty(s.Audit); Assert.Empty(t.Sent);
        await Assert.ThrowsAsync<ArgumentException>(() => service.PreviewAsync(r with { OrganizationId=Guid.NewGuid() }, default));
    }
    [Fact]
    public async Task Publish_failure_retains_uncertain_intent_and_retry_reuses_original_identity()
    {
        var (r,s,t) = Fixture(); var service = new CashCloseReplay(s,t); var plan = await service.PreviewAsync(r,default);
        t.Fail = true;
        var failure = await Assert.ThrowsAsync<CashCloseReplayInterruptedException>(() => service.ExecuteAsync(r,Guid.NewGuid(),"retry",plan.Manifest,default));
        Assert.Equal(s.Runs[0],failure.RunId);
        Assert.Equal(new[] { "run", "started" },s.Audit);
        t.Fail=false;
        await service.ExecuteAsync(r,Guid.NewGuid(),"retry",plan.Manifest,default);
        Assert.Equal(t.Sent[0],t.Sent[1]); Assert.Equal(2,s.Runs.Distinct().Count());
    }
    [Fact]
    public async Task Audit_failure_prevents_publication()
    {
        var (r,s,t) = Fixture(); var service = new CashCloseReplay(s,t); var plan = await service.PreviewAsync(r,default);
        s.FailAudit=true;
        await Assert.ThrowsAsync<CashCloseReplayInterruptedException>(() => service.ExecuteAsync(r,Guid.NewGuid(),"rebuild",plan.Manifest,default));
        Assert.Empty(t.Sent);
    }
    [Fact]
    public async Task Bounds_reject_truncation_unattributed_execution_and_invalid_ranges()
    {
        var (r,s,t)=Fixture(); var service=new CashCloseReplay(s,t);
        await Assert.ThrowsAsync<ArgumentException>(()=>service.PreviewAsync(r with { Limit=1001 },default));
        await Assert.ThrowsAsync<ArgumentException>(()=>service.PreviewAsync(r with { FromUtc=r.ToUtc.AddDays(-32) },default));
        await Assert.ThrowsAsync<ArgumentException>(()=>service.ExecuteAsync(r,Guid.Empty,"rebuild","",default));
        s.Events=[s.Events[0],s.Events[0]];
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.PreviewAsync(r with { Limit=1 },default));
        Assert.Empty(t.Sent);
    }
    private sealed class Store : ICashCloseReplayStore
    {
        public IReadOnlyList<CashCloseReplayEvent> Events=[]; public List<string> Audit=[]; public List<Guid> Runs=[]; public bool FailAudit;
        public Task<IReadOnlyList<CashCloseReplayEvent>> SelectAsync(CashCloseReplayRequest r,CancellationToken ct)=>Task.FromResult(Events);
        public Task StartAsync(Guid run,CashCloseReplayRequest r,Guid actor,string reason,string manifest,int count,CancellationToken ct)
        { if(FailAudit)throw new IOException(); Runs.Add(run);Audit.Add("run");return Task.CompletedTask; }
        public Task AppendAsync(Guid run,Guid id,string outcome,CancellationToken ct){Audit.Add(outcome);return Task.CompletedTask;}
    }
    private sealed class Transport : ICashCloseReplayTransport
    {
        public List<CashCloseReplayEvent> Sent=[]; public bool Fail; public Action? BeforeSend;
        public Task PublishAsync(CashCloseReplayEvent value,CancellationToken ct)
        { BeforeSend?.Invoke(); Sent.Add(value);if(Fail)throw new IOException();return Task.CompletedTask; }
    }
}
