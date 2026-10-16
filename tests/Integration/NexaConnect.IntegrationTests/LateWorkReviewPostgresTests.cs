extern alias POS;
extern alias ORDER;
extern alias PAYMENT;
using System.Text.Json;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Infrastructure.Persistence;
using Npgsql;
using PosDb=POS::NexaConnect.Services.POS.Infrastructure.Persistence;
using PosApp=POS::NexaConnect.Services.POS.Application.CashReviews;
using PosAuth=POS::NexaConnect.Services.POS.Application.Shifts;
namespace NexaConnect.IntegrationTests;
public sealed partial class DaySealPostgresTests
{
 private NpgsqlDataSource ReviewDb(string owner)=>owner=="Order"?order:owner=="Payment"?payment:pos;
 private Task<LateWorkPage> ReviewList(string owner,LateWorkScope scope,string? cursor=null,int limit=25)=>owner switch{
  "Order"=>new ORDER::NexaConnect.Services.Order.Infrastructure.Persistence.PostgresOrderLateWorkStore(order).ListAsync(scope,cursor,limit,true,default),
  "Payment"=>new PAYMENT::NexaConnect.Services.Payment.Infrastructure.PostgresPaymentLateWorkStore(payment).ListAsync(scope,cursor,limit,true,default),
  _=>new PosDb.PostgresPOSLateWorkStore(pos).ListAsync(scope,cursor,limit,true,default)};
 private Task<LateReviewResult> Review(string owner,LateWorkScope scope,LateReviewCommand command,string actor="manager")=>owner switch{
  "Order"=>new ORDER::NexaConnect.Services.Order.Infrastructure.Persistence.PostgresOrderLateWorkStore(order).ReviewAsync(scope,command,actor,Guid.NewGuid(),default),
  "Payment"=>new PAYMENT::NexaConnect.Services.Payment.Infrastructure.PostgresPaymentLateWorkStore(payment).ReviewAsync(scope,command,actor,Guid.NewGuid(),default),
  _=>new PosDb.PostgresPOSLateWorkStore(pos).ReviewAsync(scope,command,actor,Guid.NewGuid(),default)};
 private async Task<LateWorkScope> CapturedReview(string owner,int count=1)
 {
  var state=(await FinalizeSettlement(await PrepareSettlement())).Settlement!;var scope=new LateWorkScope(Window,state.Id);
  for(var i=0;i<count;i++){
   var payload=JsonSerializer.Serialize(new{orderId=Guid.NewGuid(),occurredAtUtc=Window.FromUtc.AddHours(1),providerReference="private-provider",amount=123,subject="private-subject"});
   Assert.True(await new PostgresLateFinancialWork(ReviewDb(owner)).CaptureAsync(organization,restaurant,branch,"order.manual-tender-settled.v1","private-event-"+Guid.NewGuid(),new string('a',64),payload,(from,to)=>from==Window.FromUtc&&to==Window.ToUtc,default));
  }return scope;
 }
 [ReportingDatabaseTheory][InlineData("Order")][InlineData("Payment")][InlineData("POS")]
 public async Task Late_review_exact_replay_preserves_append_history_and_financial_receipt(string owner)
 {
  var scope=await CapturedReview(owner);var original=JsonSerializer.Serialize((await Settlements.ReadAsync(Day,default))!.Receipt);var item=Assert.Single((await ReviewList(owner,scope)).Items);
  var command=new LateReviewCommand(item.WorkId,Guid.NewGuid(),0,"investigate","investigate_delivery");var first=await Review(owner,scope,command);
  Assert.Equal(1,first.OperationDecision.Version);Assert.Equal("investigating",first.Detail.Item.Status);
  var second=await Review(owner,scope,new(item.WorkId,Guid.NewGuid(),1,"require_correction","correction_needed"));Assert.Equal(2,second.Detail.Item.Version);
  var replay=await Review(owner,scope,command);Assert.Equal(first.OperationDecision,replay.OperationDecision);Assert.Equal(2,replay.Detail.Item.Version);Assert.Equal(2,replay.Detail.History.Length);
  await Assert.ThrowsAsync<SnapshotOperationConflictException>(()=>Review(owner,scope,command with{ReasonCode="evidence_checked",Decision="acknowledge"}));
  await Assert.ThrowsAsync<SnapshotOperationConflictException>(()=>Review(owner,scope,command,"another-manager"));
  Assert.Equal(original,JsonSerializer.Serialize((await Settlements.ReadAsync(Day,default))!.Receipt));Assert.Equal(2,await Count(ReviewDb(owner),"source_late_work_reviews"));Assert.Equal(0,await Count(pos,"cash_movements"));
  var serialized=JsonSerializer.Serialize(replay);Assert.DoesNotContain("private-",serialized);Assert.DoesNotContain("fingerprint",serialized);Assert.DoesNotContain("subject",serialized);
  Assert.Contains(scope.SettlementId,replay.Detail.SettlementLinks);Assert.Single(replay.Detail.Item.Records);
  await Assert.ThrowsAsync<PostgresException>(()=>Sql(ReviewDb(owner),"UPDATE source_late_work_reviews SET status='reviewed'"));
  await Assert.ThrowsAsync<PostgresException>(()=>Sql(ReviewDb(owner),"DELETE FROM source_late_work_reviews"));
  var migration=Directory.GetDirectories(Path.Combine(Root(),"src/Tools/NexaConnect.DataMigration/Scripts",owner),"*_late_work_reviews").Single();
  await Assert.ThrowsAsync<PostgresException>(()=>Sql(ReviewDb(owner),File.ReadAllText(Path.Combine(migration,"down.sql"))));
 }
 [ReportingDatabaseTheory][InlineData("Order")][InlineData("Payment")][InlineData("POS")]
 public async Task Late_review_concurrent_managers_have_one_version_winner(string owner)
 {
  var scope=await CapturedReview(owner);var item=Assert.Single((await ReviewList(owner,scope)).Items);
  async Task<bool> Attempt(string actor){try{await Review(owner,scope,new(item.WorkId,Guid.NewGuid(),0,"acknowledge","evidence_checked"),actor);return true;}catch(SnapshotOperationConflictException){return false;}}
  var winners=await Task.WhenAll(Attempt("first"),Attempt("second"));Assert.Single(winners,x=>x);Assert.Equal(1,await Count(ReviewDb(owner),"source_late_work_reviews"));
 }
 [ReportingDatabaseTheory][InlineData("Order")][InlineData("Payment")][InlineData("POS")]
 public async Task Late_review_pages_are_bounded_and_scoped_to_exact_committed_barrier(string owner)
 {
  var scope=await CapturedReview(owner,3);var page=await ReviewList(owner,scope,limit:2);Assert.Equal(2,page.Items.Length);Assert.NotNull(page.NextCursor);
  var last=await ReviewList(owner,scope,page.NextCursor,2);Assert.Single(last.Items);Assert.Null(last.NextCursor);Assert.DoesNotContain(last.Items[0].WorkId,page.Items.Select(x=>x.WorkId));
  Assert.Empty((await ReviewList(owner,scope with{Window=Window with{OrganizationId=Guid.NewGuid()}})).Items);
  Assert.Empty((await ReviewList(owner,scope with{Window=Window with{BranchId=Guid.NewGuid()}})).Items);
  Assert.Empty((await ReviewList(owner,scope with{Window=Window with{FromUtc=Window.FromUtc.AddMinutes(1)}})).Items);
  Assert.Empty((await ReviewList(owner,scope with{SettlementId=Guid.NewGuid()})).Items);
  await Assert.ThrowsAsync<ArgumentException>(()=>ReviewList(owner,scope,"invalid-base64"));await Assert.ThrowsAsync<ArgumentException>(()=>ReviewList(owner,scope,limit:51));
 }
 [ReportingDatabaseFact]
 public async Task Late_review_accountant_reads_and_live_revocation_blocks_new_and_replayed_decisions()
 {
  var scope=await CapturedReview("POS");var store=new PosDb.PostgresPOSLateWorkStore(pos);var permissions=new ReviewPermissions(this);
  var application=new PosApp.POSLateWork(new PosApp.PosDayRead(new PosDb.PostgresPosDayReader(pos),this,permissions),this,permissions,store);
  var page=await application.ListAsync(scope,null,25,new("accountant","token"),default);Assert.False(page.CanReview);var item=Assert.Single(page.Items);
  var command=new LateReviewCommand(item.WorkId,Guid.NewGuid(),0,"investigate","investigate_delivery");
  await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>application.ReviewAsync(scope,command,new("accountant","token"),"accountant",default));
  permissions.RevokeAfterCaseRead=true;await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>application.ReviewAsync(scope,command,new("manager","token"),"manager",default));Assert.Equal(0,await Count(pos,"source_late_work_reviews"));
  permissions.RevokeAfterCaseRead=false;permissions.Calls=0;var reviewed=await application.ReviewAsync(scope,command,new("manager","token"),"manager",default);Assert.Equal(1,reviewed.OperationDecision.Version);
  allowed=false;await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>application.ReviewAsync(scope,command,new("manager","token"),"manager",default));Assert.Equal(1,await Count(pos,"source_late_work_reviews"));
 }
 private sealed class ReviewPermissions(DaySealPostgresTests owner):PosAuth.IAuthorizationDecisionClient
 {
  public bool RevokeAfterCaseRead;public int Calls;
  public Task<PosAuth.AuthorizationDecision> DecideAsync(PosAuth.PosUserContext user,PosAuth.RestaurantAuthorizationScope scope,string permission,CancellationToken ct)
  {Calls++;return Task.FromResult(new PosAuth.AuthorizationDecision(Guid.NewGuid(),owner.allowed&&(!RevokeAfterCaseRead||Calls<=3)&&(permission!="pos.day-close.late-work.review"||user.Subject!="accountant"),null));}
 }
 [ReportingDatabaseTheory][InlineData("Order")][InlineData("Payment")][InlineData("POS")]
 public async Task Failed_review_insert_rolls_back_and_same_operation_can_retry_without_financial_mutation(string owner)
 {
  var scope=await CapturedReview(owner,2);var db=ReviewDb(owner);var items=(await ReviewList(owner,scope)).Items;
  var command=new LateReviewCommand(items[0].WorkId,Guid.NewGuid(),0,"acknowledge","evidence_checked");
  await using var revision=db.CreateCommand("SELECT COALESCE(sum(revision),0)::bigint FROM source_financial_revisions");var before=await revision.ExecuteScalarAsync();var messages=await Count(db,"outbox_messages");
  await Sql(db,"CREATE FUNCTION reject_review_test() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'test rollback'; END; $$; CREATE TRIGGER reject_review_test AFTER INSERT ON source_late_work_reviews FOR EACH ROW EXECUTE FUNCTION reject_review_test()");
  await Assert.ThrowsAsync<PostgresException>(()=>Review(owner,scope,command));Assert.Equal(0,await Count(db,"source_late_work_reviews"));
  await Sql(db,"DROP TRIGGER reject_review_test ON source_late_work_reviews; DROP FUNCTION reject_review_test()");
  var results=await Task.WhenAll(Review(owner,scope,command),Review(owner,scope,command));Assert.Equal(results[0].OperationDecision,results[1].OperationDecision);Assert.Equal(1,await Count(db,"source_late_work_reviews"));
  await Assert.ThrowsAsync<SnapshotOperationConflictException>(()=>Review(owner,scope,command with{WorkId=items[1].WorkId}));
  Assert.Equal(before,await revision.ExecuteScalarAsync());Assert.Equal(messages,await Count(db,"outbox_messages"));Assert.Equal(2,await Count(db,"source_late_work"));
 }
 [ReportingDatabaseTheory][InlineData("Order")][InlineData("Payment")][InlineData("POS")]
 public async Task Armed_custody_is_hidden_and_cannot_be_reviewed_until_source_commit(string owner)
 {
  var command=await PrepareSettlement();barrierFailAfter="POS";await Assert.ThrowsAsync<HttpRequestException>(()=>FinalizeSettlement(command));
  var intent=(await Settlements.ReadAsync(Day,default))!;var scope=new LateWorkScope(Window,intent.Id);var db=ReviewDb(owner);
  Assert.False(await new PostgresLateFinancialWork(db).CaptureAsync(organization,restaurant,branch,"order.manual-tender-settled.v1",Guid.NewGuid().ToString(),new string('b',64),"{}",(from,to)=>true,default));
  Assert.Empty((await ReviewList(owner,scope)).Items);await using var idQuery=db.CreateCommand("SELECT work_id FROM source_late_work");var id=(Guid)(await idQuery.ExecuteScalarAsync())!;
  await Assert.ThrowsAsync<SnapshotOperationConflictException>(()=>Review(owner,scope,new(id,Guid.NewGuid(),0,"investigate","investigate_delivery")));
  barrierFailAfter=null;await Recovery.RecoverAsync(intent,default);Assert.Single((await ReviewList(owner,scope)).Items);
 }
}
