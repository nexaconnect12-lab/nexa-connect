using NexaConnect.Infrastructure.Messaging;
using NexaConnect.Observability;
using NexaConnect.Services.POS.Application.Drawers;
using NexaConnect.Services.POS.Domain.Drawers;
using Npgsql;
using NpgsqlTypes;
namespace NexaConnect.Services.POS.Infrastructure.Persistence;

public sealed partial class PostgresDrawerIntentRepository(NpgsqlDataSource source):IDrawerIntentRepository,IDrawerHistoryRepository
{
    private const string Projection="""
        SELECT d.organization_id,d.restaurant_id,d.branch_id,d.store_id,d.terminal_id,d.shift_id,d.cash_session_id,
        d.operation_id,d.cause,d.order_id,d.reason,d.actor_subject_id,d.authorization_decision_id,
        d.reserved_at_utc,d.expires_at_utc,COALESCE(o.outcome,'reserved'),o.reported_at_utc
        FROM drawer_intents d LEFT JOIN drawer_intent_outcomes o USING(organization_id,operation_id)
        """;
    public async Task<(DrawerIntent Intent,bool Created)> ReserveAsync(DrawerCommand command,string actor,Guid decision,
        Func<DrawerCustody?,DateTimeOffset,DrawerIntent> create,CancellationToken ct)
    {
        await using var connection=await source.OpenConnectionAsync(ct);
        await using var transaction=await connection.BeginTransactionAsync(ct);
        // No HTTP inside this transaction. Current POS custody rows remain locked until
        // the immutable intent, audit and outbox have committed together.
        var custody=await Custody(connection,transaction,command,ct);
        var now=await Now(connection,transaction,ct);
        var proposed=create(custody,now);
        await using var insert=new NpgsqlCommand("""
            INSERT INTO drawer_intents(organization_id,restaurant_id,branch_id,store_id,terminal_id,shift_id,cash_session_id,
            operation_id,cause,order_id,reason,actor_subject_id,authorization_decision_id,reserved_at_utc,expires_at_utc)
            VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15) ON CONFLICT DO NOTHING;
            """,connection,transaction);
        var s=command.Scope;
        foreach(var id in new[]{s.OrganizationId,s.RestaurantId,s.BranchId,s.StoreId,s.TerminalId,s.ShiftId,s.CashSessionId,command.OperationId})insert.Parameters.AddWithValue(id);
        insert.Parameters.AddWithValue(command.Cause);Nullable(insert,command.OrderId,NpgsqlDbType.Uuid);Nullable(insert,command.Reason,NpgsqlDbType.Text);
        insert.Parameters.AddWithValue(actor);insert.Parameters.AddWithValue(decision);insert.Parameters.AddWithValue(now);insert.Parameters.AddWithValue(proposed.ExpiresAtUtc);
        bool created=await insert.ExecuteNonQueryAsync(ct)==1;
        DrawerIntent saved;
        if(created){saved=proposed;await Audit(connection,transaction,saved,"reserved",decision,now,ct);}
        else{
            await using var find=new NpgsqlCommand(Projection+" WHERE d.organization_id=$1 AND(d.operation_id=$2 OR(d.cause='cash_sale' AND d.order_id=$3));",connection,transaction);
            find.Parameters.AddWithValue(s.OrganizationId);find.Parameters.AddWithValue(command.OperationId);Nullable(find,command.OrderId,NpgsqlDbType.Uuid);
            await using var reader=await find.ExecuteReaderAsync(ct);
            if(!await reader.ReadAsync(ct))throw new DrawerConflictException();saved=Read(reader);
            if(await reader.ReadAsync(ct))throw new DrawerConflictException();
            saved.RequireSame(command,actor);
        }
        await transaction.CommitAsync(ct);return(saved,created);
    }
    public async Task<DrawerIntent?> FindAsync(Guid org,Guid operation,CancellationToken ct)
    {
        await using var command=source.CreateCommand(Projection+" WHERE d.organization_id=$1 AND d.operation_id=$2;");
        command.Parameters.AddWithValue(org);command.Parameters.AddWithValue(operation);
        await using var reader=await command.ExecuteReaderAsync(ct);return await reader.ReadAsync(ct)?Read(reader):null;
    }
    public async Task<DrawerIntent> ReportAsync(DrawerIntent original,string outcome,Guid decision,CancellationToken ct)
    {
        await using var connection=await source.OpenConnectionAsync(ct);await using var transaction=await connection.BeginTransactionAsync(ct);
        var s=original.Command.Scope;
        // Serialize terminal reports on the retained intent; no custody lock is needed
        // to report a past action. Reporting never renews its submission window.
        await using(var locked=new NpgsqlCommand("SELECT 1 FROM drawer_intents WHERE organization_id=$1 AND operation_id=$2 FOR UPDATE;",connection,transaction)){
            locked.Parameters.AddWithValue(s.OrganizationId);locked.Parameters.AddWithValue(original.Command.OperationId);
            if(await locked.ExecuteScalarAsync(ct) is null)throw new DrawerConflictException();
        }
        DrawerIntent current;
        await using(var find=new NpgsqlCommand(Projection+" WHERE d.organization_id=$1 AND d.operation_id=$2;",connection,transaction)){
            find.Parameters.AddWithValue(s.OrganizationId);find.Parameters.AddWithValue(original.Command.OperationId);
            await using var reader=await find.ExecuteReaderAsync(ct);if(!await reader.ReadAsync(ct))throw new DrawerConflictException();current=Read(reader);
        }
        current.RequireSame(original.Command,original.Actor);
        var now=await Now(connection,transaction,ct);var reported=current.Report(outcome,now);
        if(current.Status=="reserved"){
            await using var insert=new NpgsqlCommand("INSERT INTO drawer_intent_outcomes(organization_id,operation_id,outcome,reported_at_utc,authorization_decision_id) VALUES($1,$2,$3,$4,$5);",connection,transaction);
            insert.Parameters.AddWithValue(s.OrganizationId);insert.Parameters.AddWithValue(current.Command.OperationId);insert.Parameters.AddWithValue(reported.Status);insert.Parameters.AddWithValue(now);insert.Parameters.AddWithValue(decision);await insert.ExecuteNonQueryAsync(ct);
            await Audit(connection,transaction,current,reported.Status,decision,now,ct);
        }
        await transaction.CommitAsync(ct);return reported;
    }
    private static DrawerIntent Read(NpgsqlDataReader r)=>new(new(new(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetGuid(3),r.GetGuid(4),r.GetGuid(5),r.GetGuid(6)),r.GetGuid(7),r.GetString(8),r.IsDBNull(9)?null:r.GetGuid(9),r.IsDBNull(10)?null:r.GetString(10)),r.GetString(11),r.GetGuid(12),r.GetFieldValue<DateTimeOffset>(13),r.GetFieldValue<DateTimeOffset>(14),r.GetString(15),r.IsDBNull(16)?null:r.GetFieldValue<DateTimeOffset>(16));
    private static void Nullable(NpgsqlCommand c,object? value,NpgsqlDbType type)=>c.Parameters.Add(new NpgsqlParameter{NpgsqlDbType=type,Value=value??DBNull.Value});
    private static async Task<DateTimeOffset> Now(NpgsqlConnection c,NpgsqlTransaction tx,CancellationToken ct){await using var command=new NpgsqlCommand("SELECT clock_timestamp();",c,tx);return new((DateTime)(await command.ExecuteScalarAsync(ct))!);}
    private static async Task<DrawerCustody?> Custody(NpgsqlConnection c,NpgsqlTransaction tx,DrawerCommand command,CancellationToken ct)
    {
        var s=command.Scope;DrawerCustody? result;
        await using(var query=new NpgsqlCommand("""
            SELECT st.operational_status,t.registration_status,sh.status,cs.status,btrim(cs.currency),
            sh.employee_identity_subject_id,sh.opened_at_utc,sh.closed_at_utc IS NOT NULL,cs.opened_at_utc,cs.closed_at_utc IS NOT NULL
            FROM stores st JOIN terminals t ON t.store_id=st.id AND t.restaurant_id=st.restaurant_id
            JOIN shifts sh ON sh.store_id=st.id AND sh.terminal_id=t.id
            JOIN cash_sessions cs ON cs.store_id=st.id AND cs.shift_id=sh.id
            WHERE st.restaurant_id=$1 AND st.branch_id=$2 AND st.id=$3 AND t.id=$4 AND sh.id=$5 AND cs.id=$6
            FOR UPDATE OF st,t,sh,cs;
            """,c,tx)){
            foreach(var id in new[]{s.RestaurantId,s.BranchId,s.StoreId,s.TerminalId,s.ShiftId,s.CashSessionId})query.Parameters.AddWithValue(id);
            await using var r=await query.ExecuteReaderAsync(ct);
            result=await r.ReadAsync(ct)?new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetFieldValue<DateTimeOffset>(6),r.GetBoolean(7),r.GetFieldValue<DateTimeOffset>(8),r.GetBoolean(9),false,null):null;
        }
        if(result is not null&&command.Cause=="cash_sale"){
            await using var proof=new NpgsqlCommand("""
                SELECT occurred_at_utc FROM pos_order_settlements WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3
                AND terminal_id=$4 AND cash_session_id=$5 AND order_id=$6 AND method='cash' AND btrim(currency)='THB';
                """,c,tx);
            foreach(var id in new[]{s.OrganizationId,s.RestaurantId,s.BranchId,s.TerminalId,s.CashSessionId,command.OrderId!.Value})proof.Parameters.AddWithValue(id);
            if(await proof.ExecuteScalarAsync(ct) is DateTime at)result=result with{ConfirmedCashSale=true,SettledAtUtc=new DateTimeOffset(at)};
        }
        return result;
    }
    private static async Task Audit(NpgsqlConnection c,NpgsqlTransaction tx,DrawerIntent intent,string stage,Guid decision,DateTimeOffset now,CancellationToken ct)
    {
        var s=intent.Command.Scope;var id=Guid.NewGuid();
        await using var command=new NpgsqlCommand("INSERT INTO drawer_intent_audit(id,organization_id,operation_id,stage,actor_subject_id,authorization_decision_id,occurred_at_utc) VALUES($1,$2,$3,$4,$5,$6,$7);",c,tx);
        command.Parameters.AddWithValue(id);command.Parameters.AddWithValue(s.OrganizationId);command.Parameters.AddWithValue(intent.Command.OperationId);command.Parameters.AddWithValue(stage);command.Parameters.AddWithValue(intent.Actor);command.Parameters.AddWithValue(decision);command.Parameters.AddWithValue(now);await command.ExecuteNonQueryAsync(ct);
        var correlation=Guid.TryParse(CorrelationContext.Current,out var parsed)?parsed:Guid.NewGuid();
        await TransactionalAuditOutbox.EnqueueAuditAsync(c,tx,new(id,correlation,now,intent.Actor,s.OrganizationId,"pos.drawer.intent."+stage,"drawer-intent",intent.Command.OperationId.ToString("D"),"succeeded"),"pos.audit.v1",ct);
    }
}
