using System.Text.Json;
using NexaConnect.Services.Authorization.Application.Assignments;
using NexaConnect.Services.Authorization.Infrastructure.Persistence;
using NexaConnect.Services.Restaurant.Application.Provisioning;
using NexaConnect.Services.Restaurant.Infrastructure.Persistence;
using Npgsql;
namespace NexaConnect.FinancialPortalAcceptance.Infrastructure;
internal sealed record CorrectionReference(Guid BranchId,Guid StoreId,Guid TerminalId,Guid ProductId);
internal static class LateCashCorrectionAcceptanceFixture
{
 public static async Task ExecuteAsync(FixtureOptions options,string action,CancellationToken ct)
 {
  if(!options.Cutoff||Environment.GetEnvironmentVariable("NEXACONNECT_FINANCIAL_PORTAL_CASHIER_DAY_CLOSE")!="1")throw new ArgumentException();
  var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);var state=JsonSerializer.Deserialize<FixtureState>(await File.ReadAllTextAsync(options.StatePath,ct),json)??throw new InvalidOperationException();
  if(state.RunId!=options.RunId||state.Cashier is null)throw new InvalidOperationException();
  var path=Path.Combine(Path.GetDirectoryName(options.StatePath)!,"correction-reference.json");
  await using var pos=NpgsqlDataSource.Create(options.Connection("pos"));await using var auth=NpgsqlDataSource.Create(options.Connection("authorization"));
  if(action=="correction-reference")
  {
   if(File.Exists(path))throw new InvalidOperationException();await using var restaurant=NpgsqlDataSource.Create(options.Connection("restaurant"));
   var branch=await new RestaurantProvisioningService(new PostgresRestaurantProvisioningRepository(restaurant)).CreateBranchAsync(state.RestaurantId,new("correction","Correction acceptance","THB","Asia/Bangkok"),"acceptance",ct)??throw new InvalidOperationException();
   var reference=new CorrectionReference(branch.BranchId,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());
   await using var store=pos.CreateCommand("INSERT INTO stores(id,restaurant_id,branch_id,code,name,operational_status,created_at_utc,created_by,updated_at_utc,updated_by) VALUES($1,$2,$3,'correction','Correction acceptance','active',now(),'acceptance',now(),'acceptance')");store.Parameters.AddWithValue(reference.StoreId);store.Parameters.AddWithValue(state.RestaurantId);store.Parameters.AddWithValue(reference.BranchId);await store.ExecuteNonQueryAsync(ct);
   var assignments=new AuthorizationAssignmentService(new PostgresAuthorizationAssignmentRepository(auth));
   await assignments.AssignAsync(new(options.Reader,state.OrganizationId,state.RestaurantId,reference.BranchId,"cashier"),"acceptance",ct);
   await assignments.AssignAsync(new(options.Accountant!,state.OrganizationId,state.RestaurantId,reference.BranchId,"accountant"),"acceptance",ct);
   await using var enrollment=auth.CreateCommand("INSERT INTO authorization_user_permission_overrides(id,subject_id,scope_id,permission_code,effect,status) SELECT $1,$2,id,'pos.terminal.enroll','allow','active' FROM authorization_resource_scopes WHERE organization_id=$3 AND restaurant_id=$4 AND branch_id=$5 AND status='active'");
   foreach(var value in new object[]{Guid.NewGuid(),options.Resolver,state.OrganizationId,state.RestaurantId,reference.BranchId})enrollment.Parameters.AddWithValue(value);if(await enrollment.ExecuteNonQueryAsync(ct)!=1)throw new InvalidOperationException();
   await using var limit=auth.CreateCommand("INSERT INTO financial_approval_limits(id,restaurant_id,principal_type,principal_id,action_code,currency,maximum_amount,status) VALUES($1,$2,'subject',$3,'pos.day-close.late-cash.post','THB',1000,'active')");limit.Parameters.AddWithValue(Guid.NewGuid());limit.Parameters.AddWithValue(state.RestaurantId);limit.Parameters.AddWithValue(options.Resolver);await limit.ExecuteNonQueryAsync(ct);
   await File.WriteAllTextAsync(path,JsonSerializer.Serialize(reference,json),ct);Console.WriteLine(JsonSerializer.Serialize(reference,json));return;
  }
  var refs=JsonSerializer.Deserialize<CorrectionReference>(await File.ReadAllTextAsync(path,ct),json)??throw new InvalidOperationException();
  if(action is "limit-cash-correction" or "restore-cash-correction-limit")
  {
   await using var limit=auth.CreateCommand("UPDATE financial_approval_limits SET maximum_amount=$1 WHERE restaurant_id=$2 AND principal_type='subject' AND principal_id=$3 AND action_code='pos.day-close.late-cash.post' AND currency='THB' AND status='active'");foreach(var value in new object[]{action=="limit-cash-correction"?5m:1000m,state.RestaurantId,options.Resolver})limit.Parameters.AddWithValue(value);if(await limit.ExecuteNonQueryAsync(ct)!=1)throw new InvalidOperationException();Console.WriteLine("limit_updated");return;
  }
  if(action is "revoke-cash-correction" or "restore-cash-correction")
  {
   await using var permission=auth.CreateCommand("UPDATE authorization_user_permission_overrides o SET effect=$1 FROM authorization_resource_scopes s WHERE o.scope_id=s.id AND o.subject_id=$2 AND s.organization_id=$3 AND s.restaurant_id=$4 AND s.branch_id IS NULL AND o.permission_code='pos.day-close.late-cash.post'");
   foreach(var value in new object[]{action=="revoke-cash-correction"?"deny":"allow",options.Resolver,state.OrganizationId,state.RestaurantId})permission.Parameters.AddWithValue(value);
   if(await permission.ExecuteNonQueryAsync(ct)!=1)throw new InvalidOperationException();Console.WriteLine("permission_updated");return;
  }
  if(action!="cash-correction-proof")throw new ArgumentException();
  await using var proof=pos.CreateCommand("""
   SELECT a.authorization_decision_id,
    (SELECT count(*) FROM late_cash_corrections WHERE organization_id=$1 AND branch_id=$2),
    (SELECT count(*) FROM late_cash_correction_audit x WHERE x.organization_id=a.organization_id AND x.correction_id=a.id),
    (SELECT count(*) FROM outbox_messages x WHERE x.event_type='pos.late-cash-correction-posted.v1' AND x.aggregate_id=a.id),
    (SELECT count(*) FROM pos_order_settlements WHERE organization_id=$1 AND branch_id=$2),
    (SELECT count(*) FROM cash_movements x WHERE x.cash_session_id=a.drawer_id),
    (SELECT count(*) FROM source_late_work WHERE organization_id=$1 AND work_id=a.work_id)
   FROM late_cash_corrections a WHERE a.organization_id=$1 AND a.branch_id=$2
   """);proof.Parameters.AddWithValue(state.OrganizationId);proof.Parameters.AddWithValue(refs.BranchId);Guid decision;
  await using(var rows=await proof.ExecuteReaderAsync(ct)){if(!await rows.ReadAsync(ct))throw new InvalidOperationException();decision=rows.GetGuid(0);for(var i=1;i<=6;i++){long expected=i is 4 or 5?0:1;if(rows.GetInt64(i)!=expected)throw new InvalidOperationException();}if(await rows.ReadAsync(ct))throw new InvalidOperationException();}
  await using var authority=auth.CreateCommand("SELECT count(*) FROM authorization_decisions WHERE id=$1 AND subject_id=$2 AND organization_id=$3 AND branch_id=$4 AND action_code='pos.day-close.late-cash.post' AND granted");foreach(var value in new object[]{decision,options.Resolver,state.OrganizationId,refs.BranchId})authority.Parameters.AddWithValue(value);if((long)(await authority.ExecuteScalarAsync(ct))! !=1)throw new InvalidOperationException();
  Console.WriteLine(JsonSerializer.Serialize(new{verified=true,corrections=1,audits=1,publications=1,custodyRetained=true,originalCashUnchanged=true,authorizationVerified=true}));
 }
}
