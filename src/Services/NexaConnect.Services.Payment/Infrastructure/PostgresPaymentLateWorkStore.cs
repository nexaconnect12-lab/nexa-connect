using System.Text.Json;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Infrastructure.Persistence;
using NexaConnect.Services.Payment.Application.Refunds;
using NexaConnect.Services.Payment.Domain;
using Npgsql;
namespace NexaConnect.Services.Payment.Infrastructure;
public sealed class PostgresPaymentLateWorkStore(NpgsqlDataSource source):IPaymentLateWorkStore
{
 public Task<LateWorkPage> ListAsync(LateWorkScope scope,string? cursor,int limit,bool canReview,CancellationToken ct)=>new PostgresLateWorkReview(source).ListAsync(scope,cursor,limit,canReview,Project,ct);
 public Task<LateWorkDetail?> ReadAsync(LateWorkScope scope,Guid id,bool canReview,CancellationToken ct)=>new PostgresLateWorkReview(source).ReadAsync(scope,id,canReview,Project,ct);
 public Task<LateReviewResult> ReviewAsync(LateWorkScope scope,LateReviewCommand command,string actor,Guid authorization,CancellationToken ct)=>new PostgresLateWorkReview(source).ReviewAsync(scope,command,actor,authorization,
  (version,expected,decision,reason)=>{try{var next=new LateWorkCase(version,"pending_review").Review(expected,decision,reason);return(next.Version,next.Status);}catch(InvalidOperationException){throw new SnapshotOperationConflictException();}},Project,ct);
 private static LateWorkItem Project(string eventType,string payload,DateTimeOffset received,long version,string status)
 {
  using var json=JsonDocument.Parse(payload);var refs=new Dictionary<string,Guid>();DateTimeOffset? occurred=null;
  foreach(var field in json.RootElement.EnumerateObject())
  {
   var name=field.Name.ToLowerInvariant();if(name is "orderid" or "intentid" or "paymentintentid" or "settlementid" or "terminalid")
   {if(field.Value.ValueKind==JsonValueKind.String&&Guid.TryParse(field.Value.GetString(),out var id)&&id!=Guid.Empty)refs[name]=id;}
   if(name=="occurredatutc"&&field.Value.ValueKind==JsonValueKind.String&&field.Value.TryGetDateTimeOffset(out var at))occurred=at;
  }
  var safeType=eventType is "order.manual-tender-settled.v1" or "payment.omise-webhook-reference.v1" or "payment.authorization-reconciled.v1" or "payment.capture-reconciled.v1" or "payment.voided.v1" or "payment.void-failed.v1" or "payment.void-uncertain.v1" or "payment.void-reconciled.v1"?eventType:"unrecognized_delivery";
  return new(Guid.Empty,safeType,received,occurred,"late_delivery_for_settled_day",refs,version,status);
 }
}
