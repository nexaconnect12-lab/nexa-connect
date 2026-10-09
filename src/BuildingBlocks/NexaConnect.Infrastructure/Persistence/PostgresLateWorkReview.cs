using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NexaConnect.Contracts.Reporting;
using Npgsql;

namespace NexaConnect.Infrastructure.Persistence;

/// <summary>Owning-schema query and append mechanics. Sources supply safe projection and Domain decision policy.</summary>
public sealed class PostgresLateWorkReview(NpgsqlDataSource source)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private const string Membership="b.organization_id=$1 AND b.restaurant_id=$2 AND b.branch_id=$3 AND b.from_utc=$4 AND b.to_utc=$5 AND b.id=$6 AND b.phase='committed'";
    private const string Rows=" FROM source_day_barriers b JOIN source_late_work_links l ON l.organization_id=b.organization_id AND l.barrier_id=b.id JOIN source_late_work w ON w.organization_id=l.organization_id AND w.event_type=l.event_type AND w.event_id=l.event_id ";
    private const string Latest=" LEFT JOIN LATERAL(SELECT version,status FROM source_late_work_reviews r WHERE r.organization_id=b.organization_id AND r.barrier_id=b.id AND r.work_id=w.work_id ORDER BY version DESC LIMIT 1) r ON true ";
    public async Task<LateWorkPage> ListAsync(LateWorkScope scope,string? cursor,int limit,bool canReview,Func<string,string,DateTimeOffset,long,string,LateWorkItem> project,CancellationToken ct)
    {
        if(limit is <1 or >50)throw new ArgumentException();
        DateTimeOffset after=DateTimeOffset.MinValue;Guid afterId=Guid.Empty;
        if(cursor is not null)
        {
            try{if(cursor.Length>200)throw new FormatException();var parts=Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
                if(parts.Length!=2||!DateTimeOffset.TryParseExact(parts[0],"O",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out after)||!Guid.TryParse(parts[1],out afterId))throw new FormatException();}
            catch(FormatException){throw new ArgumentException("Invalid cursor.");}
            after=after.ToUniversalTime();
        }
        await using var q=source.CreateCommand("SELECT w.work_id,w.event_type,w.payload::text,w.received_at_utc,COALESCE(r.version,0),COALESCE(r.status,'pending_review')"+Rows+Latest+" WHERE "+Membership+" AND (w.received_at_utc,w.work_id)>($7,$8) ORDER BY w.received_at_utc,w.work_id LIMIT $9");
        Scope(q,scope);Add(q,after,afterId,limit+1);await using var rows=await q.ExecuteReaderAsync(ct);var result=new List<LateWorkItem>();
        while(await rows.ReadAsync(ct))result.Add(Project(rows,project));
        string? next=null;if(result.Count>limit){result.RemoveAt(limit);var last=result[^1];next=Convert.ToBase64String(Encoding.UTF8.GetBytes(last.ReceivedAtUtc.ToString("O")+"|"+last.WorkId.ToString("D")));}
        return new(scope,result.ToArray(),next,canReview);
    }
    public async Task<LateWorkDetail?> ReadAsync(LateWorkScope scope,Guid id,bool canReview,Func<string,string,DateTimeOffset,long,string,LateWorkItem> project,CancellationToken ct)
    {
        await using var c=await source.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct);
        var detail=await Read(c,tx,scope,id,canReview,project,ct);await tx.CommitAsync(ct);return detail;
    }
    public async Task<LateReviewResult> ReviewAsync(LateWorkScope scope,LateReviewCommand command,string subject,Guid authorizationId,
        Func<long,long,string,string,(long Version,string Status)> decide,Func<string,string,DateTimeOffset,long,string,LateWorkItem> project,CancellationToken ct)
    {
        if(command.WorkId==Guid.Empty||command.OperationId==Guid.Empty||authorizationId==Guid.Empty||string.IsNullOrWhiteSpace(subject)||subject.Length>128||subject.Any(char.IsControl))throw new ArgumentException();
        var fingerprint=Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{scope,command,subject},Json)));
        await using var c=await source.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);
        await Run(c,tx,"SELECT pg_advisory_xact_lock(hashtextextended($1,0))",ct,"late-review-op:"+scope.Window.OrganizationId+":"+command.OperationId);
        await Run(c,tx,"SELECT pg_advisory_xact_lock(hashtextextended($1,0))",ct,"late-review-case:"+scope.Window.OrganizationId+":"+scope.SettlementId+":"+command.WorkId);
        var current=await Read(c,tx,scope,command.WorkId,true,project,ct)??throw new SnapshotOperationConflictException();
        LateReviewEntry? entry=null;
        await using(var old=new NpgsqlCommand("SELECT fingerprint,version,status,decision,reason_code,reviewed_at_utc FROM source_late_work_reviews WHERE organization_id=$1 AND operation_id=$2",c,tx))
        {
            Add(old,scope.Window.OrganizationId,command.OperationId);await using var rows=await old.ExecuteReaderAsync(ct);
            if(await rows.ReadAsync(ct)){if(rows.GetString(0)!=fingerprint)throw new SnapshotOperationConflictException();entry=Entry(rows,1);}
        }
        if(entry is null)
        {
            var next=decide(current.Item.Version,command.ExpectedVersion,command.Decision,command.ReasonCode);
            await using var insert=new NpgsqlCommand("INSERT INTO source_late_work_reviews(organization_id,barrier_id,work_id,version,status,decision,reason_code,operation_id,fingerprint,subject_id,authorization_decision_id) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11) RETURNING reviewed_at_utc",c,tx);
            Add(insert,scope.Window.OrganizationId,scope.SettlementId,command.WorkId,next.Version,next.Status,command.Decision,command.ReasonCode,command.OperationId,fingerprint,subject,authorizationId);
            var at=(DateTime)(await insert.ExecuteScalarAsync(ct))!;entry=new(next.Version,next.Status,command.Decision,command.ReasonCode,new DateTimeOffset(at));
            current=(await Read(c,tx,scope,command.WorkId,true,project,ct))!;
        }
        await tx.CommitAsync(ct);return new(command.OperationId,entry,current);
    }
    private static async Task<LateWorkDetail?> Read(NpgsqlConnection c,NpgsqlTransaction tx,LateWorkScope scope,Guid id,bool canReview,Func<string,string,DateTimeOffset,long,string,LateWorkItem> project,CancellationToken ct)
    {
        LateWorkItem item;
        await using(var q=new NpgsqlCommand("SELECT w.work_id,w.event_type,w.payload::text,w.received_at_utc,COALESCE(r.version,0),COALESCE(r.status,'pending_review')"+Rows+Latest+" WHERE "+Membership+" AND w.work_id=$7",c,tx))
        {Scope(q,scope);Add(q,id);await using var rows=await q.ExecuteReaderAsync(ct);if(!await rows.ReadAsync(ct))return null;item=Project(rows,project);}
        var links=new List<Guid>();
        await using(var q=new NpgsqlCommand("SELECT b.id"+Rows+" WHERE b.organization_id=$1 AND b.restaurant_id=$2 AND b.branch_id=$3 AND w.work_id=$4 AND b.phase='committed' ORDER BY (b.id=$5) DESC,b.id LIMIT 20",c,tx))
        {Add(q,scope.Window.OrganizationId,scope.Window.RestaurantId,scope.Window.BranchId,id,scope.SettlementId);await using var rows=await q.ExecuteReaderAsync(ct);while(await rows.ReadAsync(ct))links.Add(rows.GetGuid(0));}
        var history=new List<LateReviewEntry>();
        await using(var q=new NpgsqlCommand("SELECT version,status,decision,reason_code,reviewed_at_utc FROM source_late_work_reviews WHERE organization_id=$1 AND barrier_id=$2 AND work_id=$3 ORDER BY version DESC LIMIT 21",c,tx))
        {Add(q,scope.Window.OrganizationId,scope.SettlementId,id);await using var rows=await q.ExecuteReaderAsync(ct);while(await rows.ReadAsync(ct))history.Add(Entry(rows,0));}
        var truncated=history.Count>20;if(truncated)history.RemoveAt(20);return new(scope,item,links.ToArray(),history.ToArray(),truncated,canReview);
    }
    private static LateWorkItem Project(NpgsqlDataReader rows,Func<string,string,DateTimeOffset,long,string,LateWorkItem> project)=>
        project(rows.GetString(1),rows.GetString(2),rows.GetFieldValue<DateTimeOffset>(3),rows.GetInt64(4),rows.GetString(5)) with{WorkId=rows.GetGuid(0)};
    private static LateReviewEntry Entry(NpgsqlDataReader rows,int start)=>new(rows.GetInt64(start),rows.GetString(start+1),rows.GetString(start+2),rows.GetString(start+3),rows.GetFieldValue<DateTimeOffset>(start+4));
    private static void Scope(NpgsqlCommand q,LateWorkScope scope)=>Add(q,scope.Window.OrganizationId,scope.Window.RestaurantId,scope.Window.BranchId,scope.Window.FromUtc,scope.Window.ToUtc,scope.SettlementId);
    private static void Add(NpgsqlCommand q,params object[] values){foreach(var value in values)q.Parameters.AddWithValue(value);}
    private static async Task Run(NpgsqlConnection c,NpgsqlTransaction tx,string sql,CancellationToken ct,params object[] values)
    {await using var q=new NpgsqlCommand(sql,c,tx){CommandTimeout=10};Add(q,values);await q.ExecuteNonQueryAsync(ct);}
}
