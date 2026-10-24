using System.Text.Json;
using System.Security.Cryptography;
using NexaConnect.Contracts.Reporting;
using Npgsql;
namespace NexaConnect.Infrastructure.Persistence;

/// <summary>Source-owned durable barrier persistence. Owning Domain supplies transition admission.</summary>
public sealed class PostgresDayBarrier(NpgsqlDataSource source)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public async Task<SourceBarrierProof> ExecuteAsync(SourceBarrierRequest request,Action<string?,string,bool,bool> validate,CancellationToken ct)
    {
        var command=request.Command;var fence=command.Fence;var w=fence.Window;
        if(request.DecisionId==Guid.Empty||command.SettlementId==Guid.Empty||command.OperationId==Guid.Empty||fence.OperationId==Guid.Empty||fence.SealId==Guid.Empty||fence.ApprovalId==Guid.Empty
            ||w.OrganizationId==Guid.Empty||w.RestaurantId==Guid.Empty||w.BranchId==Guid.Empty||w.FromUtc==default||w.ToUtc<=w.FromUtc||w.ToUtc-w.FromUtc>TimeSpan.FromHours(27))throw new ArgumentException();
        await using var c=await source.OpenConnectionAsync(ct);string key=$"financial-revision:{w.RestaurantId:D}:{w.BranchId:D}";
        await Run(c,null,"SELECT pg_advisory_lock(hashtextextended($1,0))",ct,key);
        try
        {
            await using var tx=await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct);
            await Run(c,tx,"SELECT pg_advisory_xact_lock(hashtextextended($1,0))",ct,"barrier:"+w.OrganizationId+":"+command.OperationId);
            var hash=Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(command,Json)));
            string? phase=null;Guid? decision=null;
            await using(var q=new NpgsqlCommand("SELECT fingerprint,phase,decision_id FROM source_day_barriers WHERE organization_id=$1 AND (id=$2 OR operation_id=$3 OR preparation_operation_id=$4) FOR UPDATE",c,tx))
            {
                Add(q,w.OrganizationId,command.SettlementId,command.OperationId,fence.OperationId);
                await using var rows=await q.ExecuteReaderAsync(ct);if(await rows.ReadAsync(ct))
                {if(rows.GetString(0)!=hash)throw new SnapshotOperationConflictException();phase=rows.GetString(1);decision=rows.IsDBNull(2)?null:rows.GetGuid(2);}
            }
            await using var lease=new NpgsqlCommand("SELECT approval_id,seal_id,from_utc,to_utc,expires_at_utc,NOT cancelled AND expires_at_utc>clock_timestamp()+interval '15 seconds',restaurant_id,branch_id FROM source_day_fences WHERE organization_id=$1 AND operation_id=$2 FOR UPDATE",c,tx);
            Add(lease,w.OrganizationId,fence.OperationId);bool active=false;
            await using(var rows=await lease.ExecuteReaderAsync(ct))
            {
                if(!await rows.ReadAsync(ct)||rows.GetGuid(0)!=fence.ApprovalId||rows.GetGuid(1)!=fence.SealId||rows.GetFieldValue<DateTimeOffset>(2)!=w.FromUtc
                    ||rows.GetFieldValue<DateTimeOffset>(3)!=w.ToUtc||rows.GetFieldValue<DateTimeOffset>(4)!=fence.ExpiresAtUtc||rows.GetGuid(6)!=w.RestaurantId||rows.GetGuid(7)!=w.BranchId)throw new SnapshotOperationConflictException();
                active=rows.GetBoolean(5);
            }
            validate(phase,request.Phase,active,request.DecisionId is {} d&&d!=Guid.Empty);
            if(phase is "committed" or "aborted")
            {
                if(phase!=request.Phase&&request.Phase!="armed"||request.Phase!="armed"&&decision!=request.DecisionId)throw new SnapshotOperationConflictException();
                var saved=await Read(c,tx,command,ct);await tx.CommitAsync(ct);return saved!;
            }
            if(phase is null)
                await Run(c,tx,"INSERT INTO source_day_barriers(organization_id,restaurant_id,branch_id,id,operation_id,preparation_operation_id,from_utc,to_utc,fingerprint,command,phase,decision_id) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10::jsonb,$11,$12)",ct,
                    w.OrganizationId,w.RestaurantId,w.BranchId,command.SettlementId,command.OperationId,fence.OperationId,w.FromUtc,w.ToUtc,hash,JsonSerializer.Serialize(command,Json),request.Phase,(object?)request.DecisionId??DBNull.Value);
            else if(phase!=request.Phase)
                await Run(c,tx,"UPDATE source_day_barriers SET phase=$3,decision_id=$4,changed_at_utc=clock_timestamp() WHERE organization_id=$1 AND id=$2",ct,w.OrganizationId,command.SettlementId,request.Phase,(object?)request.DecisionId??DBNull.Value);
            if(phase!=request.Phase)
                await Run(c,tx,"INSERT INTO source_day_barrier_audit(organization_id,barrier_id,phase,decision_id,command) VALUES($1,$2,$3,$4,$5::jsonb)",ct,w.OrganizationId,command.SettlementId,request.Phase,(object?)request.DecisionId??DBNull.Value,JsonSerializer.Serialize(command,Json));
            if(request.Phase=="aborted")
            {
                await Run(c,tx,"UPDATE source_day_fences SET cancelled=true WHERE organization_id=$1 AND operation_id=$2 AND NOT cancelled",ct,w.OrganizationId,fence.OperationId);
                await Run(c,tx,"INSERT INTO source_day_fence_audit(organization_id,operation_id,action,actor,command) VALUES($1,$2,'cancel','settlement-recovery',$3::jsonb) ON CONFLICT DO NOTHING",ct,w.OrganizationId,fence.OperationId,JsonSerializer.Serialize(fence,Json));
            }
            var proof=await Read(c,tx,command,ct);await tx.CommitAsync(ct);return proof!;
        }
        finally{await Run(c,null,"SELECT pg_advisory_unlock(hashtextextended($1,0))",CancellationToken.None,key);}
    }
    public async Task<SourceBarrierProof?> ReadAsync(EndOfDayWindow w,Guid id,CancellationToken ct)
    {
        await using var c=await source.OpenConnectionAsync(ct);await using var q=new NpgsqlCommand("SELECT command::text FROM source_day_barriers WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 AND from_utc=$4 AND to_utc=$5 AND id=$6",c);
        Add(q,w.OrganizationId,w.RestaurantId,w.BranchId,w.FromUtc,w.ToUtc,id);
        var json=await q.ExecuteScalarAsync(ct) as string;if(json is null)return null;
        return await Read(c,null,JsonSerializer.Deserialize<SourceBarrierCommand>(json,Json)!,ct);
    }
    private static async Task<SourceBarrierProof?> Read(NpgsqlConnection c,NpgsqlTransaction? tx,SourceBarrierCommand command,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand("SELECT phase,decision_id,changed_at_utc,(SELECT count(*) FROM source_late_work_links l WHERE l.organization_id=b.organization_id AND l.barrier_id=b.id) FROM source_day_barriers b WHERE organization_id=$1 AND id=$2",c,tx);Add(q,command.Fence.Window.OrganizationId,command.SettlementId);
        await using var rows=await q.ExecuteReaderAsync(ct);return await rows.ReadAsync(ct)?new(command,rows.GetString(0),rows.IsDBNull(1)?null:rows.GetGuid(1),rows.GetFieldValue<DateTimeOffset>(2),rows.GetInt64(3)):null;
    }
    private static void Add(NpgsqlCommand q,params object[] values){foreach(var v in values)q.Parameters.AddWithValue(v);}
    private static async Task Run(NpgsqlConnection c,NpgsqlTransaction? tx,string sql,CancellationToken ct,params object[] values)
    {await using var q=new NpgsqlCommand(sql,c,tx){CommandTimeout=10};Add(q,values);await q.ExecuteNonQueryAsync(ct);}
}
