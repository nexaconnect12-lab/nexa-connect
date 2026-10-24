using System.Security.Cryptography;
using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.POS.Application.DayClose;
using NexaConnect.Services.POS.Domain;
using NexaConnect.Services.POS.Domain.DayClose;
using Npgsql;
namespace NexaConnect.Services.POS.Infrastructure.DayClose;
public sealed class PostgresLateCashCorrectionStore(NpgsqlDataSource source):ILateCashCorrectionStore
{
 private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
 public async Task<LateCashCase?> CaseAsync(LateWorkScope scope,Guid work,CancellationToken ct){await using var c=await source.OpenConnectionAsync(ct);return await Case(c,null,scope,work,ct);}
 private static async Task<LateCashCase?> Case(NpgsqlConnection c,NpgsqlTransaction? tx,LateWorkScope scope,Guid work,CancellationToken ct)
 {
  OrderManualTenderSettledV1 value;long version;string status;
  await using(var q=new NpgsqlCommand("""
   SELECT w.payload::text,w.fingerprint,COALESCE(r.version,0),COALESCE(r.status,'pending_review')
   FROM source_day_barriers b JOIN source_late_work_links l ON l.organization_id=b.organization_id AND l.barrier_id=b.id
   JOIN source_late_work w ON w.organization_id=l.organization_id AND w.event_type=l.event_type AND w.event_id=l.event_id
   LEFT JOIN LATERAL(SELECT version,status FROM source_late_work_reviews r WHERE r.organization_id=b.organization_id AND r.barrier_id=b.id AND r.work_id=w.work_id ORDER BY version DESC LIMIT 1) r ON true
   WHERE b.organization_id=$1 AND b.restaurant_id=$2 AND b.branch_id=$3 AND b.from_utc=$4 AND b.to_utc=$5 AND b.id=$6 AND b.phase='committed' AND w.work_id=$7 AND w.event_type='order.manual-tender-settled.v1'
   """,c,tx))
  {
   Scope(q,scope);Add(q,work);await using var rows=await q.ExecuteReaderAsync(ct);if(!await rows.ReadAsync(ct))return null;
   value=JsonSerializer.Deserialize<OrderManualTenderSettledV1>(rows.GetString(0),Json)??throw new InvalidOperationException();
   if(HttpLateCashCorrectionEvidence.Proof(value).Fingerprint!=rows.GetString(1))throw new DayCloseConflictException("custody_evidence_changed");version=rows.GetInt64(2);status=rows.GetString(3);
  }
  if(value.OrganizationId!=scope.Window.OrganizationId||value.RestaurantId!=scope.Window.RestaurantId||value.BranchId!=scope.Window.BranchId)throw new DayCloseConflictException("custody_scope_changed");
  await using var drawer=new NpgsqlCommand("""
   SELECT c.id,c.closed_at_utc,EXISTS(SELECT 1 FROM pos_order_settlements p WHERE p.event_id=$5 OR p.settlement_id=$6 OR p.order_id=$7)
   FROM terminals t JOIN stores s ON s.id=t.store_id JOIN shifts sh ON sh.store_id=s.id AND sh.terminal_id=t.id JOIN cash_sessions c ON c.shift_id=sh.id AND c.store_id=s.id
   WHERE t.id=$1 AND s.restaurant_id=$2 AND s.branch_id=$3 AND sh.opened_at_utc<=$4 AND (sh.closed_at_utc IS NULL OR sh.closed_at_utc>=$4)
    AND c.opened_at_utc<=$4 AND c.closed_at_utc>=$4 AND c.status='closed' AND btrim(c.currency)=$8
   """,c,tx);
  Add(drawer,value.TerminalId,value.RestaurantId,value.BranchId,value.OccurredAtUtc,value.EventId,value.SettlementId,value.OrderId,value.Currency);
  await using var found=await drawer.ExecuteReaderAsync(ct);if(!await found.ReadAsync(ct))return null;
  var result=new LateCashCase(work,scope.SettlementId,version,status,HttpLateCashCorrectionEvidence.Proof(value),found.GetGuid(0),found.GetFieldValue<DateTimeOffset>(1),scope.Window.FromUtc,scope.Window.ToUtc,found.GetBoolean(2));
  if(await found.ReadAsync(ct))throw new DayCloseConflictException("drawer_ambiguous");return result;
 }
 public async Task<LateCashCorrectionReceipt?> ReadAsync(LateWorkScope scope,Guid work,CancellationToken ct)
 {
  await using var q=source.CreateCommand("SELECT receipt::text FROM late_cash_corrections WHERE organization_id=$1 AND restaurant_id=$2 AND branch_id=$3 AND work_id=$4");Add(q,scope.Window.OrganizationId,scope.Window.RestaurantId,scope.Window.BranchId,work);
  return await q.ExecuteScalarAsync(ct) is string json?JsonSerializer.Deserialize<LateCashCorrectionReceipt>(json,Json):null;
 }
 private static string Fingerprint(LateWorkScope scope,LateCashCorrectionCommand command,string actor)=>Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{scope,command,actor},Json)));
 public async Task<LateCashCorrectionReceipt?> ReplayAsync(LateWorkScope scope,LateCashCorrectionCommand command,string actor,CancellationToken ct)
 {await using var c=await source.OpenConnectionAsync(ct);return await Replay(c,null,scope,command,actor,ct);}
 private static async Task<LateCashCorrectionReceipt?> Replay(NpgsqlConnection c,NpgsqlTransaction? tx,LateWorkScope scope,LateCashCorrectionCommand command,string actor,CancellationToken ct)
 {
  await using var q=new NpgsqlCommand("SELECT request_fingerprint,receipt::text FROM late_cash_corrections WHERE organization_id=$1 AND operation_id=$2",c,tx);Add(q,scope.Window.OrganizationId,command.OperationId);await using var r=await q.ExecuteReaderAsync(ct);
  if(!await r.ReadAsync(ct))return null;if(r.GetString(0)!=Fingerprint(scope,command,actor))throw new DayCloseConflictException("correction_operation_conflict");return JsonSerializer.Deserialize<LateCashCorrectionReceipt>(r.GetString(1),Json);
 }
 public async Task<LateCashCorrectionReceipt> PostAsync(LateWorkScope scope,LateCashCorrectionCommand command,LateCashCase work,CorrectionPostingDay day,CashTenderProof proof,string actor,Guid authorization,Guid correlation,DateTimeOffset now,CancellationToken ct)
 {
  if(string.IsNullOrWhiteSpace(actor)||actor.Length>128||actor.Any(char.IsControl)||authorization==Guid.Empty||correlation==Guid.Empty)throw new ArgumentException();
  await using var c=await source.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);
  await Run(c,tx,"SELECT pg_advisory_xact_lock(hashtextextended($1,0))",ct,"late-cash-operation:"+scope.Window.OrganizationId+":"+command.OperationId);
  if(await Replay(c,tx,scope,command,actor,ct) is {} replay){await tx.CommitAsync(ct);return replay;}
  await Run(c,tx,"SELECT pg_advisory_xact_lock(hashtextextended($1,0))",ct,$"financial-revision:{scope.Window.RestaurantId:D}:{scope.Window.BranchId:D}");
  await Run(c,tx,"SELECT pg_advisory_xact_lock(hashtextextended($1,0))",ct,"late-review-case:"+scope.Window.OrganizationId+":"+scope.SettlementId+":"+command.WorkId);
  var current=await Case(c,tx,scope,command.WorkId,ct);
  if(current!=work||work.ReviewVersion!=command.ExpectedReviewVersion||LateCashCorrections.Preview(scope,work,day,-proof.Amount).Fingerprint!=command.PreviewFingerprint)throw new DayCloseConflictException("correction_preview_changed");
  decimal adjustment;try{adjustment=LateCashCorrection.Validate(work,proof,day,now);}catch(InvalidOperationException){throw new DayCloseConflictException("late_cash_evidence_changed");}
  var id=Guid.NewGuid();var eventId=Guid.NewGuid();var receipt=new LateCashCorrectionReceipt(id,command.OperationId,scope,command.WorkId,work.ReviewVersion,proof.OrderId,proof.TenderId,work.DrawerId,"THB",adjustment,day.Date,now,eventId);
  try{
   await Run(c,tx,"""
    INSERT INTO late_cash_corrections(organization_id,restaurant_id,branch_id,id,work_id,barrier_id,reviewed_version,source_event_id,tender_id,order_id,drawer_id,
     operation_id,request_fingerprint,preview_fingerprint,posting_date,posting_timezone,posting_from_utc,posting_to_utc,posted_at_utc,adjustment,currency,actor,authorization_decision_id,receipt)
    VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19,$20,'THB',$21,$22,$23::jsonb)
    """,ct,scope.Window.OrganizationId,scope.Window.RestaurantId,scope.Window.BranchId,id,command.WorkId,scope.SettlementId,work.ReviewVersion,proof.EventId,proof.TenderId,proof.OrderId,work.DrawerId,
    command.OperationId,Fingerprint(scope,command,actor),command.PreviewFingerprint,day.Date,day.TimeZone,day.FromUtc,day.ToUtc,now,adjustment,actor,authorization,JsonSerializer.Serialize(receipt,Json));
   await Run(c,tx,"INSERT INTO late_cash_correction_audit(organization_id,correction_id,actor,authorization_decision_id,occurred_at_utc) VALUES($1,$2,$3,$4,$5)",ct,scope.Window.OrganizationId,id,actor,authorization,now);
   var publication=new PosLateCashCorrectionPostedV1(eventId,correlation,now,scope.Window.OrganizationId,scope.Window.RestaurantId,scope.Window.BranchId,id,work.WorkId,scope.SettlementId,proof.OrderId,proof.TenderId,work.DrawerId,work.ReviewVersion,day.Date,day.FromUtc,day.ToUtc,"THB",adjustment);
   await Run(c,tx,"INSERT INTO outbox_messages(id,event_type,contract_version,aggregate_type,aggregate_id,payload,correlation_id,occurred_at_utc) VALUES($1,'pos.late-cash-correction-posted.v1',1,'late-cash-correction',$2,$3::jsonb,$4,$5)",ct,eventId,id,JsonSerializer.Serialize(publication,Json),correlation.ToString("D"),now);
   await tx.CommitAsync(ct);return receipt;
  }catch(PostgresException e)when(e.SqlState is "23505" or "23514" or "PDS01" or "P0001"){throw new DayCloseConflictException("correction_posting_conflict");}
 }
 private static void Add(NpgsqlCommand q,params object[] values){foreach(var value in values)q.Parameters.AddWithValue(value);}
 private static void Scope(NpgsqlCommand q,LateWorkScope s)=>Add(q,s.Window.OrganizationId,s.Window.RestaurantId,s.Window.BranchId,s.Window.FromUtc,s.Window.ToUtc,s.SettlementId);
 private static async Task Run(NpgsqlConnection c,NpgsqlTransaction tx,string sql,CancellationToken ct,params object[] values){await using var q=new NpgsqlCommand(sql,c,tx){CommandTimeout=10};Add(q,values);await q.ExecuteNonQueryAsync(ct);}
}
