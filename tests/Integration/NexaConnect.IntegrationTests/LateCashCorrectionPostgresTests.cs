extern alias POS;
extern alias ORDER;
extern alias REPORTING;
using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Contracts.Reporting;
using Npgsql;
using App=POS::NexaConnect.Services.POS.Application.DayClose;
using Cash=POS::NexaConnect.Services.POS.Application.CashReviews;
using Auth=POS::NexaConnect.Services.POS.Application.Shifts;
using Domain=POS::NexaConnect.Services.POS.Domain;
using DayDomain=POS::NexaConnect.Services.POS.Domain.DayClose;
using Db=POS::NexaConnect.Services.POS.Infrastructure.DayClose;
using PosDb=POS::NexaConnect.Services.POS.Infrastructure.Persistence;
using OrderDb=ORDER::NexaConnect.Services.Order.Infrastructure.Persistence;
using OrderApp=ORDER::NexaConnect.Services.Order.Application.ManualTenders;
using OrderDomain=ORDER::NexaConnect.Services.Order.Domain;
namespace NexaConnect.IntegrationTests;
public sealed partial class DaySealPostgresTests
{
 private Db.PostgresLateCashCorrectionStore Corrections=>new(pos);
 private async Task<(LateWorkScope Scope,Guid Work,OrderManualTenderSettledV1 Tender)> CorrectionCase()
 {
  var drawer=await Drawer(await Store(),Window.FromUtc.AddHours(1),true);await using var terminal=pos.CreateCommand("SELECT s.terminal_id FROM cash_sessions c JOIN shifts s ON s.id=c.shift_id WHERE c.id=$1");terminal.Parameters.AddWithValue(drawer);var terminalId=(Guid)(await terminal.ExecuteScalarAsync())!;
  var sourceClock=new SealClock(Window.FromUtc.AddMinutes(90));var repository=new OrderDb.PostgresOrderRepository(order,sourceClock);
  var aggregate=OrderDomain.OrderAggregate.Create(Guid.NewGuid(),organization,branch,[new OrderDomain.OrderLine(Guid.NewGuid(),"Rice",10,1,"kitchen")],"THB",restaurantId:restaurant);
  aggregate.Submit();aggregate.MarkInventoryReserved();aggregate.MarkKitchenAccepted();await repository.SaveAsync(aggregate,default);
  var result=await new OrderApp.ManualTenderApplicationService(repository,sourceClock).ConfirmAsync(new(organization,branch,aggregate.Id,terminalId,Guid.NewGuid(),"cash",10,"THB",false,null,"manager",Guid.NewGuid(),Guid.NewGuid()),default);Assert.NotNull(result);
  await using var query=order.CreateCommand("SELECT payload::text FROM outbox_messages WHERE aggregate_id=$1 AND event_type='order.manual-tender-settled.v1'");query.Parameters.AddWithValue(aggregate.Id);
  var tender=JsonSerializer.Deserialize<OrderManualTenderSettledV1>((string)(await query.ExecuteScalarAsync())!,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
  Assert.Equal(tender,await new OrderDb.PostgresManualTenderEvidenceStore(order).ReadAsync(organization,restaurant,branch,tender.EventId,default));
  await using var sale=order.CreateCommand("SELECT payload::text FROM order_sale_publications WHERE order_id=$1");sale.Parameters.AddWithValue(aggregate.Id);
  var original=JsonSerializer.Deserialize<OrderSaleCompletedV1>((string)(await sale.ExecuteScalarAsync())!,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
  await new REPORTING::NexaConnect.Services.Reporting.Application.SaleFinancialReporting(new REPORTING::NexaConnect.Services.Reporting.Infrastructure.Persistence.PostgresSaleFinancialFactRepository(reporting)).ProjectAsync(original,default);
  var state=(await FinalizeSettlement(await PrepareSettlement())).Settlement!;var scope=new LateWorkScope(Window,state.Id);
  Assert.Equal(POS::NexaConnect.Services.POS.Application.OrderSettlements.OrderSettlementProjectionStatus.LateCaptured,await new PosDb.LateAwareOrderSettlementStore(pos).ProjectAsync(tender,default));
  var item=Assert.Single((await new PosDb.PostgresPOSLateWorkStore(pos).ListAsync(scope,null,25,true,default)).Items);
  await new PosDb.PostgresPOSLateWorkStore(pos).ReviewAsync(scope,new(item.WorkId,Guid.NewGuid(),0,"require_correction","correction_needed"),"manager",Guid.NewGuid(),default);
  return(scope,item.WorkId,tender);
 }
 private App.LateCashCorrections CorrectionApplication(CorrectionPermissions? permissions=null,CorrectionEvidence? evidence=null)
 {
  permissions??=new(this);evidence??=new(this);var store=new PosDb.PostgresPOSLateWorkStore(pos);
  var access=new Cash.POSLateWork(new Cash.PosDayRead(new PosDb.PostgresPosDayReader(pos),this,permissions),this,permissions,store);
  return new(Settlements,Corrections,evidence,this,permissions,access,TimeProvider.System);
 }
 private sealed class CorrectionEvidence(DaySealPostgresTests owner):App.ILateCashCorrectionEvidence
 {
  public bool Unavailable;public bool Denied;public bool Changed;
  public Task<App.CorrectionCalendar> CalendarAsync(Guid branch,CancellationToken ct)=>Task.FromResult(new App.CorrectionCalendar(owner.organization,owner.restaurant,owner.branch,"UTC","THB"));
  public async Task<Domain.CashTenderProof?> TenderAsync(Domain.LateCashCase work,Auth.PosUserContext user,CancellationToken ct)
  {
   if(Denied)throw new UnauthorizedAccessException();if(Unavailable)throw new HttpRequestException("private-source");
   var value=await new OrderDb.PostgresManualTenderEvidenceStore(owner.order).ReadAsync(owner.organization,owner.restaurant,owner.branch,work.Tender.EventId,ct);
   return value is null?null:Db.HttpLateCashCorrectionEvidence.Proof(Changed?value with{Amount=11}:value);
  }
 }
 private sealed class CorrectionPermissions(DaySealPostgresTests owner):Auth.IAuthorizationDecisionClient
 {
  public int PostDecisions;public bool RevokeBeforeAppend;public decimal? Limit=1000;
  public Task<Auth.AuthorizationDecision> DecideAsync(Auth.PosUserContext user,Auth.RestaurantAuthorizationScope scope,string permission,CancellationToken ct)
  {if(permission==Domain.LateCashCorrection.PostPermission)PostDecisions++;return Task.FromResult(new Auth.AuthorizationDecision(Guid.NewGuid(),owner.allowed&&(!(permission==Domain.LateCashCorrection.PostPermission&&RevokeBeforeAppend&&PostDecisions>=2))&&(!(permission==Domain.LateCashCorrection.PostPermission&&user.Subject=="accountant")),permission==Domain.LateCashCorrection.PostPermission?Limit:null));}
 }
 private async Task<LateCashCorrectionCommand> CorrectionCommand(App.LateCashCorrections app,Guid work)
 {
  var value=await app.PreviewAsync(organization,branch,Date,work,new("manager","token"),default);Assert.NotNull(value.Preview);return new(work,Guid.NewGuid(),value.Preview.ReviewVersion,value.Preview.Fingerprint);
 }
 [ReportingDatabaseFact]
 public async Task Verified_cash_correction_posts_once_with_audit_outbox_and_preserved_original_finances()
 {
  var (scope,work,tender)=await CorrectionCase();var app=CorrectionApplication();var command=await CorrectionCommand(app,work);
  var original=JsonSerializer.Serialize((await Settlements.ReadAsync(Day,default))!.Receipt);var before=await new PosDb.PostgresPosDayReader(pos).ReadAsync(scope.Window,default);
  var results=await Task.WhenAll(app.PostAsync(organization,branch,Date,command,new("manager","token"),Guid.NewGuid(),default),app.PostAsync(organization,branch,Date,command,new("manager","token"),Guid.NewGuid(),default));
  Assert.Equal(results[0].Receipt,results[1].Receipt);var receipt=results[0].Receipt!;Assert.Equal(-10,receipt.Adjustment);Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow),receipt.PostingDate);
  Assert.Equal(1,await Count(pos,"late_cash_corrections"));Assert.Equal(1,await Count(pos,"late_cash_correction_audit"));Assert.Equal(0,await Count(pos,"cash_movements"));Assert.Equal(0,await Count(pos,"pos_order_settlements"));
  await using var publication=pos.CreateCommand("SELECT payload::text FROM outbox_messages WHERE event_type='pos.late-cash-correction-posted.v1'");var published=JsonSerializer.Deserialize<PosLateCashCorrectionPostedV1>((string)(await publication.ExecuteScalarAsync())!,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;Assert.Equal(receipt.EventId,published.EventId);Assert.Equal(-10,published.CashVarianceAdjustment);Assert.Equal(tender.SettlementId,published.TenderId);
  var after=await new PosDb.PostgresPosDayReader(pos).ReadAsync(scope.Window,default);Assert.Equal(before.EvidenceVersion,after.EvidenceVersion);Assert.Equal(before.CashVariance,after.CashVariance);
  var today=new EndOfDayWindow(organization,restaurant,branch,DateTime.UtcNow.Date,DateTime.UtcNow.Date.AddDays(1));var adjusted=await new PosDb.PostgresPosDayReader(pos).ReadAsync(today,default);
  Assert.Equal(-10,adjusted.CashVariance);Assert.Equal(-10,adjusted.LateCashCorrectionAdjustment);Assert.Equal(1,adjusted.LateCashCorrections);
  Assert.Equal(original,JsonSerializer.Serialize((await Settlements.ReadAsync(Day,default))!.Receipt));Assert.Equal("ready_for_review",(await Sealing.ReadAsync(organization,branch,Date,new("manager","token"),default)).Status);
  Assert.Equal(POS::NexaConnect.Services.POS.Application.OrderSettlements.OrderSettlementProjectionStatus.LateCaptured,await new PosDb.LateAwareOrderSettlementStore(pos).ProjectAsync(tender,default));
  await Assert.ThrowsAsync<DayDomain.DayCloseConflictException>(()=>app.PostAsync(organization,branch,Date,command,new("other-manager","token"),Guid.NewGuid(),default));
  await Assert.ThrowsAsync<DayDomain.DayCloseConflictException>(()=>app.PostAsync(organization,branch,Date,command with{PreviewFingerprint=new('f',64)},new("manager","token"),Guid.NewGuid(),default));
 }
 [ReportingDatabaseFact]
 public async Task Competing_cash_correction_operations_have_one_winner_and_failed_publication_rolls_back()
 {
  var (_,work,_)=await CorrectionCase();var app=CorrectionApplication();var command=await CorrectionCommand(app,work);
  await Sql(pos,"CREATE FUNCTION reject_correction_publication_test() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.event_type='pos.late-cash-correction-posted.v1' THEN RAISE EXCEPTION 'rollback test'; END IF; RETURN NEW; END; $$; CREATE TRIGGER reject_correction_publication_test BEFORE INSERT ON outbox_messages FOR EACH ROW EXECUTE FUNCTION reject_correction_publication_test()");
  await Assert.ThrowsAsync<DayDomain.DayCloseConflictException>(()=>app.PostAsync(organization,branch,Date,command,new("manager","token"),Guid.NewGuid(),default));Assert.Equal(0,await Count(pos,"late_cash_corrections"));Assert.Equal(0,await Count(pos,"late_cash_correction_audit"));
  await Sql(pos,"DROP TRIGGER reject_correction_publication_test ON outbox_messages;DROP FUNCTION reject_correction_publication_test()");
  async Task<bool> Attempt(Guid operation){try{await app.PostAsync(organization,branch,Date,command with{OperationId=operation},new("manager","token"),Guid.NewGuid(),default);return true;}catch(DayDomain.DayCloseConflictException){return false;}}
  var winners=await Task.WhenAll(Attempt(command.OperationId),Attempt(Guid.NewGuid()));Assert.Single(winners,x=>x);Assert.Equal(1,await Count(pos,"late_cash_corrections"));
 }
 [ReportingDatabaseFact]
 public async Task Correction_requires_live_post_permission_exact_review_and_authoritative_source()
 {
  var (scope,work,_)=await CorrectionCase();var permissions=new CorrectionPermissions(this);var evidence=new CorrectionEvidence(this);var app=CorrectionApplication(permissions,evidence);var command=await CorrectionCommand(app,work);
  Assert.False((await app.PreviewAsync(organization,branch,Date,work,new("accountant","token"),default)).CanPost);
  await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>app.PostAsync(organization,branch,Date,command,new("accountant","token"),Guid.NewGuid(),default));
  permissions.PostDecisions=0;permissions.RevokeBeforeAppend=true;await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>app.PostAsync(organization,branch,Date,command,new("manager","token"),Guid.NewGuid(),default));permissions.RevokeBeforeAppend=false;
  evidence.Denied=true;await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>app.PostAsync(organization,branch,Date,command,new("manager","token"),Guid.NewGuid(),default));evidence.Denied=false;
  evidence.Changed=true;await Assert.ThrowsAsync<DayDomain.DayCloseConflictException>(()=>app.PostAsync(organization,branch,Date,command,new("manager","token"),Guid.NewGuid(),default));evidence.Changed=false;
  await new PosDb.PostgresPOSLateWorkStore(pos).ReviewAsync(scope,new(work,Guid.NewGuid(),1,"investigate","investigate_delivery"),"another-manager",Guid.NewGuid(),default);
  await Assert.ThrowsAsync<DayDomain.DayCloseConflictException>(()=>app.PostAsync(organization,branch,Date,command,new("manager","token"),Guid.NewGuid(),default));Assert.Equal(0,await Count(pos,"late_cash_corrections"));
 }
 [ReportingDatabaseFact]
 public async Task Correction_history_is_immutable_and_exact_recovery_does_not_depend_on_original_source_availability()
 {
  var (_,work,_)=await CorrectionCase();var evidence=new CorrectionEvidence(this);var app=CorrectionApplication(evidence:evidence);var command=await CorrectionCommand(app,work);
  var posted=await app.PostAsync(organization,branch,Date,command,new("manager","token"),Guid.NewGuid(),default);evidence.Unavailable=true;
  Assert.Equal(posted.Receipt,(await CorrectionApplication(evidence:evidence).PostAsync(organization,branch,Date,command,new("manager","token"),Guid.NewGuid(),default)).Receipt);
  foreach(var sql in new[]{"UPDATE late_cash_corrections SET adjustment=-11","DELETE FROM late_cash_corrections","TRUNCATE late_cash_corrections CASCADE","DELETE FROM late_cash_correction_audit"})await Assert.ThrowsAsync<PostgresException>(()=>Sql(pos,sql));
  await Assert.ThrowsAsync<PostgresException>(()=>Sql(pos,File.ReadAllText(Path.Combine(Root(),"src/Tools/NexaConnect.DataMigration/Scripts/POS/0020_late_cash_corrections/down.sql"))));
  allowed=false;await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>app.PostAsync(organization,branch,Date,command,new("manager","token"),Guid.NewGuid(),default));
 }
 [ReportingDatabaseFact]
 public async Task Correction_is_global_per_work_even_when_later_settlements_have_independent_review_links()
 {
  correctionTestDate=DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2));var (originalScope,work,tender)=await CorrectionCase();var app=CorrectionApplication();var command=await CorrectionCommand(app,work);
  var posted=await app.PostAsync(organization,branch,Date,command,new("manager","token"),Guid.NewGuid(),default);
  correctionTestDate=Date.AddDays(1);var second=(await FinalizeSettlement(await PrepareSettlement())).Settlement!;
  var hash=Db.HttpLateCashCorrectionEvidence.Proof(tender).Fingerprint;await new NexaConnect.Infrastructure.Persistence.PostgresLateFinancialWork(pos).CaptureAsync(organization,restaurant,branch,"order.manual-tender-settled.v1",tender.EventId.ToString("D"),hash,JsonSerializer.Serialize(tender,new JsonSerializerOptions(JsonSerializerDefaults.Web)),(_,_)=>true,default);
  var secondScope=new LateWorkScope(Window,second.Id);await new PosDb.PostgresPOSLateWorkStore(pos).ReviewAsync(secondScope,new(work,Guid.NewGuid(),0,"require_correction","correction_needed"),"manager",Guid.NewGuid(),default);
  Assert.Equal(posted.Receipt,(await app.PreviewAsync(organization,branch,Date,work,new("manager","token"),default)).Receipt);Assert.Equal(1,await Count(pos,"late_cash_corrections"));Assert.NotEqual(originalScope.SettlementId,secondScope.SettlementId);
  await Assert.ThrowsAsync<DayDomain.DayCloseConflictException>(()=>app.PostAsync(organization,branch,Date,command,new("manager","token"),Guid.NewGuid(),default));
 }
 [ReportingDatabaseFact]
 public async Task Posting_limits_apply_to_verified_amount_and_exact_replays()
 {
  var (_,work,_)=await CorrectionCase();var permissions=new CorrectionPermissions(this);var app=CorrectionApplication(permissions);var command=await CorrectionCommand(app,work);
  permissions.Limit=null;Assert.False((await app.PreviewAsync(organization,branch,Date,work,new("manager","token"),default)).CanPost);
  await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>app.PostAsync(organization,branch,Date,command,new("manager","token"),Guid.NewGuid(),default));
  permissions.Limit=5;Assert.False((await app.PreviewAsync(organization,branch,Date,work,new("manager","token"),default)).CanPost);
  await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>app.PostAsync(organization,branch,Date,command,new("manager","token"),Guid.NewGuid(),default));Assert.Equal(0,await Count(pos,"late_cash_corrections"));
  permissions.Limit=10;Assert.NotNull((await app.PostAsync(organization,branch,Date,command,new("manager","token"),Guid.NewGuid(),default)).Receipt);
  permissions.Limit=5;await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>app.PostAsync(organization,branch,Date,command,new("manager","token"),Guid.NewGuid(),default));Assert.Equal(1,await Count(pos,"late_cash_corrections"));
 }
}
