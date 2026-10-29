extern alias RESTAURANT;
using Npgsql;
using Repo=RESTAURANT::NexaConnect.Services.Restaurant.Infrastructure.Persistence.PostgresBranchPromptPayQrRepository;
using Approval=RESTAURANT::NexaConnect.Services.Restaurant.Domain.PromptPayQrApproval;
using PricingRepo=RESTAURANT::NexaConnect.Services.Restaurant.Infrastructure.Persistence.PostgresBranchProductConfigurationRepository;
using PricingUpdate=RESTAURANT::NexaConnect.Services.Restaurant.Application.Configuration.UpdateBranchProductConfigurationCommand;
namespace NexaConnect.IntegrationTests;

public sealed class PromptPayQrPersistenceTests
{
 [ConfiguredEnvironmentFact("NEXACONNECT_QR_INTEGRATION_DB")]
 public async Task Approval_is_scoped_compare_and_swap_and_atomic_with_audit_preserving_other_configuration()
 {
  var settings=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NEXACONNECT_QR_INTEGRATION_DB"));
  Assert.Equal("127.0.0.1",settings.Host);Assert.Matches("^nexa_qr_it_[a-f0-9]{32}$",settings.Database!);
  string schema="promptpay_"+Guid.NewGuid().ToString("N");await using var admin=new NpgsqlConnection(settings.ConnectionString);await admin.OpenAsync();
  try{
   await using(var create=new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"",admin))await create.ExecuteNonQueryAsync();
   settings.SearchPath=schema;await using var source=NpgsqlDataSource.Create(settings.ConnectionString);
   string scripts=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../src/Tools/NexaConnect.DataMigration/Scripts/Restaurant"));
   foreach(var directory in Directory.GetDirectories(scripts).Order()){await using var migration=source.CreateCommand(await File.ReadAllTextAsync(Path.Combine(directory,"up.sql")));await migration.ExecuteNonQueryAsync();}
   Guid org=Guid.NewGuid(),restaurant=Guid.NewGuid(),branch=Guid.NewGuid(),asset=Guid.NewGuid();
   await using(var seed=source.CreateCommand("INSERT INTO restaurants(id,organization_id,code,name,default_currency,default_time_zone,status,created_at_utc,updated_at_utc,created_by,updated_by) VALUES($1,$2,'fixture','Fixture','THB','Asia/Singapore','active',now(),now(),'fixture','fixture');")){seed.Parameters.AddWithValue(restaurant);seed.Parameters.AddWithValue(org);await seed.ExecuteNonQueryAsync();}
   await using(var seed=source.CreateCommand("INSERT INTO branches(id,restaurant_id,code,name,time_zone,currency,status,created_at_utc,updated_at_utc,created_by,updated_by) VALUES($1,$2,'fixture','Fixture','Asia/Singapore','THB','active',now(),now(),'fixture','fixture');")){seed.Parameters.AddWithValue(branch);seed.Parameters.AddWithValue(restaurant);await seed.ExecuteNonQueryAsync();}
   var repo=new Repo(source);Assert.Null((await repo.GetAsync(org,branch,default))!.AssetId);
   Assert.Null(await repo.GetAsync(Guid.NewGuid(),branch,default));Assert.Null(await repo.SaveAsync(Guid.NewGuid(),branch,new Approval(1,asset,true),"fixture",default));
   var first=await repo.SaveAsync(org,branch,new(1,asset,true),"fixture",default);Assert.Equal(2,first!.Version);Assert.Equal(asset,first.AssetId);
   Assert.Null(await repo.SaveAsync(org,branch,new(1,Guid.NewGuid(),true),"fixture",default));
   var races=await Task.WhenAll(repo.SaveAsync(org,branch,new(2,asset,true),"fixture",default),repo.SaveAsync(org,branch,new(2,null,true),"fixture",default));Assert.Single(races,r=>r is not null);
   var before=await repo.GetAsync(org,branch,default);var pricing=new PricingRepo(source);
   Assert.NotNull(await pricing.UpdateAsync(org,branch,new PricingUpdate(true,true,false,10,3,7,false),"fixture",default));
   var after=await repo.GetAsync(org,branch,default);Assert.Equal(4,after!.Version);Assert.Equal(before!.AssetId,after.AssetId);
   await using(var fail=source.CreateCommand("ALTER TABLE outbox_messages ADD CONSTRAINT promptpay_test_reject CHECK(false) NOT VALID;"))await fail.ExecuteNonQueryAsync();
   await Assert.ThrowsAsync<PostgresException>(()=>repo.SaveAsync(org,branch,new(4,Guid.NewGuid(),true),"fixture",default));
   Assert.Equal(after,await repo.GetAsync(org,branch,default));
   await using(var count=source.CreateCommand("SELECT (SELECT count(*) FROM branch_management_audit),(SELECT count(*) FROM outbox_messages);"))await using(var rows=await count.ExecuteReaderAsync()){await rows.ReadAsync();Assert.Equal(3,rows.GetInt64(0));Assert.Equal(3,rows.GetInt64(1));}
   await using(var fix=source.CreateCommand("ALTER TABLE outbox_messages DROP CONSTRAINT promptpay_test_reject;"))await fix.ExecuteNonQueryAsync();
   var removed=await repo.SaveAsync(org,branch,new(4,null,true),"fixture",default);Assert.Null(removed!.AssetId);
   await using(var inactive=source.CreateCommand("UPDATE branches SET status='suspended' WHERE id=$1;")){inactive.Parameters.AddWithValue(branch);await inactive.ExecuteNonQueryAsync();}
   Assert.Null(await repo.GetAsync(org,branch,default));Assert.Null(await repo.SaveAsync(org,branch,new(5,asset,true),"fixture",default));
  }finally{await using var drop=new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE",admin);await drop.ExecuteNonQueryAsync();}
 }
}
