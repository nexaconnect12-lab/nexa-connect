using System.Data;
using System.Text.Json;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.POS.Application.DayClose;
using NexaConnect.Services.POS.Infrastructure.DayClose;
using Npgsql;
namespace NexaConnect.Services.POS.Infrastructure.Persistence;

public sealed class PostgresSettlementReplayStore(NpgsqlDataSource source):ISettlementReplayStore
{
    public async Task<IReadOnlyList<SettlementReplayEvent>> SelectAsync(SettlementReplayRequest request,CancellationToken ct)
    {
        request.Validate();var selected=await new PostgresSettlementInventoryStore(source).ReadRetainedAsync(
            new(request.OrganizationId,request.BranchId,request.FromDate,request.ToDate),request.RestaurantId,ct);
        return Events(request,selected);
    }
    private static SettlementReplayEvent[] Events(SettlementReplayRequest request,(SettlementManifest Manifest,IReadOnlyDictionary<Guid,string> Payloads) selected)
    {
        var events=selected.Manifest.Events.Select(p=>p.V1 is{} v1?new SettlementReplayEvent(v1.EventId,1,selected.Payloads[v1.EventId])
            :new(p.V2!.EventId,2,selected.Payloads[p.V2.EventId])).OrderBy(e=>e.Id).ToArray();
        SettlementReplay.Plan(request,events);return events;
    }
    public async Task<IReadOnlyList<SettlementReplayEvent>> BeginAsync(Guid runId,SettlementReplayRequest request,Guid operatorId,string reason,string expectedManifest,CancellationToken ct)
    {
        SettlementReplay.ValidateExecution(request,operatorId,reason,expectedManifest);if(runId==Guid.Empty)throw new ArgumentException();
        await using var c=await source.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
        var selected=await PostgresSettlementInventoryStore.QueryRetainedAsync(new(request.OrganizationId,request.BranchId,request.FromDate,request.ToDate),request.RestaurantId,c,tx,ct);
        var events=Events(request,selected);var plan=SettlementReplay.Plan(request,events);
        if(plan.Manifest!=expectedManifest)throw new SettlementReplaySelectionChangedException();
        var identities=JsonSerializer.Serialize(events.Select(e=>new{e.Id,e.ContractVersion,PayloadHash=SettlementReplay.PayloadHash(e)}),new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await using var q=new NpgsqlCommand("""
            INSERT INTO settlement_replay_runs(id,organization_id,restaurant_id,branch_id,from_date,to_date,operator_id,reason,manifest,event_count,selection_limit,selected_events)
            VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12::jsonb)
            """,c,tx);
        foreach(var v in new object[]{runId,request.OrganizationId,request.RestaurantId,request.BranchId,request.FromDate,request.ToDate,operatorId,reason,plan.Manifest,plan.Count,request.Limit,identities})q.Parameters.AddWithValue(v);
        await q.ExecuteNonQueryAsync(ct);await tx.CommitAsync(ct);return events;
    }
    public async Task AppendAsync(Guid runId,Guid eventId,string outcome,CancellationToken ct)
    {
        if(runId==Guid.Empty||eventId==Guid.Empty||outcome is not("started" or "confirmed"))throw new ArgumentException();
        await using var q=source.CreateCommand("INSERT INTO settlement_replay_attempts(run_id,event_id,outcome) VALUES($1,$2,$3)");
        q.Parameters.AddWithValue(runId);q.Parameters.AddWithValue(eventId);q.Parameters.AddWithValue(outcome);await q.ExecuteNonQueryAsync(ct);
    }
}
