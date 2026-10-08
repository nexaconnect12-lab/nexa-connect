using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using NexaConnect.Contracts.Reporting;
using Npgsql;
namespace NexaConnect.Infrastructure.Persistence;

/// <summary>Lease and journal persistence mechanics. Financial admission is supplied by the owning Domain.</summary>
public sealed class PostgresWindowFence(NpgsqlDataSource source)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public async Task<SourceDayFence> ExecuteAsync(SourceFenceCommand command,string actor,bool cancel,
        Func<string?,bool?> affects,Action<bool,long,long,bool> validate,CancellationToken ct)
    {
        if(command.OperationId==Guid.Empty||command.ApprovalId==Guid.Empty||command.SealId==Guid.Empty||command.ExpiresAtUtc==default
            ||string.IsNullOrWhiteSpace(actor)||actor.Length>128||actor.Any(char.IsControl))throw new ArgumentException();
        var w=command.Window;
        // PostgreSQL timestamps retain microseconds; make response/replay identity use the same precision.
        command=command with{ExpiresAtUtc=new DateTimeOffset(command.ExpiresAtUtc.UtcTicks-command.ExpiresAtUtc.UtcTicks%10,TimeSpan.Zero)};
        await using var c=await source.OpenConnectionAsync(ct);
        string key=$"financial-revision:{w.RestaurantId:D}:{w.BranchId:D}";
        await Run(c,null,"SELECT pg_advisory_lock(hashtextextended($1,0))",ct,key);
        try
        {
            await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
            await Run(c,tx,"SELECT pg_advisory_xact_lock(hashtextextended($1,0))",ct,"fence:"+w.OrganizationId+":"+command.OperationId);
            var hash=Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(command,Json)));
            await using var old=new NpgsqlCommand("SELECT command_hash,actor,cancelled,acquired_revision::text FROM source_day_fences WHERE organization_id=$1 AND operation_id=$2 FOR UPDATE",c,tx);
            Add(old,w.OrganizationId,command.OperationId);
            bool exists=false,cancelled=false;SourceFinancialRevision? retained=null;
            await using(var rows=await old.ExecuteReaderAsync(ct))if(await rows.ReadAsync(ct))
            {
                exists=true;
                if(rows.GetString(0)!=hash||!cancel&&rows.GetString(1)!=actor)throw new SnapshotOperationConflictException();
                cancelled=rows.GetBoolean(2);retained=rows.IsDBNull(3)?null:JsonSerializer.Deserialize<SourceFinancialRevision>(rows.GetString(3),Json);
            }
            if(exists)
            {
                if(cancel&&!cancelled){await Run(c,tx,"UPDATE source_day_fences SET cancelled=true WHERE organization_id=$1 AND operation_id=$2",ct,w.OrganizationId,command.OperationId);await Audit(c,tx,command,actor,"cancel",ct);cancelled=true;}
                var result=await Result(c,tx,command,cancelled,retained,ct);await tx.CommitAsync(ct);return result;
            }
            var now=await Now(c,tx,ct);
            if(!cancel&&(command.ExpiresAtUtc<=now||command.ExpiresAtUtc>now.AddMinutes(5)))throw new SnapshotOperationConflictException();
            if(!cancel)
            {
                await using var conflict=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM source_day_fences WHERE restaurant_id=$1 AND branch_id=$2 AND from_utc<$4 AND to_utc>$3 AND NOT cancelled AND expires_at_utc>clock_timestamp())",c,tx);
                Add(conflict,w.RestaurantId,w.BranchId,w.FromUtc,w.ToUtc);
                if((bool)(await conflict.ExecuteScalarAsync(ct))!)throw new SnapshotOperationConflictException();
                await using var sealedQuery=new NpgsqlCommand("SELECT payload::text FROM source_day_seals WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 AND from_utc=$4 AND to_utc=$5 AND id=$6",c,tx);
                Add(sealedQuery,w.OrganizationId,w.RestaurantId,w.BranchId,w.FromUtc,w.ToUtc,command.SealId);
                var seal=await sealedQuery.ExecuteScalarAsync(ct) is string data?JsonSerializer.Deserialize<SourceDaySeal>(data,Json):null;
                if(seal is null||seal.Window!=w||seal.SealId!=command.SealId)throw new SnapshotOperationConflictException();
                retained=await PostgresFinancialRevision.ReadAsync(w,c,tx,ct);
                long count=0,bytes=0;bool relevant=false,complete=true;
                await using var journal=new NpgsqlCommand("SELECT revision,attribution::text FROM source_financial_changes WHERE restaurant_id=$1 AND branch_id=$2 AND epoch=$3 AND revision>$4 ORDER BY revision LIMIT 10001",c,tx);
                Add(journal,w.RestaurantId,w.BranchId,seal.SourceRevision.Epoch,seal.SourceRevision.Revision);
                await using(var rows=await journal.ExecuteReaderAsync(ct))while(await rows.ReadAsync(ct))
                {
                    var journalData=rows.IsDBNull(1)?null:rows.GetString(1);bytes+=journalData is null?0:System.Text.Encoding.UTF8.GetByteCount(journalData);
                    if(++count>10000||bytes>16*1024*1024){complete=false;break;}
                    if(rows.GetInt64(0)!=seal.SourceRevision.Revision+count)complete=false;
                    relevant|=affects(journalData)!=false;
                }
                validate(retained.Epoch==seal.SourceRevision.Epoch,retained.Revision-seal.SourceRevision.Revision,count,complete&&!relevant);
            }
            await Run(c,tx,"INSERT INTO source_day_fences(organization_id,restaurant_id,branch_id,from_utc,to_utc,operation_id,approval_id,seal_id,expires_at_utc,command_hash,actor,cancelled,acquired_revision) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13::jsonb)",ct,
                w.OrganizationId,w.RestaurantId,w.BranchId,w.FromUtc,w.ToUtc,command.OperationId,command.ApprovalId,command.SealId,command.ExpiresAtUtc,hash,actor,cancel,
                retained is null?DBNull.Value:JsonSerializer.Serialize(retained,Json));
            await Audit(c,tx,command,actor,cancel?"cancel":"acquire",ct);
            var created=await Result(c,tx,command,cancel,retained,ct);await tx.CommitAsync(ct);return created;
        }
        finally{await Run(c,null,"SELECT pg_advisory_unlock(hashtextextended($1,0))",CancellationToken.None,key);}
    }
    public async Task<SourceDayFence?> ReadAsync(EndOfDayWindow w,Guid operation,CancellationToken ct)
    {
        await using var c=await source.OpenConnectionAsync(ct);
        await using var q=new NpgsqlCommand("SELECT approval_id,seal_id,expires_at_utc,cancelled,acquired_revision::text,NOT cancelled AND expires_at_utc>clock_timestamp() FROM source_day_fences WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 AND from_utc=$4 AND to_utc=$5 AND operation_id=$6",c);
        Add(q,w.OrganizationId,w.RestaurantId,w.BranchId,w.FromUtc,w.ToUtc,operation);
        await using var rows=await q.ExecuteReaderAsync(ct);if(!await rows.ReadAsync(ct))return null;
        return new(operation,w,rows.GetGuid(0),rows.GetGuid(1),rows.GetFieldValue<DateTimeOffset>(2),rows.GetBoolean(3),rows.GetBoolean(5),rows.IsDBNull(4)?null:JsonSerializer.Deserialize<SourceFinancialRevision>(rows.GetString(4),Json));
    }
    private static async Task<SourceDayFence> Result(NpgsqlConnection c,NpgsqlTransaction tx,SourceFenceCommand command,bool cancelled,SourceFinancialRevision? revision,CancellationToken ct)=>
        new(command.OperationId,command.Window,command.ApprovalId,command.SealId,command.ExpiresAtUtc,cancelled,!cancelled&&command.ExpiresAtUtc>await Now(c,tx,ct),revision);
    private static async Task<DateTimeOffset> Now(NpgsqlConnection c,NpgsqlTransaction tx,CancellationToken ct)
    {await using var q=new NpgsqlCommand("SELECT clock_timestamp()",c,tx);return new DateTimeOffset((DateTime)(await q.ExecuteScalarAsync(ct))!);}
    private static Task Audit(NpgsqlConnection c,NpgsqlTransaction tx,SourceFenceCommand command,string actor,string action,CancellationToken ct)=>Run(c,tx,
        "INSERT INTO source_day_fence_audit(organization_id,operation_id,action,actor,command) VALUES($1,$2,$3,$4,$5::jsonb)",ct,command.Window.OrganizationId,command.OperationId,action,actor,JsonSerializer.Serialize(command,Json));
    private static void Add(NpgsqlCommand q,params object[] values){foreach(var v in values)q.Parameters.AddWithValue(v);}
    private static async Task Run(NpgsqlConnection c,NpgsqlTransaction? tx,string sql,CancellationToken ct,params object[] values)
    {await using var q=new NpgsqlCommand(sql,c,tx){CommandTimeout=10};Add(q,values);await q.ExecuteNonQueryAsync(ct);}
}
