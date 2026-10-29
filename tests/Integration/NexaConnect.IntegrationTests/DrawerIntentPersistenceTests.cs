extern alias POS;
extern alias AUTH;
using Npgsql;
using POS::NexaConnect.Services.POS.Domain.Drawers;
using POS::NexaConnect.Services.POS.Infrastructure.Persistence;
namespace NexaConnect.IntegrationTests;

public sealed class DrawerIntentPersistenceTests
{
 [ConfiguredEnvironmentFact("NEXACONNECT_DRAWER_INTEGRATION_DB")]
 public async Task Durable_concurrent_reservations_reports_and_history_never_repeat_or_change_money()
 {
  await using var f=await Fixture.Create();var request=f.Command;var repo=new PostgresDrawerIntentRepository(f.Source);
  Task<(DrawerIntent Intent,bool Created)> Reserve(DrawerCommand c){var decision=Guid.NewGuid();return repo.ReserveAsync(c,"cashier",decision,(custody,now)=>DrawerIntent.Reserve(c,"cashier",decision,custody,now),default);}
  var results=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>Reserve(request)));Assert.Single(results,r=>r.Created);
  var original=results[0].Intent;Assert.All(results,r=>Assert.Equal(original.ExpiresAtUtc,r.Intent.ExpiresAtUtc));
  Assert.Equal(1,await f.Count("drawer_intents"));Assert.Equal(1,await f.Count("drawer_intent_audit"));Assert.Equal(1,await f.Count("outbox_messages"));
  Assert.False((await Reserve(request)).Created);await Assert.ThrowsAsync<DrawerConflictException>(()=>Reserve(request with{Reason="change_required"}));
  Assert.Null(await repo.FindAsync(Guid.NewGuid(),request.OperationId,default));
  var restarted=new PostgresDrawerIntentRepository(f.Source);Assert.Equal(original,await restarted.FindAsync(request.Scope.OrganizationId,request.OperationId,default));
  var reported=await restarted.ReportAsync(original,"unknown",Guid.NewGuid(),default);Assert.Equal("unknown",reported.Status);
  Assert.Equal(reported,await restarted.ReportAsync(original,"unknown",Guid.NewGuid(),default));
  await Assert.ThrowsAsync<DrawerConflictException>(()=>restarted.ReportAsync(original,"submitted",Guid.NewGuid(),default));
  Assert.False((await Reserve(request)).Created);Assert.Equal(2,await f.Count("drawer_intent_audit"));Assert.Equal(2,await f.Count("outbox_messages"));
  foreach(string table in new[]{"drawer_intents","drawer_intent_outcomes","drawer_intent_audit"}){
   await Assert.ThrowsAsync<PostgresException>(()=>f.Sql($"DELETE FROM {table}"));
  }
  Assert.Equal(0,await f.Count("cash_movements"));Assert.Equal(0,await f.Count("pos_order_settlements"));
  Assert.Equal("open",await f.Scalar("SELECT status FROM cash_sessions"));
  await Assert.ThrowsAsync<PostgresException>(()=>f.Script("POS","0024_drawer_intents","down.sql"));Assert.Equal(1,await f.Count("drawer_intents"));
 }
 [ConfiguredEnvironmentFact("NEXACONNECT_DRAWER_INTEGRATION_DB")]
 public async Task Custody_cash_evidence_and_audit_outbox_failures_fence_new_intents_and_roll_back()
 {
  await using var f=await Fixture.Create();var repo=new PostgresDrawerIntentRepository(f.Source);
  Task<(DrawerIntent Intent,bool Created)> Reserve(DrawerCommand c){var decision=Guid.NewGuid();return repo.ReserveAsync(c,"cashier",decision,(custody,now)=>DrawerIntent.Reserve(c,"cashier",decision,custody,now),default);}
  await f.Sql("UPDATE terminals SET registration_status='suspended'");await Assert.ThrowsAsync<DrawerConflictException>(()=>Reserve(f.Command));
  await f.Sql("UPDATE terminals SET registration_status='active';UPDATE cash_sessions SET status='counting'");await Assert.ThrowsAsync<DrawerConflictException>(()=>Reserve(f.Command));
  await f.Sql("UPDATE cash_sessions SET status='open'");
  await Assert.ThrowsAsync<DrawerConflictException>(()=>Reserve(f.Command with{Scope=f.Command.Scope with{TerminalId=Guid.NewGuid()}}));
  var sale=f.Command with{Cause="cash_sale",Reason=null,OrderId=Guid.NewGuid()};await Assert.ThrowsAsync<DrawerConflictException>(()=>Reserve(sale));
  var s=sale.Scope;
  await f.Sql("INSERT INTO pos_order_settlements(event_id,settlement_id,order_id,organization_id,restaurant_id,branch_id,terminal_id,cash_session_id,method,amount,currency,occurred_at_utc,projected_at_utc) VALUES($1,$2,$3,$4,$5,$6,$7,$8,'cash',10,'THB',clock_timestamp()-interval '1 second',clock_timestamp())",Guid.NewGuid(),Guid.NewGuid(),sale.OrderId!.Value,s.OrganizationId,s.RestaurantId,s.BranchId,s.TerminalId,s.CashSessionId);
  var first=await Reserve(sale);Assert.True(first.Created);Assert.False((await Reserve(sale with{OperationId=Guid.NewGuid()})).Created);
  // Exactly the same original sale under a changed operation cannot create a second intent.
  Assert.Equal(1,await f.Count("drawer_intents"));
  await f.Sql("ALTER TABLE outbox_messages ADD CONSTRAINT drawer_fixture_reject CHECK(event_type<>'pos.audit.v1') NOT VALID");
  await Assert.ThrowsAsync<PostgresException>(()=>Reserve(f.Command with{OperationId=Guid.NewGuid()}));Assert.Equal(1,await f.Count("drawer_intents"));
  await Assert.ThrowsAsync<PostgresException>(()=>repo.ReportAsync(first.Intent,"submitted",Guid.NewGuid(),default));Assert.Equal(0,await f.Count("drawer_intent_outcomes"));
  Assert.Equal("reserved",(await repo.FindAsync(s.OrganizationId,sale.OperationId,default))!.Status);
  await f.Sql("ALTER TABLE outbox_messages DROP CONSTRAINT drawer_fixture_reject;UPDATE cash_sessions SET status='counting'");
  // Late outcome recording is evidence only and cannot issue a new opening grant.
  Assert.Equal("unknown",(await repo.ReportAsync(first.Intent,"unknown",Guid.NewGuid(),default)).Status);
  Assert.Equal(0,await f.Count("cash_movements"));Assert.Equal(1,await f.Count("pos_order_settlements"));
 }
 [ConfiguredEnvironmentFact("NEXACONNECT_DRAWER_INTEGRATION_DB")]
 public async Task Permission_upgrade_and_new_role_defaults_keep_manual_open_separate_from_cashiers()
 {
  await using var f=await Fixture.Create("Authorization",15);
  foreach(var role in new[]{"tenant-admin","store-manager","cashier","accountant","kitchen-operator"})await f.Sql("INSERT INTO authorization_roles(id,organization_id,code,name,status) VALUES($1,$2,$3,$3,'active')",Guid.NewGuid(),Guid.NewGuid(),role);
  await f.Script("Authorization","0016_drawer_permissions","up.sql");
  foreach(var role in new[]{"tenant-admin","store-manager","cashier","accountant","kitchen-operator"}){
   var expected=role is "tenant-admin" or "store-manager"?3:role=="cashier"?2:0;
   Assert.Equal((long)expected,await f.Scalar("SELECT count(*) FROM authorization_role_permissions p JOIN authorization_roles r ON r.id=p.role_id WHERE r.code=$1 AND p.permission_code LIKE 'pos.drawer.%'",role));
   var defaults=AUTH::NexaConnect.Services.Authorization.Domain.ProductRoleDefaults.PermissionsFor(role);
   Assert.Equal(role is "tenant-admin" or "store-manager",defaults.Contains("pos.drawer.manual-open"));
  }
  await f.Script("Authorization","0016_drawer_permissions","down.sql");Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM authorization_role_permissions WHERE permission_code LIKE 'pos.drawer.%'"));
 }
 private sealed class Fixture:IAsyncDisposable
 {
  private readonly NpgsqlConnection admin;private readonly string schema;
  public NpgsqlDataSource Source{get;}public DrawerCommand Command{get;}
  private Fixture(NpgsqlConnection admin,string schema,NpgsqlDataSource source){this.admin=admin;this.schema=schema;Source=source;Command=new(new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid()),Guid.NewGuid(),"manual",null,"cash_check");}
  public static async Task<Fixture> Create(string service="POS",int maximum=24)
  {
   var settings=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NEXACONNECT_DRAWER_INTEGRATION_DB"));Assert.Equal("127.0.0.1",settings.Host);Assert.Matches("^nexa_drawer_it_[a-f0-9]{32}$",settings.Database!);
   var admin=new NpgsqlConnection(settings.ConnectionString);await admin.OpenAsync();string schema="drawer_"+Guid.NewGuid().ToString("N");await using(var create=new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"",admin))await create.ExecuteNonQueryAsync();
   settings.SearchPath=schema;var fixture=new Fixture(admin,schema,NpgsqlDataSource.Create(settings.ConnectionString));
   try{
    var root=Path.Combine(Root,service);foreach(var dir in Directory.GetDirectories(root).Order().Where(d=>int.Parse(Path.GetFileName(d)[..4])<=maximum)){await using var sql=fixture.Source.CreateCommand(await File.ReadAllTextAsync(Path.Combine(dir,"up.sql")));await sql.ExecuteNonQueryAsync();}
    if(service=="POS"){
     await fixture.Script("POS","0024_drawer_intents","down.sql");await fixture.Script("POS","0024_drawer_intents","up.sql");
     var s=fixture.Command.Scope;
     await fixture.Sql("INSERT INTO stores(id,restaurant_id,branch_id,code,name,operational_status,created_at_utc,created_by,updated_at_utc,updated_by) VALUES($1,$2,$3,'fixture','Fixture','active',now(),'fixture',now(),'fixture')",s.StoreId,s.RestaurantId,s.BranchId);
     await fixture.Sql("INSERT INTO terminals(id,restaurant_id,store_id,code,device_type,registration_status,registered_at_utc,created_at_utc,updated_at_utc) VALUES($1,$2,$3,'fixture','pos','active',now(),now(),now())",s.TerminalId,s.RestaurantId,s.StoreId);
     await fixture.Sql("INSERT INTO shifts(id,store_id,terminal_id,employee_identity_subject_id,shift_number,status,opened_at_utc,opened_by,created_at_utc,updated_at_utc,authorization_decision_id) VALUES($1,$2,$3,'cashier','fixture','open',now()-interval '1 minute','cashier',now(),now(),$4)",s.ShiftId,s.StoreId,s.TerminalId,Guid.NewGuid());
     await fixture.Sql("INSERT INTO cash_sessions(id,store_id,shift_id,currency,opening_amount,status,opened_at_utc,created_at_utc,updated_at_utc) VALUES($1,$2,$3,'THB',0,'open',now()-interval '1 minute',now(),now())",s.CashSessionId,s.StoreId,s.ShiftId);
    }
    return fixture;
   }catch{await fixture.DisposeAsync();throw;}
  }
  private static string Root=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../src/Tools/NexaConnect.DataMigration/Scripts"));
  public async Task Script(string service,string migration,string file)=>await Sql(await File.ReadAllTextAsync(Path.Combine(Root,service,migration,file)));
  public async Task Sql(string sql,params object[] values){await using var command=Source.CreateCommand(sql);foreach(var v in values)command.Parameters.AddWithValue(v);await command.ExecuteNonQueryAsync();}
  public async Task<object?> Scalar(string sql,params object[] values){await using var command=Source.CreateCommand(sql);foreach(var v in values)command.Parameters.AddWithValue(v);return await command.ExecuteScalarAsync();}
  public async Task<long> Count(string table)=> (long)(await Scalar("SELECT count(*) FROM "+table))!;
  public async ValueTask DisposeAsync(){await Source.DisposeAsync();await using(var drop=new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE",admin))await drop.ExecuteNonQueryAsync();await admin.DisposeAsync();}
 }
}
