extern alias CATALOG;
using Npgsql;
using CATALOG::NexaConnect.Services.Catalog.Infrastructure;
using CATALOG::NexaConnect.Services.Catalog.Domain.Organization;
namespace NexaConnect.IntegrationTests;
public sealed class MenuOrganizationPersistenceTests
{
    [ConfiguredEnvironmentFact("NEXACONNECT_QR_INTEGRATION_DB")]
    public async Task Tenant_scoped_first_write_CAS_atomic_audit_outbox_and_forward_recovery_preserve_menu_prices()
    {
        var settings=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NEXACONNECT_QR_INTEGRATION_DB"));Assert.Equal("127.0.0.1",settings.Host);Assert.Matches("^nexa_qr_it_[a-f0-9]{32}$",settings.Database!);
        string schema="organization_"+Guid.NewGuid().ToString("N");await using var admin=new NpgsqlConnection(settings.ConnectionString);await admin.OpenAsync();
        try{
            await using(var create=new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"",admin))await create.ExecuteNonQueryAsync();settings.SearchPath=schema;await using var source=NpgsqlDataSource.Create(settings.ConnectionString);
            string scripts=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../src/Tools/NexaConnect.DataMigration/Scripts/Catalog"));
            foreach(var dir in Directory.GetDirectories(scripts).Order()){await using var up=source.CreateCommand(await File.ReadAllTextAsync(Path.Combine(dir,"up.sql")));await up.ExecuteNonQueryAsync();}
            foreach(var file in new[]{"down.sql","up.sql"}){await using var migrate=source.CreateCommand(await File.ReadAllTextAsync(Path.Combine(scripts,"0007_menu_organization",file)));await migrate.ExecuteNonQueryAsync();}
            var menu=new PostgresMenuCatalog(source);var repo=new PostgresMenuOrganizationRepository(source,TimeProvider.System);Guid org=Guid.NewGuid(),branch=Guid.NewGuid(),one=Guid.NewGuid(),two=Guid.NewGuid();var category=new Category(Guid.NewGuid(),"Food");
            menu.AddForOrganizationBranch(org,branch,new(one,"Meal",107,"THB","kitchen"));menu.AddForOrganizationBranch(org,branch,new(two,"Tea",5,"THB","bar"));
            async Task<bool> Save(){try{await repo.ChangeAsync(org,branch,(s,p)=>s.Change(0,[category],[new(two,null),new(one,category.Id)],p),new("manager",Guid.NewGuid()),default);return true;}catch(MenuOrganizationConflictException){return false;}}
            Assert.Single(await Task.WhenAll(Save(),Save()),x=>x);
            var first=await repo.ReadAsync(org,branch,default);Assert.Equal(1,first.Version);Assert.Equal(two,first.Items[0].ProductId);
            Assert.Equal(0,(await repo.ReadAsync(Guid.NewGuid(),branch,default)).Version);Assert.Equal(0,(await repo.ReadAsync(org,Guid.NewGuid(),default)).Version);
            await Assert.ThrowsAsync<ArgumentException>(()=>repo.ChangeAsync(org,branch,(s,p)=>s.Change(1,[category],[new(Guid.NewGuid(),category.Id)],p),new("manager",Guid.NewGuid()),default));
            await repo.ChangeAsync(org,branch,(s,p)=>s.Change(1,s.Categories,s.Items,p),new("manager",Guid.NewGuid()),default);
            await using(var fail=source.CreateCommand("ALTER TABLE outbox_messages RENAME TO outbox_unavailable"))await fail.ExecuteNonQueryAsync();
            try{await Assert.ThrowsAsync<PostgresException>(()=>repo.ChangeAsync(org,branch,(s,p)=>s.Change(1,[],[new(one,null),new(two,null)],p),new("manager",Guid.NewGuid()),default));}
            finally{await using var restore=source.CreateCommand("ALTER TABLE outbox_unavailable RENAME TO outbox_messages");await restore.ExecuteNonQueryAsync();}
            Assert.Equal(1,(await repo.ReadAsync(org,branch,default)).Version);
            await using(var count=source.CreateCommand("SELECT (SELECT count(*) FROM catalog_menu_organization_audit)=1 AND (SELECT count(*) FROM outbox_messages WHERE aggregate_type='catalog-menu-organization')=1 AND (SELECT count(*) FROM outbox_messages WHERE event_type='catalog.menu-item.changed.v1')=2"))Assert.True((bool)(await count.ExecuteScalarAsync())!);
            await using(var tamper=source.CreateCommand("UPDATE catalog_menu_organization_audit SET version=99"))await Assert.ThrowsAsync<PostgresException>(()=>tamper.ExecuteNonQueryAsync());
            await using(var down=source.CreateCommand(await File.ReadAllTextAsync(Path.Combine(scripts,"0007_menu_organization/down.sql"))))await Assert.ThrowsAsync<PostgresException>(()=>down.ExecuteNonQueryAsync());
            Assert.Equal(107,menu.GetForOrganizationBranch(org,branch).Single(x=>x.ProductId==one).UnitPrice);
            Assert.Equal(5,menu.GetForOrganizationBranch(org,branch).Single(x=>x.ProductId==two).UnitPrice);
        }finally{await using var drop=new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE",admin);await drop.ExecuteNonQueryAsync();}
    }
}
