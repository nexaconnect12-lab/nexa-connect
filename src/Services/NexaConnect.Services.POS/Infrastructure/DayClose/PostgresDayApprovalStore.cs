using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;
using NexaConnect.Services.POS.Application.DayClose;
using NexaConnect.Services.POS.Domain.DayClose;
namespace NexaConnect.Services.POS.Infrastructure.DayClose;

public sealed class PostgresDayApprovalStore(NpgsqlDataSource source):IDayApprovalStore
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public async Task<DayApprovalDecision?> ReplayAsync(DayIdentity day,ApprovalCommand command,string subject,CancellationToken ct)
    {
        await using var c=await source.OpenConnectionAsync(ct);
        return await Replay(c,null,day,command,subject,ct);
    }
    public async Task<ApprovalView> ApproveAsync(DayIdentity day,ApprovalCommand command,DayEvidence proof,PreparationActor actor,DateTimeOffset now,CancellationToken ct)
    {
        await using var c=await source.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);
        await Execute(c,tx,"SELECT pg_advisory_xact_lock(hashtextextended($1,0))",ct,"approval:"+day.OrganizationId+":"+command.OperationId);
        var sealedDay=await Seal(c,tx,day,ct)??throw new DayCloseConflictException("reviewed_seal_not_ready");
        var state=await Load(c,tx,day,now,ct);
        var replay=await Replay(c,tx,day,command,actor.Subject,ct);
        if(replay is not null)
        {
            var next=BranchDayApproval.Observe(state,sealedDay,proof,now);
            if(next!=state)await Save(c,tx,day,next,actor,next.Status=="superseded"?"supersede":"validate",ct);
            var validated=next.Status=="approved"?proof.ObservedAtUtc:(DateTimeOffset?)null;
            var existing=await View(c,tx,day,next,sealedDay,validated,ct);
            await tx.CommitAsync(ct);return existing with{OperationDecision=replay};
        }
        if(actor.DecisionId==Guid.Empty)throw new UnauthorizedAccessException();
        state=BranchDayApproval.Approve(state,sealedDay,command,actor.Subject,proof,now);
        var decision=state.Decision!;
        await Execute(c,tx,"INSERT INTO branch_day_approval_decisions(organization_id,restaurant_id,branch_id,business_date,id,operation_id,approval_version,seal_version,fingerprint,payload) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10::jsonb)",ct,
            day.OrganizationId,day.RestaurantId,day.BranchId,day.BusinessDate,decision.ApprovalId,command.OperationId,decision.ApprovalVersion,decision.SealVersion,Fingerprint(day,command,actor.Subject),JsonSerializer.Serialize(decision,Json));
        await Save(c,tx,day,state,actor,"approve",ct);
        var view=await View(c,tx,day,state,sealedDay,proof.ObservedAtUtc,ct);
        await tx.CommitAsync(ct);return view with{OperationDecision=decision};
    }
    public async Task<ApprovalView> ObserveAsync(DayIdentity day,long? observedSealVersion,DayEvidence? proof,PreparationActor actor,DateTimeOffset now,CancellationToken ct)
    {
        await using var c=await source.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);
        var sealedDay=await Seal(c,tx,day,ct);
        if(sealedDay is null){await tx.CommitAsync(ct);return new(day,0,"not_approved",null,[],false,null,false,null,null,"not_approved");}
        var state=await Load(c,tx,day,now,ct);
        if(sealedDay?.Version!=observedSealVersion)proof=null;
        var next=BranchDayApproval.Observe(state,sealedDay,proof,now);
        if(next!=state)await Save(c,tx,day,next,actor,next.Status=="superseded"?"supersede":"validate",ct);
        DateTimeOffset? validated=sealedDay?.Status=="ready_for_review" && proof is not null && proof.IsValid(now) && now-proof.ObservedAtUtc<=TimeSpan.FromMinutes(1) && proof.Blockers().Length==0
            && sealedDay.Snapshot!.SameEvidence(proof)?proof.ObservedAtUtc:null;
        var view=await View(c,tx,day,next,sealedDay,validated,ct);await tx.CommitAsync(ct);return view;
    }
    // Called only after the seal row has been locked; status projection and audit share its transaction.
    internal static async Task ObserveSealChange(NpgsqlConnection c,NpgsqlTransaction tx,PreparationState sealedDay,PreparationActor actor,CancellationToken ct)
    {
        var state=await Existing(c,tx,sealedDay.Identity,ct);
        if(state is null)return;
        var next=BranchDayApproval.Observe(state,sealedDay,null,sealedDay.UpdatedAtUtc);
        if(next!=state)await Save(c,tx,sealedDay.Identity,next,actor,next.Status=="superseded"?"supersede":"validate",ct);
    }
    private static async Task<DayApprovalState> Load(NpgsqlConnection c,NpgsqlTransaction tx,DayIdentity day,DateTimeOffset now,CancellationToken ct)
    {
        var initial=new DayApprovalState(0,"not_approved",null,"not_approved",now);
        await Execute(c,tx,"INSERT INTO branch_day_approvals(organization_id,restaurant_id,branch_id,business_date,version,state) VALUES($1,$2,$3,$4,0,$5::jsonb) ON CONFLICT DO NOTHING",ct,
            day.OrganizationId,day.RestaurantId,day.BranchId,day.BusinessDate,JsonSerializer.Serialize(initial,Json));
        return await Existing(c,tx,day,ct)??throw new InvalidOperationException();
    }
    private static async Task<DayApprovalState?> Existing(NpgsqlConnection c,NpgsqlTransaction tx,DayIdentity day,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand("SELECT state::text FROM branch_day_approvals WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 AND business_date=$4 FOR UPDATE",c,tx);Scope(q,day);
        var text=await q.ExecuteScalarAsync(ct) as string;
        var state=text is null?null:JsonSerializer.Deserialize<DayApprovalState>(text,Json);
        if(state?.Decision is { } decision)
        {
            await using var ledger=new NpgsqlCommand("SELECT payload::text FROM branch_day_approval_decisions WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 AND business_date=$4 AND id=$5",c,tx);
            Scope(ledger,day);Add(ledger,decision.ApprovalId);
            var payload=await ledger.ExecuteScalarAsync(ct) as string??throw new InvalidOperationException();
            var original=JsonSerializer.Deserialize<DayApprovalDecision>(payload,Json)??throw new InvalidOperationException();
            if(original.Identity!=day || original.ApprovalVersion>state.Version)throw new InvalidOperationException();
            state=state with{Decision=original};
        }
        return state;
    }
    private static async Task<PreparationState?> Seal(NpgsqlConnection c,NpgsqlTransaction tx,DayIdentity day,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand("SELECT state::text FROM branch_day_seals WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 AND business_date=$4 FOR UPDATE",c,tx);Scope(q,day);
        var text=await q.ExecuteScalarAsync(ct) as string;var state=text is null?null:JsonSerializer.Deserialize<PreparationState>(text,Json);
        if(state is not null && state.Identity!=day)throw new InvalidOperationException();return state;
    }
    private static async Task<DayApprovalDecision?> Replay(NpgsqlConnection c,NpgsqlTransaction? tx,DayIdentity day,ApprovalCommand command,string subject,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand("SELECT fingerprint,payload::text FROM branch_day_approval_decisions WHERE organization_id=$1 AND operation_id=$2",c,tx);Add(q,day.OrganizationId,command.OperationId);
        await using var rows=await q.ExecuteReaderAsync(ct);
        if(!await rows.ReadAsync(ct))return null;
        if(rows.GetString(0)!=Fingerprint(day,command,subject))throw new DayCloseConflictException("approval_operation_conflict");
        var result=JsonSerializer.Deserialize<DayApprovalDecision>(rows.GetString(1),Json)??throw new InvalidOperationException();
        if(result.Identity!=day)throw new InvalidOperationException();return result;
    }
    private static string Fingerprint(DayIdentity day,ApprovalCommand command,string subject)=>Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{day,command,subject},Json)));
    private static async Task Save(NpgsqlConnection c,NpgsqlTransaction tx,DayIdentity day,DayApprovalState state,PreparationActor actor,string action,CancellationToken ct)
    {
        if(actor.DecisionId==Guid.Empty)throw new UnauthorizedAccessException();
        var text=JsonSerializer.Serialize(state,Json);
        await Execute(c,tx,"UPDATE branch_day_approvals SET version=$5,state=$6::jsonb WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 AND business_date=$4",ct,
            day.OrganizationId,day.RestaurantId,day.BranchId,day.BusinessDate,state.Version,text);
        await Execute(c,tx,"INSERT INTO branch_day_approval_audit(organization_id,branch_id,business_date,version,action,subject_id,authorization_decision_id,occurred_at_utc,state) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9::jsonb)",ct,
            day.OrganizationId,day.BranchId,day.BusinessDate,state.Version,action,actor.Subject,actor.DecisionId,state.UpdatedAtUtc,text);
    }
    private static async Task<ApprovalView> View(NpgsqlConnection c,NpgsqlTransaction tx,DayIdentity day,DayApprovalState state,PreparationState? sealedDay,DateTimeOffset? validated,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand("SELECT payload::text FROM branch_day_approval_decisions WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 AND business_date=$4 ORDER BY approval_version DESC LIMIT 21",c,tx);Scope(q,day);
        var history=new List<DayApprovalDecision>();
        await using(var rows=await q.ExecuteReaderAsync(ct))while(await rows.ReadAsync(ct))history.Add(JsonSerializer.Deserialize<DayApprovalDecision>(rows.GetString(0),Json)!);
        var snapshot=validated is not null?sealedDay?.Snapshot:null;
        return new(day,state.Version,state.Status,state.Decision,history.Take(20).ToArray(),history.Count>20,validated,false,
            snapshot is null?null:sealedDay?.Version,snapshot,state.Reason);
    }
    private static void Scope(NpgsqlCommand q,DayIdentity day)=>Add(q,day.OrganizationId,day.RestaurantId,day.BranchId,day.BusinessDate);
    private static void Add(NpgsqlCommand q,params object[] values){foreach(var v in values)q.Parameters.AddWithValue(v);}
    private static async Task Execute(NpgsqlConnection c,NpgsqlTransaction tx,string sql,CancellationToken ct,params object[] values)
    {await using var q=new NpgsqlCommand(sql,c,tx);Add(q,values);await q.ExecuteNonQueryAsync(ct);}
}
