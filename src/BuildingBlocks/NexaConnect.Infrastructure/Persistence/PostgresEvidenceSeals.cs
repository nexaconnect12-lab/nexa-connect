using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using NexaConnect.Contracts.Reporting;
using Npgsql;

namespace NexaConnect.Infrastructure.Persistence;

/// <summary>Immutable evidence storage and locking mechanics. Owning Domain supplies the seal policy.</summary>
public sealed class PostgresEvidenceSeals<T>(NpgsqlDataSource source)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public async Task<SourceDaySeal> RetainAsync(SourceSealCommand command,string actor,
        Action<SourceCutoff<T>,SourceFinancialRevision> validate,CancellationToken ct)
    {
        if(command.OperationId==Guid.Empty || command.ManifestId==Guid.Empty || command.ExpectedRevision is null
            || command.ExpectedRevision.Epoch==Guid.Empty || command.ExpectedRevision.Revision<0
            || string.IsNullOrWhiteSpace(actor) || actor.Length>128 || actor.Any(char.IsControl))throw new ArgumentException();
        await using var c=await source.OpenConnectionAsync(ct);
        string operation="seal:"+command.Window.OrganizationId+":"+command.OperationId;
        string branch=$"financial-revision:{command.Window.RestaurantId:D}:{command.Window.BranchId:D}";
        await Lock(c,operation,ct);
        try
        {
            await Lock(c,branch,ct);
            try
            {
                // Session locks precede the snapshot, including when the branch revision is still zero.
                await using var t=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
                var fingerprint=Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{command,actor},Json)));
                await using(var existing=new NpgsqlCommand("SELECT fingerprint,payload::text FROM source_day_seals WHERE organization_id=$1 AND operation_id=$2",c,t))
                {
                    Add(existing,command.Window.OrganizationId,command.OperationId);
                    await using var rows=await existing.ExecuteReaderAsync(ct);
                    if(await rows.ReadAsync(ct))
                    {
                        if(rows.GetString(0)!=fingerprint)throw new SnapshotOperationConflictException();
                        return JsonSerializer.Deserialize<SourceDaySeal>(rows.GetString(1),Json)!;
                    }
                }
                var manifest=await Manifest(c,t,command.Window,command.ManifestId,ct)??throw new SnapshotOperationConflictException();
                var current=await PostgresFinancialRevision.ReadAsync(command.Window,c,t,ct);
                validate(manifest,current);
                var seal=new SourceDaySeal(Guid.NewGuid(),command.OperationId,command.Window,manifest.ManifestId,current,DateTimeOffset.UtcNow);
                await using var insert=new NpgsqlCommand("""
                    INSERT INTO source_day_seals(organization_id,restaurant_id,branch_id,from_utc,to_utc,id,operation_id,manifest_id,fingerprint,actor,payload)
                    VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11::jsonb)
                    """,c,t);
                Window(insert,command.Window);Add(insert,seal.SealId,command.OperationId,seal.ManifestId,fingerprint,actor,JsonSerializer.Serialize(seal,Json));
                await insert.ExecuteNonQueryAsync(ct);await t.CommitAsync(ct);return seal;
            }
            finally{await Unlock(c,branch);}
        }
        finally{await Unlock(c,operation);}
    }
    public async Task<SourceSealRead<T>?> ReadAsync(EndOfDayWindow w,Guid id,CancellationToken ct)
    {
        await using var c=await source.OpenConnectionAsync(ct);
        await using var t=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
        await using var query=new NpgsqlCommand("SELECT payload::text FROM source_day_seals WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 AND from_utc=$4 AND to_utc=$5 AND id=$6",c,t);
        Window(query,w);Add(query,id);
        if(await query.ExecuteScalarAsync(ct) is not string json)return null;
        var seal=JsonSerializer.Deserialize<SourceDaySeal>(json,Json)??throw new InvalidOperationException();
        var manifest=await Manifest(c,t,w,seal.ManifestId,ct)??throw new InvalidOperationException();
        if(seal.Window!=w || seal.SealId!=id || seal.SourceRevision!=manifest.SourceRevision)throw new InvalidOperationException();
        var revision=await PostgresFinancialRevision.ReadAsync(w,c,t,ct);
        long pending=revision.Epoch==seal.SourceRevision.Epoch && revision.Revision>=seal.SourceRevision.Revision
            ?revision.Revision-seal.SourceRevision.Revision:-1;
        await using var count=new NpgsqlCommand("SELECT count(*) FROM source_financial_changes WHERE restaurant_id=$1 AND branch_id=$2 AND epoch=$3 AND revision>$4",c,t);
        Add(count,w.RestaurantId,w.BranchId,seal.SourceRevision.Epoch,seal.SourceRevision.Revision);
        long observed=(long)(await count.ExecuteScalarAsync(ct))!;
        await using var changes=new NpgsqlCommand("SELECT revision,recorded_at_utc FROM source_financial_changes WHERE restaurant_id=$1 AND branch_id=$2 AND epoch=$3 AND revision>$4 ORDER BY revision LIMIT 256",c,t);
        Add(changes,w.RestaurantId,w.BranchId,seal.SourceRevision.Epoch,seal.SourceRevision.Revision);
        var entries=new List<SourceSealChange>();
        await using(var rows=await changes.ExecuteReaderAsync(ct))while(await rows.ReadAsync(ct))entries.Add(new(rows.GetInt64(0),rows.GetFieldValue<DateTimeOffset>(1)));
        await t.CommitAsync(ct);
        return new(seal,manifest,Math.Max(0,pending),pending>=0&&observed==pending,entries,observed>entries.Count);
    }
    private static async Task<SourceCutoff<T>?> Manifest(NpgsqlConnection c,NpgsqlTransaction t,EndOfDayWindow w,Guid id,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand("SELECT payload::text FROM source_day_cutoffs WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 AND from_utc=$4 AND to_utc=$5 AND id=$6",c,t);
        Window(q,w);Add(q,id);
        return await q.ExecuteScalarAsync(ct) is string json?JsonSerializer.Deserialize<SourceCutoff<T>>(json,Json):null;
    }
    private static void Window(NpgsqlCommand q,EndOfDayWindow w)=>Add(q,w.OrganizationId,w.RestaurantId,w.BranchId,w.FromUtc.ToUniversalTime(),w.ToUtc.ToUniversalTime());
    private static void Add(NpgsqlCommand q,params object[] values){foreach(var value in values)q.Parameters.AddWithValue(value);}
    private static async Task Lock(NpgsqlConnection c,string key,CancellationToken ct)
    {await using var q=new NpgsqlCommand("SELECT pg_advisory_lock(hashtextextended($1,0))",c);Add(q,key);await q.ExecuteNonQueryAsync(ct);}
    private static async Task Unlock(NpgsqlConnection c,string key)
    {await using var q=new NpgsqlCommand("SELECT pg_advisory_unlock(hashtextextended($1,0))",c){CommandTimeout=5};Add(q,key);await q.ExecuteNonQueryAsync(CancellationToken.None);}
}
