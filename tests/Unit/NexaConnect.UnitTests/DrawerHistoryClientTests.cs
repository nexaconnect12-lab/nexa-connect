using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using NexaConnect.Contracts.POS;
using NexaConnect.POS;
using NexaConnect.POS.Hardware.Drawers;
using Reader=NexaConnect.POS.Hardware.Drawers.DrawerHistoryReader;
namespace NexaConnect.UnitTests;

public sealed class DrawerHistoryClientTests:IDisposable
{
 private readonly string root=Path.Combine(Path.GetTempPath(),"nexa-history-client-"+Guid.NewGuid().ToString("N"));
 private string FileName=>Path.Combine(root,"drawer-intents.db");
 private static PosClientConfiguration Config()=>new("http://127.0.0.1:49199/realms/fixture","nexaconnect-pos","nexaconnect-pos://oauth/callback","openid","http://127.0.0.1:49199/","http://127.0.0.1:49199/",Guid.NewGuid(),Guid.NewGuid(),"THB","cash_manual",null,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),CatalogApi:"http://127.0.0.1:49199/");
 private static DrawerRequest Request(PosClientConfiguration c,string subject="cashier")=>new(c.OrganizationId,c.RestaurantId,c.BranchId,c.StoreId,c.TerminalId,Guid.NewGuid(),Guid.NewGuid(),subject,Guid.NewGuid(),DrawerCause.Manual,null,DrawerReason.CashCheck);
 private SqliteDrawerIntentStore Store(PosClientConfiguration c,IDrawerHistoryProtector? protector=null)=>new(FileName,c.Authority,protector??new FixtureProtector());
 private static PosTokenSet Token(PosClientConfiguration c)=>new("synthetic",null,DateTimeOffset.UtcNow.AddMinutes(10),"Bearer",SessionExpiresAtUtc:DateTimeOffset.UtcNow.AddHours(1),Subject:"cashier",Authority:c.Authority);
 private static DrawerHistoryItemV1 Item(DrawerRequest r){var at=DateTimeOffset.UtcNow.AddSeconds(-5);return new(r.OperationId,"manual","cash_check",null,r.ShiftId,r.CashSessionId,"reserved",at,at.AddSeconds(60),null);}
 private static DrawerHistoryPageV1 Page(PosClientConfiguration c,DateTimeOffset from,DateTimeOffset to,params DrawerHistoryItemV1[] items)=>new(c.OrganizationId,c.RestaurantId,c.BranchId,c.StoreId,c.TerminalId,from,to,items,null);
 [Fact]public void Reading_missing_or_legacy_journal_creates_no_files_or_metadata()
 {
  var c=Config();var store=Store(c);Assert.Empty(store.ReadHistory(c,"cashier",DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow));Assert.False(Directory.Exists(root));
  var legacy=new SqliteDrawerIntentStore(FileName);legacy.Reserve(Request(c));var before=SHA256.HashData(File.ReadAllBytes(FileName));
  Assert.Empty(store.ReadHistory(c,"cashier",DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow));Assert.Equal(before,SHA256.HashData(File.ReadAllBytes(FileName)));
 }
 [Fact]public void Protected_context_survives_restart_and_filters_actor_scope_and_issuer()
 {
  var c=Config();var r=Request(c);var other=Request(c,"other");var store=Store(c);store.Reserve(r);store.Reserve(other);store.Complete(r,DrawerOutcome.Unknown);
  var before=File.ReadAllBytes(FileName);var rows=Store(c).ReadHistory(c,"cashier",DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow);
  Assert.Single(rows);Assert.Equal(r,rows[0].Request);Assert.Equal("Unknown",rows[0].LocalStatus);Assert.Equal(before,File.ReadAllBytes(FileName));
  Assert.DoesNotContain(r.Subject,Encoding.UTF8.GetString(before));Assert.DoesNotContain(r.OperationId.ToString(),Encoding.UTF8.GetString(before));
  Assert.Empty(Store(c).ReadHistory(c with{BranchId=Guid.NewGuid()},"cashier",DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow));
  var otherIssuer=c with{Authority="http://127.0.0.1:49199/realms/other"};Assert.Empty(new SqliteDrawerIntentStore(FileName,otherIssuer.Authority,new FixtureProtector()).ReadHistory(otherIssuer,"cashier",DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow));
 }
 [Fact]public void Protection_failure_rolls_back_reservation_and_corrupt_metadata_is_not_displayed()
 {
  var c=Config();var r=Request(c);Assert.Throws<CryptographicException>(()=>Store(c,new BrokenProtector()).Reserve(r));Assert.Equal(DrawerReservation.Created,Store(c).Reserve(r));
  using(var db=new SqliteConnection("Data Source="+FileName+";Pooling=False")){db.Open();using var update=db.CreateCommand();update.CommandText="UPDATE drawer_history_context SET protected_context=X'00'";update.ExecuteNonQuery();}
  Assert.ThrowsAny<Exception>(()=>Store(c).ReadHistory(c,"cashier",DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow));Assert.Equal(DrawerReservation.AlreadyAttempted,Store(c).Reserve(r));
 }
 [Fact]public void Windows_current_user_protection_is_opaque_and_round_trips_when_available()
 {
  if(!OperatingSystem.IsWindows())return;
  var c=Config();var r=Request(c);var store=new SqliteDrawerIntentStore(FileName,c.Authority);store.Reserve(r);
  Assert.DoesNotContain(r.Subject,Encoding.UTF8.GetString(File.ReadAllBytes(FileName)));Assert.Equal(r,store.ReadHistory(c,"cashier",DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow).Single().Request);
 }
 [Fact]public async Task Valid_protected_context_cannot_be_substituted_between_cashiers()
 {
  var c=Config();var own=Request(c);var other=Request(c,"other");var store=Store(c);store.Reserve(own);store.Reserve(other);
  using(var db=new SqliteConnection("Data Source="+FileName+";Pooling=False")){db.Open();using var replace=db.CreateCommand();replace.CommandText="UPDATE drawer_history_context SET protected_context=(SELECT protected_context FROM drawer_history_context WHERE intent_key=$other) WHERE intent_key=$own";replace.Parameters.AddWithValue("$other",other.Key);replace.Parameters.AddWithValue("$own",own.Key);replace.ExecuteNonQuery();}
  Assert.Throws<InvalidDataException>(()=>store.ReadHistory(c,"cashier",DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow));
  var to=DateTimeOffset.UtcNow;using var reader=new Reader(c,new Port{Page=Page(c,to.AddDays(-1),to,Item(own))},store);var result=await reader.LoadAsync("cashier",to.AddDays(-1),to,null,default);
  Assert.False(result.LocalAvailable);Assert.Equal("cashier",Assert.Single(result.Rows).Context.Subject);Assert.Null(result.Rows[0].LocalStatus);
 }
 [Fact]public async Task Read_transport_sends_only_get_and_validates_scope_before_local_access()
 {
  var c=Config();var r=Request(c);DateTimeOffset to=DateTimeOffset.UtcNow,from=to.AddDays(-1);int gets=0;
  var handler=new Handler(call=>{gets++;Assert.Equal(HttpMethod.Get,call.Method);Assert.Null(call.Content);Assert.Equal("synthetic",call.Headers.Authorization!.Parameter);Assert.True(Guid.TryParse(call.Headers.GetValues("X-Correlation-ID").Single(),out _));
   return new(HttpStatusCode.OK){Content=call.RequestUri!.AbsolutePath.EndsWith("/review")?JsonContent.Create(Item(r)):JsonContent.Create(Page(c,from,to,Item(r)))};});
  using var remote=new HttpDrawerAuthority(c,()=>Token(c),handler,NullLogger.Instance);using var reader=new Reader(c,remote,Store(c));
  var batch=await reader.LoadAsync("cashier",from,to,null,default);var row=Assert.Single(batch.Rows);var checkedRow=await reader.CheckAsync(row,default);
  Assert.Equal("Reserved",checkedRow.ServerLabel);Assert.Equal(2,gets);Assert.False(Directory.Exists(root));
 }
 [Fact]public async Task Denied_authority_or_foreign_response_never_reads_local_history()
 {
  var c=Config();var local=new CountingLocal();var port=new Port{Denied=true};using var reader=new Reader(c,port,local);DateTimeOffset to=DateTimeOffset.UtcNow;
  await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>reader.LoadAsync("cashier",to.AddDays(-1),to,null,default));Assert.Equal(0,local.Calls);
  port.Denied=false;port.Page=Page(c with{OrganizationId=Guid.NewGuid()},to.AddDays(-1),to);await Assert.ThrowsAsync<InvalidDataException>(()=>reader.LoadAsync("cashier",to.AddDays(-1),to,null,default));Assert.Equal(0,local.Calls);
 }
 [Fact]public async Task Missing_server_record_is_guidance_only_and_never_completes_local_outcome()
 {
  var c=Config();var r=Request(c);Store(c).Reserve(r);var to=DateTimeOffset.UtcNow;var port=new Port{Page=Page(c,to.AddDays(-1),to)};
  using var reader=new Reader(c,port,Store(c));var batch=await reader.LoadAsync("cashier",to.AddDays(-1),to,null,default);var row=Assert.Single(batch.Rows);var before=File.ReadAllBytes(FileName);
  var checkedRow=await reader.CheckAsync(row,default);Assert.Equal("not_found",checkedRow.ReviewStatus);Assert.Contains("does not prove",checkedRow.Guidance);Assert.Equal(before,File.ReadAllBytes(FileName));Assert.Equal("Reserved",Store(c).ReadHistory(c,"cashier",to.AddDays(-1),to).Single().LocalStatus);
 }
 [Fact]public void Loading_more_does_not_erase_previously_checked_server_observation()
 {
  var c=Config();var r=Request(c);var item=Item(r);var old=new DrawerHistoryRow(r,item.ReservedAtUtc,"Unknown",item,"reserved");var local=new DrawerHistoryRow(r,item.ReservedAtUtc,"Unknown",null);
  var merged=Assert.Single(Reader.Merge([old],[local]));Assert.Equal("Reserved",merged.ServerLabel);Assert.Equal("Unknown",merged.LocalLabel);
 }
 private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> send):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)=>Task.FromResult(send(r));}
 private sealed class Port:IDrawerHistoryPort{public bool Denied;public DrawerHistoryPageV1? Page;public Task<DrawerHistoryPageV1> ListAsync(string s,DateTimeOffset from,DateTimeOffset to,string? cursor,CancellationToken ct)=>Denied?throw new UnauthorizedAccessException():Task.FromResult(Page!);public Task<DrawerHistoryItemV1?> ReviewAsync(string s,Guid op,CancellationToken ct)=>Task.FromResult<DrawerHistoryItemV1?>(null);}
 private sealed class CountingLocal:ILocalDrawerHistory{public int Calls;public IReadOnlyList<DrawerLocalHistoryEntry> ReadHistory(PosClientConfiguration c,string s,DateTimeOffset from,DateTimeOffset to,int limit=50){Calls++;return [];}}
 // Test-only reversible fixture codec. Production always defaults to Windows DPAPI.
 private sealed class FixtureProtector:IDrawerHistoryProtector{public byte[] Protect(byte[] b)=>b.Select(x=>(byte)(x^0xaa)).ToArray();public byte[] Unprotect(byte[] b)=>Protect(b);}
 private sealed class BrokenProtector:IDrawerHistoryProtector{public byte[] Protect(byte[] b)=>throw new CryptographicException();public byte[] Unprotect(byte[] b)=>throw new CryptographicException();}
 public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
