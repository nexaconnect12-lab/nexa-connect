extern alias POS;
using Npgsql;
using NexaConnect.Contracts.POS;
using POS::NexaConnect.Services.POS.Application.CashierPins;
using POS::NexaConnect.Services.POS.Infrastructure.Persistence;

namespace NexaConnect.IntegrationTests;

public sealed class CashierPinPersistenceTests
{
    [ConfiguredEnvironmentFact("NEXACONNECT_QR_INTEGRATION_DB")]
    public async Task Concurrent_enrollment_durable_attempts_isolation_and_security_history_are_atomic()
    {
        var settings=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NEXACONNECT_QR_INTEGRATION_DB"));
        Assert.Equal("127.0.0.1",settings.Host); Assert.Matches("^nexa_qr_it_[a-f0-9]{32}$",settings.Database!);
        string schema="pins_"+Guid.NewGuid().ToString("N");
        await using var admin=new NpgsqlConnection(settings.ConnectionString); await admin.OpenAsync();
        try
        {
            await using(var create=new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"",admin)) await create.ExecuteNonQueryAsync();
            settings.SearchPath=schema; await using var source=NpgsqlDataSource.Create(settings.ConnectionString);
            string scripts=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../src/Tools/NexaConnect.DataMigration/Scripts/POS/0026_cashier_pins"));
            await using(var up=source.CreateCommand(await File.ReadAllTextAsync(Path.Combine(scripts,"up.sql")))) await up.ExecuteNonQueryAsync();
            await using(var down=source.CreateCommand(await File.ReadAllTextAsync(Path.Combine(scripts,"down.sql")))) await down.ExecuteNonQueryAsync();
            await using(var reapply=source.CreateCommand(await File.ReadAllTextAsync(Path.Combine(scripts,"up.sql")))) await reapply.ExecuteNonQueryAsync();
            var repository=new PostgresCashierPinRepository(source); var key=new CashierPinKey(Guid.NewGuid(),"https://identity/realm","staff' OR 1=1 --");
            var scope=new CashierPinScopeV1(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());
            CashierPinAudit Audit(string action,Guid? id=null)=>new(id??Guid.NewGuid(),"synthetic-actor",action,Guid.NewGuid(),scope);
            var changes=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>repository.MutateAsync(key,Audit("change"),0,c=>c.Change(0,"opaque-protected-verifier",DateTimeOffset.UtcNow,_=>false),default)));
            Assert.Single(changes,x=>x.Accepted); Assert.Equal(1,(await repository.ReadAsync(key,default)).Version);
            foreach(var foreign in new[]{key with{OrganizationId=Guid.NewGuid()},key with{Issuer="https://other/realm"},key with{Subject="another"}})
                Assert.Equal("unset",(await repository.ReadAsync(foreign,default)).State);
            Guid repeated=Guid.NewGuid();
            for(int i=0;i<3;i++) Assert.False((await repository.MutateAsync(key,Audit("verify",repeated),1,c=>c.Verify(1,DateTimeOffset.UtcNow,_=>false),default)).Accepted);
            await using(var count=source.CreateCommand("SELECT failed_attempts FROM cashier_pin_credentials")) Assert.Equal(1,Convert.ToInt32(await count.ExecuteScalarAsync()));
            await Task.WhenAll(Enumerable.Range(0,8).Select(_=>repository.MutateAsync(key,Audit("verify"),1,c=>c.Verify(1,DateTimeOffset.UtcNow,_=>false),default)));
            var restarted=new PostgresCashierPinRepository(source);
            Assert.NotNull((await restarted.ReadAsync(key,default)).LockedUntilUtc);
            Assert.False((await restarted.MutateAsync(key,Audit("verify"),1,c=>c.Verify(1,DateTimeOffset.UtcNow,_=>true),default)).Accepted);
            Assert.True((await restarted.MutateAsync(key,Audit("reset"),1,c=>c.Administer(1,"reset"),default)).Accepted);
            Assert.True((await restarted.MutateAsync(key,Audit("change"),2,c=>c.Change(2,"next-protected-verifier",DateTimeOffset.UtcNow,_=>false),default)).Accepted);
            var success=Audit("verify"); Assert.True((await restarted.MutateAsync(key,success,3,c=>c.Verify(3,DateTimeOffset.UtcNow,_=>true),default)).Accepted);
            Assert.False((await restarted.MutateAsync(key,success,3,c=>c.Verify(3,DateTimeOffset.UtcNow,_=>true),default)).Accepted);
            foreach(string sql in new[]{"DELETE FROM cashier_pin_audit","UPDATE cashier_pin_audit SET actor='tampered'","TRUNCATE cashier_pin_audit"})
            { await using var immutable=source.CreateCommand(sql); await Assert.ThrowsAsync<PostgresException>(()=>immutable.ExecuteNonQueryAsync()); }
            await using(var rollback=source.CreateCommand(await File.ReadAllTextAsync(Path.Combine(scripts,"down.sql")))) await Assert.ThrowsAsync<PostgresException>(()=>rollback.ExecuteNonQueryAsync());
            await using(var audit=source.CreateCommand("SELECT row_to_json(a)::text FROM cashier_pin_audit a"))
            { await using var rows=await audit.ExecuteReaderAsync(); while(await rows.ReadAsync()) Assert.DoesNotContain("verifier",rows.GetString(0)); }
        }
        finally
        {
            Assert.Matches("^pins_[a-f0-9]{32}$",schema);
            await using var drop=new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE",admin); await drop.ExecuteNonQueryAsync();
        }
    }
}
