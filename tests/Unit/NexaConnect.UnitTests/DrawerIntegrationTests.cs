using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NexaConnect.Contracts.POS;
using NexaConnect.POS;
using NexaConnect.POS.Hardware.Drawers;
namespace NexaConnect.UnitTests;

public sealed class DrawerIntegrationTests:IDisposable
{
 private readonly string root=Path.Combine(Path.GetTempPath(),"nexa-drawer-client-"+Guid.NewGuid().ToString("N"));
 private static PosClientConfiguration Config()=>new("http://127.0.0.1:49199/realms/fixture","nexaconnect-pos","nexaconnect-pos://oauth/callback","openid","http://127.0.0.1:49199/","http://127.0.0.1:49199/",Guid.NewGuid(),Guid.NewGuid(),"THB","cash_manual",null,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),CatalogApi:"http://127.0.0.1:49199/");
 private static DrawerRequest Request(PosClientConfiguration c)=>new(c.OrganizationId,c.RestaurantId,c.BranchId,c.StoreId,c.TerminalId,Guid.NewGuid(),Guid.NewGuid(),"cashier",Guid.NewGuid(),DrawerCause.ConfirmedCashSale,Guid.NewGuid());
 private static PosTokenSet Token(PosClientConfiguration c)=>new("synthetic-token",null,DateTimeOffset.UtcNow.AddMinutes(5),"Bearer",SessionStartedAtUtc:DateTimeOffset.UtcNow,SessionExpiresAtUtc:DateTimeOffset.UtcNow.AddHours(1),Subject:"cashier",Authority:c.Authority);
 private static DrawerIntentV1 Receipt(DrawerRequest r,bool grant=true,string status="reserved"){var now=DateTimeOffset.UtcNow;return new(r.OperationId,status,grant,now,now.AddSeconds(60),status=="reserved"?null:now);}
 private SqliteDrawerIntentStore Store()=>new(Path.Combine(root,"drawer-intents.db"));
 [Fact]public async Task Http_adapter_uses_exact_scope_fixed_reason_bearer_correlation_and_original_report()
 {
  var c=Config();var r=Request(c) with{Cause=DrawerCause.Manual,OrderId=null,Reason=DrawerReason.CashCheck};
  var handler=new Handler(async(call,ct)=>{
   Assert.Equal(r.OperationId.ToString("D"),call.Headers.GetValues("X-Correlation-ID").Single());Assert.Equal("synthetic-token",call.Headers.Authorization!.Parameter);
   Assert.StartsWith($"/api/pos/v1/customer/organizations/{c.OrganizationId:D}/drawer-intents",call.RequestUri!.AbsolutePath);
   if(call.Method==HttpMethod.Get)return Ok(Receipt(r,false));
   if(call.RequestUri.AbsolutePath.EndsWith("/outcome")){var report=await call.Content!.ReadFromJsonAsync<DrawerOutcomeRequestV1>(ct);Assert.Equal(new(c.OrganizationId,"submitted"),report);return Ok(Receipt(r,false,"submitted"));}
   var bytes=await call.Content!.ReadAsStringAsync(ct);Assert.DoesNotContain("subject",bytes,StringComparison.OrdinalIgnoreCase);
   var payload=JsonSerializer.Deserialize<DrawerIntentRequestV1>(bytes,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;Assert.Equal("manual",payload.Cause);Assert.Equal("cash_check",payload.Reason);Assert.Equal(r.ShiftId,payload.ShiftId);Assert.Equal(r.CashSessionId,payload.CashSessionId);return Ok(Receipt(r));
  });
  using var authority=new HttpDrawerAuthority(c,()=>Token(c),handler,NullLogger.Instance);
  Assert.True((await authority.AuthorizeAsync(r,default)).AllowsSubmission(DateTimeOffset.UtcNow));await authority.ReportAsync(r,DrawerOutcome.Submitted,default);
  Assert.False((await authority.ReadAsync(r,default)).MaySubmit);Assert.Equal(3,handler.Calls);
 }
 [Theory][InlineData("subject")][InlineData("locked")][InlineData("expired")][InlineData("issuer")][InlineData("scope")][InlineData("absolute")][InlineData("missing-absolute")]
 public async Task Changed_identity_lock_or_scope_never_sends_credentials(string change)
 {
  var c=Config();var r=Request(c);var token=Token(c);token=change switch{"subject"=>token with{Subject="other"},"locked"=>token with{LockedAtUtc=DateTimeOffset.UtcNow},"expired"=>token with{ExpiresAtUtc=DateTimeOffset.UtcNow.AddSeconds(-1)},"issuer"=>token with{Authority="https://wrong.invalid/"},"absolute"=>token with{SessionExpiresAtUtc=DateTimeOffset.UtcNow.AddSeconds(-1)},_=>token};
  if(change=="missing-absolute")token=token with{SessionExpiresAtUtc=null};
  if(change=="scope")r=r with{StoreId=Guid.NewGuid()};var handler=new Handler((_,_)=>throw new InvalidOperationException());
  using var authority=new HttpDrawerAuthority(c,()=>token,handler,NullLogger.Instance);await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>authority.AuthorizeAsync(r,default));Assert.Equal(0,handler.Calls);
 }
 [Fact]public async Task Local_intent_is_durable_before_http_and_lost_response_cannot_be_replayed()
 {
  var c=Config();var r=Request(c);var handler=new Handler((_,_)=>{Assert.Equal(DrawerReservation.AlreadyAttempted,Store().Reserve(r));throw new HttpRequestException("synthetic lost response");});
  using var authority=new HttpDrawerAuthority(c,()=>Token(c),handler,NullLogger.Instance);var driver=new Driver();var flow=new DrawerWorkflow(driver,authority,Store());
  Assert.Equal(DrawerOutcome.AuditUnavailable,await flow.OpenAsync(r,()=>true,default));Assert.Equal(DrawerOutcome.AlreadyAttempted,await flow.OpenAsync(r with{OperationId=Guid.NewGuid()},()=>true,default));Assert.Equal(1,handler.Calls);Assert.Equal(0,driver.Calls);
 }
 [Fact]public async Task Replayed_server_grant_never_opens_even_with_empty_local_journal()
 {
  var c=Config();var r=Request(c);var handler=new Handler((_,_)=>Task.FromResult(Ok(Receipt(r,false))));using var authority=new HttpDrawerAuthority(c,()=>Token(c),handler,NullLogger.Instance);var driver=new Driver();
  Assert.Equal(DrawerOutcome.AlreadyAttempted,await new DrawerWorkflow(driver,authority,Store()).OpenAsync(r,()=>true,default));Assert.Equal(0,driver.Calls);
 }
 [Fact]public async Task Cancellation_after_headers_aborts_body_read_and_consumes_intent_without_opening()
 {
  var c=Config();var r=Request(c);using var cancelled=new CancellationTokenSource();
  var handler=new Handler((_,_)=>{cancelled.Cancel();return Task.FromResult(Ok(Receipt(r)));});
  using var authority=new HttpDrawerAuthority(c,()=>Token(c),handler,NullLogger.Instance);var driver=new Driver();
  Assert.Equal(DrawerOutcome.AuditUnavailable,await new DrawerWorkflow(driver,authority,Store()).OpenAsync(r,()=>true,cancelled.Token));
  Assert.Equal(0,driver.Calls);Assert.Equal(1,handler.Calls);Assert.Equal(DrawerReservation.AlreadyAttempted,Store().Reserve(r));
 }
 [Fact]public async Task Failed_report_preserves_uncertainty_without_repeating_device_or_changing_paid_result()
 {
  var c=Config();var r=Request(c);var handler=new Handler((call,_)=>Task.FromResult(call.RequestUri!.AbsolutePath.EndsWith("/outcome")?new HttpResponseMessage(HttpStatusCode.ServiceUnavailable):Ok(Receipt(r))));
  using var authority=new HttpDrawerAuthority(c,()=>Token(c),handler,NullLogger.Instance);var driver=new Driver();using var coordinator=new DrawerCoordinator(new(driver,authority,Store()));
  var attempt=Attempt(r);var paid=Paid(r);
  Assert.Equal(DrawerOutcome.AuditUnavailableAfterAttempt,await coordinator.AfterPaymentAsync(paid,attempt,r,false,()=>true,default));
  Assert.Equal(DrawerOutcome.AlreadyAttempted,await coordinator.AfterPaymentAsync(paid,attempt,r,false,()=>true,default));Assert.Equal(1,driver.Calls);Assert.Equal("Paid",paid.Status);Assert.Equal(attempt,Attempt(r));
 }
 [Fact]public async Task Expired_or_locked_grant_is_cancelled_and_reported_without_device_submission()
 {
  var c=Config();var r=Request(c);var auth=new Authority{Expiry=DateTimeOffset.UtcNow.AddSeconds(-1)};var driver=new Driver();
  Assert.Equal(DrawerOutcome.Cancelled,await new DrawerWorkflow(driver,auth,Store()).OpenAsync(r,()=>true,default));Assert.Equal(DrawerOutcome.Cancelled,auth.Reported);Assert.Equal(0,driver.Calls);
  bool current=true;auth=new(){After=()=>current=false};r=r with{OrderId=Guid.NewGuid()};Assert.Equal(DrawerOutcome.Cancelled,await new DrawerWorkflow(driver,auth,Store()).OpenAsync(r,()=>current,default));Assert.Equal(0,driver.Calls);
 }
 [Theory][InlineData("operation")][InlineData("oversized")][InlineData("lease")][InlineData("future")]
 public async Task Invalid_success_response_never_reaches_driver(string change)
 {
  var c=Config();var r=Request(c);var receipt=Receipt(r);receipt=change switch{"operation"=>receipt with{OperationId=Guid.NewGuid()},"lease"=>receipt with{ExpiresAtUtc=receipt.ExpiresAtUtc.AddSeconds(1)},"future"=>receipt with{ReservedAtUtc=receipt.ReservedAtUtc.AddHours(1),ExpiresAtUtc=receipt.ExpiresAtUtc.AddHours(1)},_=>receipt};
  var handler=new Handler((_,_)=>Task.FromResult(change=="oversized"?new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(new string('x',4097))}:Ok(receipt)));
  using var authority=new HttpDrawerAuthority(c,()=>Token(c),handler,NullLogger.Instance);var driver=new Driver();Assert.Equal(DrawerOutcome.AuditUnavailable,await new DrawerWorkflow(driver,authority,Store()).OpenAsync(r,()=>true,default));Assert.Equal(0,driver.Calls);
 }
 [Theory][InlineData("promptpay")][InlineData("card")][InlineData("replayed")][InlineData("recovery")][InlineData("order")][InlineData("amount")][InlineData("old")]
 public async Task Only_fresh_original_confirmed_cash_completion_is_eligible(string change)
 {
  var r=Request(Config());var auth=new Authority();var driver=new Driver();using var coordinator=new DrawerCoordinator(new(driver,auth,Store()));var paid=Paid(r);var attempt=Attempt(r);
  paid=change switch{"promptpay"=>paid with{Method="promptpay_manual"},"card"=>paid with{Method="card_omise_test"},"replayed"=>paid with{Replayed=true},"order"=>paid with{OrderId=Guid.NewGuid()},"amount"=>paid with{Amount=20},"old"=>paid with{OccurredAtUtc=DateTimeOffset.UtcNow.AddMinutes(-2)},_=>paid};
  Assert.Equal(DrawerOutcome.Cancelled,await coordinator.AfterPaymentAsync(paid,attempt,r,change=="recovery",()=>true,default));Assert.Equal(0,auth.Calls);Assert.Equal(0,driver.Calls);Assert.False(Directory.Exists(root));
 }
 [Fact]public async Task Manual_confirmation_and_current_session_are_required_and_default_driver_has_no_side_effects()
 {
  var r=Request(Config()) with{Cause=DrawerCause.Manual,OrderId=null,Reason=DrawerReason.SupervisorAccess};var auth=new Authority();var driver=new Driver();using var coordinator=new DrawerCoordinator(new(driver,auth,Store()));
  Assert.Equal(DrawerOutcome.Cancelled,await coordinator.ManualAsync(r,false,()=>true,default));Assert.Equal(DrawerOutcome.Cancelled,await coordinator.ManualAsync(r,true,()=>false,default));Assert.Equal(0,auth.Calls);
  using var disabled=new DrawerCoordinator(new(new DisabledDrawerDriver(),auth,Store()));Assert.Equal(DrawerOutcome.Disabled,await disabled.ManualAsync(r,true,()=>true,default));Assert.False(Directory.Exists(root));
 }
 private static LocalPendingSettlementState Attempt(DrawerRequest r)=>new(r.OrderId!.Value,10,"THB",r.OperationId,"cash");
 private static ManualTenderResult Paid(DrawerRequest r)=>new(Guid.NewGuid(),r.OrderId!.Value,"Paid","cash",10,"THB",DateTimeOffset.UtcNow.AddSeconds(-1),false);
 private static HttpResponseMessage Ok(DrawerIntentV1 receipt)=>new(HttpStatusCode.OK){Content=JsonContent.Create(receipt)};
 private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
 {public int Calls;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){Calls++;return send(r,ct);}}
 private sealed class Driver:IDrawerDriver{public bool Configured=>true;public int Calls;public Task SubmitOpenAsync(CancellationToken ct){Calls++;return Task.CompletedTask;}}
 private sealed class Authority:IDrawerAuthorization
 {
  public int Calls;public Action? After;public DateTimeOffset? Expiry;public DrawerOutcome? Reported;
  public Task<DrawerPermit> AuthorizeAsync(DrawerRequest r,CancellationToken ct){Calls++;After?.Invoke();return Task.FromResult(new DrawerPermit(DrawerPermitStatus.Granted,Expiry??DateTimeOffset.UtcNow.AddSeconds(60)));}
  public Task ReportAsync(DrawerRequest r,DrawerOutcome outcome,CancellationToken ct){Reported=outcome;return Task.CompletedTask;}
 }
 public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
