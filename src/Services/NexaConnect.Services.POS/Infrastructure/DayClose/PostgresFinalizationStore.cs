using System.Security.Cryptography;
using System.Text.Json;
using NexaConnect.Services.POS.Application.DayClose;
using NexaConnect.Services.POS.Domain.DayClose;
using Npgsql;
namespace NexaConnect.Services.POS.Infrastructure.DayClose;

public sealed class PostgresFinalizationStore(NpgsqlDataSource source):IFinalizationStore
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public async Task<string?> SettlementStatusAsync(DayIdentity day,CancellationToken ct)
    {
        await using var q=source.CreateCommand("SELECT state->>'status' FROM branch_day_settlements WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 AND business_date=$4 AND state->>'status'<>'aborted'");
        Scope(q,day);return await q.ExecuteScalarAsync(ct) as string;
    }
    public async Task<FinalizationState?> ReadAsync(DayIdentity day,CancellationToken ct)
    {await using var c=await source.OpenConnectionAsync(ct);return await Load(c,null,day,false,ct);}
    public async Task<FinalizationState> BeginAsync(DayIdentity day,FinalizationCommand command,ApprovalView approval,PreparationActor actor,DateTimeOffset now,CancellationToken ct)
    {
        FinalizationPreparation.Validate(command,day);ValidateActor(actor);
        await using var c=await source.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);
        await Run(c,tx,"SELECT pg_advisory_xact_lock(hashtextextended($1,0))",ct,"finalization:"+day.OrganizationId+":"+command.OperationId);
        bool bound=await ApprovalMatches(c,tx,day,approval,now,ct); // Global lock order: seal, approval, preparation.
        var state=await Load(c,tx,day,true,ct);
        var hash=Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{day,command,actor.Subject},Json)));
        await using var operation=new NpgsqlCommand("SELECT fingerprint FROM branch_day_finalization_operations WHERE organization_id=$1 AND operation_id=$2",c,tx);Add(operation,day.OrganizationId,command.OperationId);
        var existing=await operation.ExecuteScalarAsync(ct) as string;
        if(existing is not null)
        {
            if(existing!=hash||state?.Command.OperationId!=command.OperationId)throw new DayCloseConflictException("finalization_operation_conflict");
            if(state.Status is "cancelled" or "expired" or "prepared"){await tx.CommitAsync(ct);return state with{ClaimId=null};}
            if(state.Status=="cancelling"||state.ClaimUntilUtc>now)throw new DayCloseConflictException("finalization_in_progress");
        }
        else
        {
            if((state?.Version??0)!=command.ExpectedVersion)throw new DayCloseConflictException("finalization_version_changed");
            if(state is not null&&state.ExpiresAtUtc>now&&state.Status!="cancelled")throw new DayCloseConflictException("active_finalization_exists");
        }
        if(!bound||approval.Status!="approved"||approval.Decision?.ApprovalId!=command.ApprovalId||approval.Version!=command.ReviewedApprovalVersion)
            throw new DayCloseConflictException("reviewed_approval_changed");
        var claim=Guid.NewGuid();
        if(existing is null)
        {
            state=new(day,(state?.Version??0)+1,"preparing",command,approval.Decision!,DateTimeOffset.FromUnixTimeMilliseconds(now.AddMinutes(4).ToUnixTimeMilliseconds()),actor.Subject,claim,now.AddSeconds(30),[],[],null);
            await Run(c,tx,"INSERT INTO branch_day_finalization_operations(organization_id,operation_id,fingerprint,command) VALUES($1,$2,$3,$4::jsonb)",ct,day.OrganizationId,command.OperationId,hash,JsonSerializer.Serialize(command,Json));
        }
        else
        {
            if(state!.ExpiresAtUtc<=now.AddSeconds(15))throw new DayCloseConflictException("finalization_expired");
            state=state with{Version=checked(state.Version+1),Status="preparing",ClaimId=claim,ClaimUntilUtc=now.AddSeconds(30),ValidatedAtUtc=null,Blockers=[]};
        }
        await Save(c,tx,state!,actor,"prepare",ct);await tx.CommitAsync(ct);return state!;
    }
    public async Task<FinalizationState> CancelAsync(DayIdentity day,Guid operation,PreparationActor actor,DateTimeOffset now,CancellationToken ct)
    {
        ValidateActor(actor);await using var c=await source.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);
        await ApprovalMatches(c,tx,day,null,now,ct);
        var state=await Load(c,tx,day,true,ct)??throw new DayCloseConflictException("finalization_missing");
        if(state.Command.OperationId!=operation)throw new DayCloseConflictException("finalization_operation_changed");
        if(state.Status=="cancelled"){await tx.CommitAsync(ct);return state with{ClaimId=null};}
        if(state.Status=="cancelling"&&state.ClaimUntilUtc>now)throw new DayCloseConflictException("cancellation_in_progress");
        state=state with{Version=checked(state.Version+1),Status="cancelling",ClaimId=Guid.NewGuid(),ClaimUntilUtc=now.AddSeconds(30),ValidatedAtUtc=null};
        await Save(c,tx,state,actor,"cancel",ct);await tx.CommitAsync(ct);return state;
    }
    public async Task<FinalizationState> CompleteAsync(FinalizationState claimed,ApprovalView? approval,FinalizationFence[] sources,PreparationActor actor,DateTimeOffset now,bool cancel,CancellationToken ct)
    {
        await using var c=await source.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);
        bool valid=await ApprovalMatches(c,tx,claimed.Identity,approval,now,ct);
        var state=await Load(c,tx,claimed.Identity,true,ct)??throw new DayCloseConflictException("finalization_missing");
        if(state.Command.OperationId!=claimed.Command.OperationId)throw new DayCloseConflictException("finalization_operation_changed");
        state=FinalizationPreparation.Complete(state,claimed.ClaimId!.Value,valid?approval?.Decision:null,approval?.Version??0,approval?.Status??"unverified",sources,now,cancel);
        await Save(c,tx,state,actor,cancel?"cancel_result":"prepare_result",ct);await tx.CommitAsync(ct);return state;
    }
    public async Task<FinalizationState> ObserveAsync(FinalizationState observed,ApprovalView? approval,FinalizationFence[] sources,PreparationActor actor,DateTimeOffset now,CancellationToken ct)
    {
        await using var c=await source.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);
        bool bound=await ApprovalMatches(c,tx,observed.Identity,approval,now,ct);
        var state=await Load(c,tx,observed.Identity,true,ct)??throw new DayCloseConflictException("finalization_missing");
        if(state.Command.OperationId!=observed.Command.OperationId||state.Version!=observed.Version)throw new DayCloseConflictException("finalization_version_changed");
        var status=state.Status;
        if(status is not("cancelled" or "expired" or "cancelling"))
        {
            if(state.ExpiresAtUtc<=now.AddSeconds(15))status="expired";
            else if(status=="prepared"&&!bound||status=="prepared"&&!FinalizationPreparation.HasLiveProof(state,approval?.Decision,approval?.Version??0,approval?.Status??"unverified",sources,now))status="blocked";
        }
        if(status!=state.Status)
        {
            state=state with{Version=checked(state.Version+1),Status=status,ValidatedAtUtc=null,Sources=[..sources],Blockers=[status=="expired"?"source_fences_expired":"finalization_proof_unavailable"]};
            await Save(c,tx,state,actor,"observe",ct);
        }
        await tx.CommitAsync(ct);
        return state with{Sources=[..sources],ValidatedAtUtc=status=="prepared"?now:null};
    }
    internal static async Task<bool> ApprovalMatches(NpgsqlConnection c,NpgsqlTransaction tx,DayIdentity day,ApprovalView? approval,DateTimeOffset now,CancellationToken ct)
    {
        await using var seal=new NpgsqlCommand("SELECT version,state->>'status' FROM branch_day_seals WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 AND business_date=$4 FOR UPDATE",c,tx);Scope(seal,day);
        long sealVersion=-1;string? sealStatus=null;
        await using(var rows=await seal.ExecuteReaderAsync(ct))if(await rows.ReadAsync(ct)){sealVersion=rows.GetInt64(0);sealStatus=rows.GetString(1);}
        await using var q=new NpgsqlCommand("SELECT version,state->>'status',state->'decision'->>'approvalId' FROM branch_day_approvals WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 AND business_date=$4 FOR UPDATE",c,tx);Scope(q,day);
        await using var read=await q.ExecuteReaderAsync(ct);
        return await read.ReadAsync(ct)&&approval?.Identity==day&&approval.Status=="approved"&&approval.ValidatedAtUtc is {} checkedAt&&checkedAt<=now&&now-checkedAt<=TimeSpan.FromMinutes(1)
            &&approval.Decision?.Snapshot.Seals is not null&&sealStatus=="ready_for_review"&&sealVersion==approval.Decision.SealVersion
            &&read.GetInt64(0)==approval.Version&&read.GetString(1)=="approved"&&read.GetString(2)==approval.Decision.ApprovalId.ToString("D");
    }
    private static async Task<FinalizationState?> Load(NpgsqlConnection c,NpgsqlTransaction? tx,DayIdentity day,bool locked,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand("SELECT state::text FROM branch_day_finalization_preparations WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 AND business_date=$4"+(locked?" FOR UPDATE":""),c,tx);Scope(q,day);
        var text=await q.ExecuteScalarAsync(ct) as string;var state=text is null?null:JsonSerializer.Deserialize<FinalizationState>(text,Json);
        if(state is not null&&state.Identity!=day)throw new InvalidOperationException();return state;
    }
    private static async Task Save(NpgsqlConnection c,NpgsqlTransaction tx,FinalizationState state,PreparationActor actor,string action,CancellationToken ct)
    {
        ValidateActor(actor);var day=state.Identity;var text=JsonSerializer.Serialize(state,Json);
        await Run(c,tx,"INSERT INTO branch_day_finalization_preparations(organization_id,restaurant_id,branch_id,business_date,version,state) VALUES($1,$2,$3,$4,$5,$6::jsonb) ON CONFLICT(organization_id,branch_id,business_date) DO UPDATE SET version=EXCLUDED.version,state=EXCLUDED.state",ct,day.OrganizationId,day.RestaurantId,day.BranchId,day.BusinessDate,state.Version,text);
        await Run(c,tx,"INSERT INTO branch_day_finalization_audit(organization_id,branch_id,business_date,version,action,subject_id,authorization_decision_id,state) VALUES($1,$2,$3,$4,$5,$6,$7,$8::jsonb)",ct,day.OrganizationId,day.BranchId,day.BusinessDate,state.Version,action,actor.Subject,actor.DecisionId,text);
    }
    private static void ValidateActor(PreparationActor actor){if(actor.DecisionId==Guid.Empty||string.IsNullOrWhiteSpace(actor.Subject)||actor.Subject.Length>128||actor.Subject.Any(char.IsControl))throw new UnauthorizedAccessException();}
    private static void Scope(NpgsqlCommand q,DayIdentity day)=>Add(q,day.OrganizationId,day.RestaurantId,day.BranchId,day.BusinessDate);
    private static void Add(NpgsqlCommand q,params object[] values){foreach(var value in values)q.Parameters.AddWithValue(value);}
    private static async Task Run(NpgsqlConnection c,NpgsqlTransaction tx,string sql,CancellationToken ct,params object[] values)
    {await using var q=new NpgsqlCommand(sql,c,tx);Add(q,values);await q.ExecuteNonQueryAsync(ct);}
}
