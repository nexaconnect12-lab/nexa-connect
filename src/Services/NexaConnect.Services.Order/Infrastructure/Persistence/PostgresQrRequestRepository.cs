using System.Text.Json;
using Npgsql;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Contracts.RestaurantQr;
using NexaConnect.Services.Order.Application.Orders;
using NexaConnect.Services.Order.Domain.Qr;

namespace NexaConnect.Services.Order.Infrastructure.Persistence;

public sealed class PostgresQrRequestRepository(NpgsqlDataSource source) : IQrRequestRepository
{
    private const string Select = "SELECT q.id,q.organization_id,q.restaurant_id,q.branch_id,q.visitor_id,q.entry_id,q.table_code,q.fingerprint,q.snapshot,r.status,r.version,r.submitted_at_utc,r.deadline_utc,r.reason,r.review_snapshot,r.consented_snapshot,a.order_id,CASE o.status WHEN 'accepted' THEN 'kitchen_accepted' WHEN 'completed' THEN 'paid' ELSE o.status END FROM order_qr_requests r JOIN order_qr_quotes q ON q.id=r.id LEFT JOIN order_qr_acceptances a ON a.request_id=r.id LEFT JOIN orders o ON o.id=a.order_id ";
    public async Task<RetainedGuestRequest?> GetAsync(Guid organization,Guid branch,Guid id,CancellationToken ct)
    {
        await using var db=await source.OpenConnectionAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
        await Begin(db,tx,organization,branch,ct);var value=await Read(db,tx,organization,branch,id,ct);await tx.CommitAsync(ct);return value;
    }
    public async Task<RetainedGuestRequest> ChangeAsync(RetainedGuestRequest expected,string action,QrQuote? review,QrAcceptedOrder? handoff,string actor,Guid? decision,Guid correlation,CancellationToken ct)
    {
        var s=expected.Request.Scope;
        await using var db=await source.OpenConnectionAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
        var now=await Begin(db,tx,s.OrganizationId,s.BranchId,ct);var value=await Read(db,tx,s.OrganizationId,s.BranchId,expected.Request.Id,ct)??throw new Domain.Qr.GuestRequestConflictException();
        if(value.Request.Status=="accepted"&&action=="accepted"){await tx.CommitAsync(ct);return value;}
        if(value.Request.Scope!=s||value.Request.Version!=expected.Request.Version||value.Quote.Fingerprint!=expected.Quote.Fingerprint)throw new Domain.Qr.GuestRequestConflictException();
        if(action=="price_review")
        {
            if(review is null||review.ExpiresAtUtc<=now||review.ExpiresAtUtc>value.Request.DeadlineUtc)throw new Domain.Qr.GuestRequestConflictException();
            value.Request.ReviewPrice(expected.Request.Version,now);
            await using var command=Command(db,tx,"UPDATE order_qr_requests SET review_snapshot=$1::jsonb WHERE id=$2",JsonSerializer.Serialize(review,JsonSerializerOptions.Web),value.Request.Id);await command.ExecuteNonQueryAsync(ct);
            value=value with{ReviewQuote=review};
        }
        else if(action=="price_confirmed")
        {
            if(value.ReviewQuote is null||value.ReviewQuote.ExpiresAtUtc<=now)throw new Domain.Qr.GuestRequestConflictException();
            value.Request.ConfirmPrice(expected.Request.Version,now);
            await using var command=Command(db,tx,"UPDATE order_qr_requests SET consented_snapshot=review_snapshot,review_snapshot=NULL WHERE id=$1",value.Request.Id);await command.ExecuteNonQueryAsync(ct);
            value=value with{Quote=value.ReviewQuote,ReviewQuote=null};
        }
        else if(action=="accepted")
        {
            if(handoff is null||decision is null||decision==Guid.Empty)throw new Domain.Qr.GuestRequestConflictException();
            value.Request.Accept(expected.Request.Version,now);
            var order=handoff.Order;
            if(order.Id!=value.Request.Id||order.OrganizationId!=s.OrganizationId||order.RestaurantId!=s.RestaurantId||order.BranchId!=s.BranchId||order.Channel!="qr"||order.ServiceType!="dine_in"||order.WorkflowPaymentMethod!="cash_manual"||order.Status!=Domain.OrderStatus.Submitted)throw new Domain.Qr.GuestRequestConflictException();
            await new PostgresOrderRepository(source).SaveWithEventInTransactionAsync(db,tx,order,handoff.Submitted,ct);
            await using var insert=Command(db,tx,"INSERT INTO order_qr_acceptances(request_id,order_id,organization_id,restaurant_id,branch_id,entry_id,visitor_id,table_code,actor,decision_id,request_version,fingerprint,correlation_id,accepted_at_utc) VALUES($1,$1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13)",order.Id,s.OrganizationId,s.RestaurantId,s.BranchId,s.EntryId,s.VisitorId,s.TableCode,actor,decision.Value,value.Request.Version,order.PricingFingerprint!,correlation,now);await insert.ExecuteNonQueryAsync(ct);
            value=value with{OrderId=order.Id,OrderStatus="submitted"};
        }
        else throw new ArgumentException("Invalid QR transition.");
        await SaveState(db,tx,value.Request,ct);await Audit(db,tx,value.Request,action,actor,decision,correlation,now,ct);await tx.CommitAsync(ct);return value;
    }
    public async Task SaveQuoteAsync(RetainedGuestQuote value, CancellationToken ct)
    {
        await using var db = await source.OpenConnectionAsync(ct); await using var tx = await db.BeginTransactionAsync(ct);
        var s = value.Scope; var now = await Begin(db, tx, s.OrganizationId, s.BranchId, ct);
        if (value.Quote.ExpiresAtUtc <= now || value.Quote.ExpiresAtUtc > now.AddMinutes(2).AddSeconds(5)) throw new GuestRequestConflictException();
        await using (var count = Command(db, tx, "SELECT count(*) FILTER(WHERE visitor_id=$3),count(*) FILTER(WHERE entry_id=$4),count(*) FROM order_qr_quotes WHERE organization_id=$1 AND branch_id=$2 AND expires_at_utc>$5", s.OrganizationId,s.BranchId,s.VisitorId,s.EntryId,now))
        await using (var reader = await count.ExecuteReaderAsync(ct))
        { await reader.ReadAsync(ct); GuestRequest.AdmitQuote((int)reader.GetInt64(0),(int)reader.GetInt64(1),(int)reader.GetInt64(2)); }
        await using var insert = Command(db, tx, "INSERT INTO order_qr_quotes(id,organization_id,restaurant_id,branch_id,visitor_id,entry_id,table_code,fingerprint,snapshot,expires_at_utc) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9::jsonb,$10)",
            value.Quote.QuoteId!.Value,s.OrganizationId,s.RestaurantId,s.BranchId,s.VisitorId,s.EntryId,s.TableCode,value.Quote.Fingerprint,JsonSerializer.Serialize(value.Quote,JsonSerializerOptions.Web),value.Quote.ExpiresAtUtc);
        await insert.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task<RetainedGuestRequest> SubmitAsync(GuestRequestScope scope,QrSubmission input,DateTimeOffset visitorExpiry,Guid correlation,CancellationToken ct)
    {
        await using var db = await source.OpenConnectionAsync(ct); await using var tx = await db.BeginTransactionAsync(ct);
        var now = await Begin(db,tx,scope.OrganizationId,scope.BranchId,ct);
        var existing = await Read(db,tx,scope.OrganizationId,scope.BranchId,input.QuoteId,ct);
        if (existing is not null)
        {
            if (existing.Request.Scope != scope || existing.Request.Fingerprint != input.Fingerprint) throw new GuestRequestConflictException();
            await tx.CommitAsync(ct); return existing;
        }
        QrQuote quote;
        await using (var command = Command(db,tx,"SELECT snapshot,restaurant_id,entry_id,table_code FROM order_qr_quotes WHERE id=$1 AND organization_id=$2 AND branch_id=$3 AND visitor_id=$4 AND fingerprint=$5 AND expires_at_utc>$6",input.QuoteId,scope.OrganizationId,scope.BranchId,scope.VisitorId,input.Fingerprint,now))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct) || reader.GetGuid(1)!=scope.RestaurantId || reader.GetGuid(2)!=scope.EntryId || reader.GetString(3)!=scope.TableCode || visitorExpiry<=now) throw new GuestRequestConflictException();
            quote = JsonSerializer.Deserialize<QrQuote>(reader.GetString(0),JsonSerializerOptions.Web)!;
        }
        await using (var command = Command(db,tx,"SELECT count(*) FILTER(WHERE visitor_id=$3),count(*) FILTER(WHERE entry_id=$4 AND submitted_at_utc>$5),count(*) FILTER(WHERE submitted_at_utc>$5),count(*) FILTER(WHERE visitor_id=$3 AND status IN('pending','price_review')),count(*) FILTER(WHERE entry_id=$4 AND status IN('pending','price_review')),count(*) FILTER(WHERE status IN('pending','price_review')) FROM order_qr_requests WHERE organization_id=$1 AND branch_id=$2",scope.OrganizationId,scope.BranchId,scope.VisitorId,scope.EntryId,now.AddHours(-1)))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        { await reader.ReadAsync(ct); GuestRequest.Admit((int)reader.GetInt64(0),(int)reader.GetInt64(1),(int)reader.GetInt64(2),(int)reader.GetInt64(3),(int)reader.GetInt64(4),(int)reader.GetInt64(5)); }
        var request = new GuestRequest(input.QuoteId,scope,input.Fingerprint,now,visitorExpiry < now.AddMinutes(15)?visitorExpiry:now.AddMinutes(15));
        await using (var insert = Command(db,tx,"INSERT INTO order_qr_requests(id,organization_id,branch_id,visitor_id,entry_id,status,version,submitted_at_utc,deadline_utc) VALUES($1,$2,$3,$4,$5,'pending',1,$6,$7)",request.Id,scope.OrganizationId,scope.BranchId,scope.VisitorId,scope.EntryId,now,request.DeadlineUtc)) await insert.ExecuteNonQueryAsync(ct);
        await Audit(db,tx,request,"submitted","qr-guest",null,correlation,now,ct); await tx.CommitAsync(ct); return new(request,quote);
    }
    public async Task<QrRequestList> ListAsync(Guid organization,Guid branch,Guid? visitor,string state,CancellationToken ct)
    {
        await using var db = await source.OpenConnectionAsync(ct); await using var tx = await db.BeginTransactionAsync(ct);
        await Begin(db,tx,organization,branch,ct);
        string filter = visitor is null ? "r.status=$3" : "r.visitor_id=$3";
        // SQL identifiers/order come only from service-owned fixed alternatives.
        string order = visitor is null && state=="pending" ? "r.submitted_at_utc,r.id" : "r.submitted_at_utc DESC,r.id";
        await using var command = Command(db,tx,Select+$"WHERE r.organization_id=$1 AND r.branch_id=$2 AND {filter} ORDER BY {order} LIMIT 11",organization,branch,visitor is Guid id?(object)id:state);
        var result = new List<QrRequestView>(); await using(var reader = await command.ExecuteReaderAsync(ct))while(await reader.ReadAsync(ct))result.Add(Map(reader).View());
        await tx.CommitAsync(ct); return new(result.Take(10).ToArray(),result.Count>10);
    }
    public async Task<RetainedGuestRequest?> RejectAsync(Guid organization,Guid branch,Guid id,QrRejection rejection,string actor,Guid decision,Guid correlation,CancellationToken ct)
    {
        await using var db = await source.OpenConnectionAsync(ct); await using var tx = await db.BeginTransactionAsync(ct);
        var now = await Begin(db,tx,organization,branch,ct); var value = await Read(db,tx,organization,branch,id,ct);
        if (value is null) { await tx.CommitAsync(ct); return null; }
        value.Request.Reject(rejection.ExpectedVersion,rejection.Reason,now);
        await SaveState(db,tx,value.Request,ct); await Audit(db,tx,value.Request,"rejected",actor,decision,correlation,now,ct);
        await tx.CommitAsync(ct); return value;
    }
    private static async Task<DateTimeOffset> Begin(NpgsqlConnection db,NpgsqlTransaction tx,Guid organization,Guid branch,CancellationToken ct)
    {
        await using (var command=Command(db,tx,"SELECT pg_advisory_xact_lock(hashtextextended($1,0))",$"qr-requests:{organization:D}:{branch:D}"))await command.ExecuteNonQueryAsync(ct);
        DateTimeOffset now;await using(var command=Command(db,tx,"SELECT clock_timestamp()"))now=new((DateTime)(await command.ExecuteScalarAsync(ct))!);
        var expired=new List<RetainedGuestRequest>();
        await using(var command=Command(db,tx,Select+"WHERE r.organization_id=$1 AND r.branch_id=$2 AND r.status IN('pending','price_review') AND r.deadline_utc<=$3",organization,branch,now))
        await using(var reader=await command.ExecuteReaderAsync(ct))while(await reader.ReadAsync(ct))expired.Add(Map(reader));
        foreach(var value in expired)
        {
            if(value.Request.Expire(now)){await SaveState(db,tx,value.Request,ct);await Audit(db,tx,value.Request,"expired","qr-expiry",null,Guid.NewGuid(),now,ct);}
        }
        await using(var command=Command(db,tx,"DELETE FROM order_qr_requests WHERE organization_id=$1 AND branch_id=$2 AND status IN('rejected','expired') AND submitted_at_utc<$3",organization,branch,now.AddHours(-24)))await command.ExecuteNonQueryAsync(ct);
        await using(var command=Command(db,tx,"DELETE FROM order_qr_quotes q WHERE organization_id=$1 AND branch_id=$2 AND expires_at_utc<=$3 AND NOT EXISTS(SELECT 1 FROM order_qr_requests r WHERE r.id=q.id)",organization,branch,now))await command.ExecuteNonQueryAsync(ct);
        return now;
    }
    private static async Task SaveState(NpgsqlConnection db,NpgsqlTransaction tx,GuestRequest value,CancellationToken ct)
    {
        await using var command=Command(db,tx,"UPDATE order_qr_requests SET status=$1,version=$2,reason=$3 WHERE id=$4 AND organization_id=$5 AND branch_id=$6 AND version=$7",value.Status,value.Version,value.Reason is null?DBNull.Value:value.Reason,value.Id,value.Scope.OrganizationId,value.Scope.BranchId,value.Version-1);
        if(await command.ExecuteNonQueryAsync(ct)!=1)throw new GuestRequestConflictException();
    }
    private static async Task<RetainedGuestRequest?> Read(NpgsqlConnection db,NpgsqlTransaction tx,Guid organization,Guid branch,Guid id,CancellationToken ct)
    {
        await using var command=Command(db,tx,Select+"WHERE r.organization_id=$1 AND r.branch_id=$2 AND r.id=$3",organization,branch,id);
        await using var reader=await command.ExecuteReaderAsync(ct);return await reader.ReadAsync(ct)?Map(reader):null;
    }
    private static RetainedGuestRequest Map(NpgsqlDataReader r)=>new(new(r.GetGuid(0),new(r.GetGuid(1),r.GetGuid(2),r.GetGuid(3),r.GetGuid(4),r.GetGuid(5),r.GetString(6)),r.GetString(7),r.GetFieldValue<DateTimeOffset>(11),r.GetFieldValue<DateTimeOffset>(12),r.GetString(9),r.GetInt64(10),r.IsDBNull(13)?null:r.GetString(13)),JsonSerializer.Deserialize<QrQuote>(r.GetString(r.IsDBNull(15)?8:15),JsonSerializerOptions.Web)!,r.IsDBNull(14)?null:JsonSerializer.Deserialize<QrQuote>(r.GetString(14),JsonSerializerOptions.Web),r.IsDBNull(16)?null:r.GetGuid(16),r.IsDBNull(17)?null:r.GetString(17));
    private static async Task Audit(NpgsqlConnection db,NpgsqlTransaction tx,GuestRequest value,string action,string actor,Guid? decision,Guid correlation,DateTimeOffset now,CancellationToken ct)
    {
        var id=Guid.NewGuid();
        await using(var command=Command(db,tx,"INSERT INTO order_qr_request_audit(id,organization_id,branch_id,request_id,action,version,actor,decision_id,correlation_id,occurred_at_utc) VALUES($1,$2,$3,$4,$5,$6,$7,$8::uuid,$9,$10)",id,value.Scope.OrganizationId,value.Scope.BranchId,value.Id,action,value.Version,actor,decision is Guid d?d:DBNull.Value,correlation,now))await command.ExecuteNonQueryAsync(ct);
        var audit=new PlatformAuditEventV1(id,correlation,now,actor,value.Scope.OrganizationId,"order.qr-request."+action,"qr-request",value.Id.ToString("D"),"succeeded");
        await using var enqueue=Command(db,tx,"INSERT INTO outbox_messages(id,event_type,contract_version,aggregate_type,aggregate_id,payload,correlation_id,occurred_at_utc) VALUES($1,'order.audit.v1',1,'qr-request',$2,$3::jsonb,$4,$5)",id,value.Id,JsonSerializer.Serialize(audit),correlation.ToString("D"),now);await enqueue.ExecuteNonQueryAsync(ct);
    }
    private static NpgsqlCommand Command(NpgsqlConnection db,NpgsqlTransaction tx,string sql,params object[] values)
    {
        var command=new NpgsqlCommand(sql,db,tx);foreach(var value in values){var p=command.Parameters.AddWithValue(value);if(value==DBNull.Value)p.NpgsqlDbType=NpgsqlTypes.NpgsqlDbType.Text; }return command;
    }
}
