using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using NexaConnect.Services.POS.Domain.DayClose;
using NexaConnect.Services.POS.Application.DayClose;
namespace NexaConnect.Services.POS.Infrastructure.DayClose;

public sealed class PostgresDayCutoffStore(NpgsqlDataSource source) : ICutoffPreparationStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<PreparationState?> ReadAsync(DayIdentity day, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        return await Load(connection, null, day, false, ct);
    }
    public async Task<PreparationLease> BeginAsync(DayIdentity day, PreparationCommand command, PreparationActor actor, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        // Serializes reuse of an organization-scoped operation across different branch/day rows.
        await Execute(connection, tx, "SELECT pg_advisory_xact_lock(hashtextextended($1,0))", ct, day.OrganizationId.ToString("D")+":"+command.OperationId.ToString("D"));
        var initial = BranchDayCutoff.New(day, now).Export();
        await Execute(connection, tx, "INSERT INTO branch_day_cutoffs(organization_id,restaurant_id,branch_id,business_date,version,state) VALUES($1,$2,$3,$4,0,$5::jsonb) ON CONFLICT DO NOTHING", ct,
            day.OrganizationId,day.RestaurantId,day.BranchId,day.BusinessDate,JsonSerializer.Serialize(initial,Json));
        var state = await Load(connection,tx,day,true,ct) ?? throw new UnauthorizedAccessException();
        string fingerprint = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { day, command, actor.Subject },Json)));
        string? previousStatus = null;
        await using(var query = new NpgsqlCommand("SELECT fingerprint,status FROM branch_day_cutoff_operations WHERE organization_id=$1 AND operation_id=$2",connection,tx))
        {
            Add(query,day.OrganizationId,command.OperationId);
            await using var reader=await query.ExecuteReaderAsync(ct);
            if(await reader.ReadAsync(ct))
            {
                if(reader.GetString(0)!=fingerprint)throw new DayCloseConflictException("operation_conflict");
                previousStatus=reader.GetString(1);
            }
        }
        if(previousStatus=="completed") { await tx.CommitAsync(ct); return new(state,null); }
        if(previousStatus=="abandoned")throw new DayCloseConflictException("operation_superseded");
        var aggregate=BranchDayCutoff.Restore(state);var claim=Guid.NewGuid();
        aggregate.Begin(command,actor.Subject,claim,now,previousStatus=="pending");
        if(state.OperationId is { } old && old!=command.OperationId)
            await Execute(connection,tx,"UPDATE branch_day_cutoff_operations SET status='abandoned' WHERE organization_id=$1 AND operation_id=$2 AND status='pending'",ct,day.OrganizationId,old);
        if(previousStatus is null)
            await Execute(connection,tx,"INSERT INTO branch_day_cutoff_operations(organization_id,operation_id,branch_id,business_date,fingerprint,status) VALUES($1,$2,$3,$4,$5,'pending')",ct,
                day.OrganizationId,command.OperationId,day.BranchId,day.BusinessDate,fingerprint);
        state=aggregate.Export();await Save(connection,tx,state,"prepare",command.OperationId,actor,ct);
        await tx.CommitAsync(ct);return new(state,claim);
    }
    public async Task<PreparationState> CompleteAsync(DayIdentity day, Guid claim, DayEvidence? evidence, PreparationActor actor, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection=await source.OpenConnectionAsync(ct);await using var tx=await connection.BeginTransactionAsync(ct);
        var state=await Load(connection,tx,day,true,ct)??throw new DayCloseConflictException("preparation_missing");
        if(state.PreparingSubject!=actor.Subject || actor.DecisionId==Guid.Empty)throw new UnauthorizedAccessException();
        var operation=state.OperationId;var aggregate=BranchDayCutoff.Restore(state);aggregate.Complete(claim,evidence,now);state=aggregate.Export();
        await Save(connection,tx,state,"complete",operation,actor,ct);
        await Execute(connection,tx,"UPDATE branch_day_cutoff_operations SET status='completed',result_version=$3 WHERE organization_id=$1 AND operation_id=$2 AND status='pending'",ct,day.OrganizationId,operation!.Value,state.Version);
        await tx.CommitAsync(ct);return state;
    }
    public async Task<PreparationState> ValidateAsync(DayIdentity day,long expectedVersion,DayEvidence? evidence,PreparationActor actor,DateTimeOffset now,CancellationToken ct)
    {
        await using var connection=await source.OpenConnectionAsync(ct);await using var tx=await connection.BeginTransactionAsync(ct);
        var state=await Load(connection,tx,day,true,ct)??throw new DayCloseConflictException("preparation_missing");
        // Never return a concurrently replaced Ready snapshot validated against another version.
        if(state.Version!=expectedVersion)throw new DayCloseConflictException("version_changed");
        var aggregate=BranchDayCutoff.Restore(state);
        if(aggregate.Validate(evidence,now)){state=aggregate.Export();await Save(connection,tx,state,"invalidate",null,actor,ct);}
        await tx.CommitAsync(ct);return state;
    }
    private static async Task<PreparationState?> Load(NpgsqlConnection connection,NpgsqlTransaction? tx,DayIdentity day,bool locked,CancellationToken ct)
    {
        await using var query=new NpgsqlCommand("SELECT state FROM branch_day_cutoffs WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 AND business_date=$4"+(locked?" FOR UPDATE":""),connection,tx);
        Add(query,day.OrganizationId,day.RestaurantId,day.BranchId,day.BusinessDate);
        var json=await query.ExecuteScalarAsync(ct);
        if(json is not string text)return null;
        var state=JsonSerializer.Deserialize<PreparationState>(text,Json)??throw new InvalidOperationException("Preparation state invalid.");
        if(state.Identity!=day)throw new InvalidOperationException("Preparation state scope invalid.");
        return state;
    }
    private static async Task Save(NpgsqlConnection connection,NpgsqlTransaction tx,PreparationState state,string action,Guid? operation,PreparationActor actor,CancellationToken ct)
    {
        string json=JsonSerializer.Serialize(state,Json);var day=state.Identity;
        await Execute(connection,tx,"UPDATE branch_day_cutoffs SET version=$5,state=$6::jsonb WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 AND business_date=$4",ct,
            day.OrganizationId,day.RestaurantId,day.BranchId,day.BusinessDate,state.Version,json);
        await using var audit=new NpgsqlCommand("INSERT INTO branch_day_cutoff_audit(organization_id,branch_id,business_date,version,action,operation_id,subject_id,authorization_decision_id,occurred_at_utc,state) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10::jsonb)",connection,tx);
        Add(audit,day.OrganizationId,day.BranchId,day.BusinessDate,state.Version,action);
        audit.Parameters.Add(new NpgsqlParameter{NpgsqlDbType=NpgsqlDbType.Uuid,Value=(object?)operation??DBNull.Value});
        Add(audit,actor.Subject,actor.DecisionId,state.UpdatedAtUtc,json);await audit.ExecuteNonQueryAsync(ct);
    }
    private static void Add(NpgsqlCommand command,params object[] values){foreach(var value in values)command.Parameters.Add(new NpgsqlParameter{Value=value});}
    private static async Task Execute(NpgsqlConnection connection,NpgsqlTransaction tx,string sql,CancellationToken ct,params object[] values)
    {await using var command=new NpgsqlCommand(sql,connection,tx);Add(command,values);await command.ExecuteNonQueryAsync(ct);}
}
