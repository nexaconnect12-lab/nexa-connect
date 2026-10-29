extern alias CATALOG;
using Npgsql;
using CATALOG::NexaConnect.Services.Catalog.Infrastructure;
using CATALOG::NexaConnect.Services.Catalog.Domain.Barcodes;
using CATALOG::NexaConnect.Services.Catalog.Application.Menu;

namespace NexaConnect.IntegrationTests;

public sealed class BarcodePersistenceTests
{
    [ConfiguredEnvironmentFact("NEXACONNECT_QR_INTEGRATION_DB")]
    public async Task Barcode_transactions_fence_tenants_versions_concurrent_claims_and_audit_rollback()
    {
        var settings=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NEXACONNECT_QR_INTEGRATION_DB"));
        Assert.Equal("127.0.0.1",settings.Host);Assert.Matches("^nexa_qr_it_[a-f0-9]{32}$",settings.Database!);
        string schema="barcode_"+Guid.NewGuid().ToString("N");await using var admin=new NpgsqlConnection(settings.ConnectionString);await admin.OpenAsync();
        try{
            await using(var create=new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"",admin))await create.ExecuteNonQueryAsync();
            settings.SearchPath=schema;await using var source=NpgsqlDataSource.Create(settings.ConnectionString);
            string scripts=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../src/Tools/NexaConnect.DataMigration/Scripts/Catalog"));
            foreach(var dir in Directory.GetDirectories(scripts).Order()){await using var migrate=source.CreateCommand(await File.ReadAllTextAsync(Path.Combine(dir,"up.sql")));await migrate.ExecuteNonQueryAsync();}
            // Empty 5→4→5 is supported; history later refuses downgrade.
            foreach(var file in new[]{"down.sql","up.sql"}){await using var migrate=source.CreateCommand(await File.ReadAllTextAsync(Path.Combine(scripts,"0005_product_barcodes",file)));await migrate.ExecuteNonQueryAsync();}
            var menu=new PostgresMenuCatalog(source);var repo=new PostgresBarcodeRepository(source,TimeProvider.System);
            Guid org=Guid.NewGuid(),branch=Guid.NewGuid(),otherBranch=Guid.NewGuid(),one=Guid.NewGuid(),two=Guid.NewGuid(),otherOrg=Guid.NewGuid();
            foreach(var p in new[]{one,two}){menu.AddForOrganizationBranch(org,branch,new(p,"Item",10,"THB","kitchen"));menu.AddForOrganizationBranch(org,otherBranch,new(p,"Item",10,"THB","kitchen"));}
            menu.AddForOrganizationBranch(otherOrg,branch,new(two,"Other tenant",11,"THB","bar"));
            async Task<bool> Claim(Guid product){try{await repo.ChangeAsync(org,branch,product,"001",s=>BarcodeAssignment.Change(s,product,"001",0),new("manager",Guid.NewGuid()),default);return true;}catch(BarcodeConflictException){return false;}}
            var results=await Task.WhenAll(Claim(one),Claim(two));Assert.Single(results,x=>x);
            var winner=(await repo.LookupAsync(org,branch,"001",default))!.ProductId;var loser=winner==one?two:one;
            await repo.ChangeAsync(org,otherBranch,winner,"001",s=>BarcodeAssignment.Change(s,winner,"001",0),new("manager",Guid.NewGuid()),default);
            await repo.ChangeAsync(otherOrg,branch,two,"001",s=>BarcodeAssignment.Change(s,two,"001",0),new("manager",Guid.NewGuid()),default);
            Assert.Equal("Other tenant",(await repo.LookupAsync(otherOrg,branch,"001",default))!.Name);
            Assert.Null(await repo.LookupAsync(org,Guid.NewGuid(),"001",default));
            await Assert.ThrowsAsync<BarcodeConflictException>(()=>repo.ChangeAsync(org,branch,winner,"002",s=>BarcodeAssignment.Change(s,winner,"002",0),new("manager",Guid.NewGuid()),default));
            await repo.ChangeAsync(org,branch,winner,null,s=>BarcodeAssignment.Change(s,winner,null,1),new("manager",Guid.NewGuid()),default);
            await Assert.ThrowsAsync<BarcodeConflictException>(()=>repo.ChangeAsync(org,branch,loser,"001",s=>BarcodeAssignment.Change(s,loser,"001",0),new("manager",Guid.NewGuid()),default));
            // Failed outbox append rolls back binding, registry and audit together.
            await using(var fail=source.CreateCommand("ALTER TABLE outbox_messages RENAME TO outbox_unavailable"))await fail.ExecuteNonQueryAsync();
            try{await Assert.ThrowsAsync<PostgresException>(()=>repo.ChangeAsync(org,branch,loser,"ROLLBACK",s=>BarcodeAssignment.Change(s,loser,"ROLLBACK",0),new("manager",Guid.NewGuid()),default));}
            finally{await using var restore=source.CreateCommand("ALTER TABLE outbox_unavailable RENAME TO outbox_messages");await restore.ExecuteNonQueryAsync();}
            Assert.Null(await repo.LookupAsync(org,branch,"ROLLBACK",default));Assert.Equal(0,(await repo.ReadAsync(org,branch,loser,default))!.Version);
            await using(var count=source.CreateCommand("SELECT (SELECT count(*) FROM catalog_barcode_audit)=(SELECT count(*) FROM outbox_messages WHERE aggregate_type='catalog-product-barcode')"))Assert.True((bool)(await count.ExecuteScalarAsync())!);
            await using(var tamper=source.CreateCommand("UPDATE catalog_barcode_audit SET version=999"))await Assert.ThrowsAsync<PostgresException>(()=>tamper.ExecuteNonQueryAsync());
            await using(var down=source.CreateCommand(await File.ReadAllTextAsync(Path.Combine(scripts,"0005_product_barcodes/down.sql"))))await Assert.ThrowsAsync<PostgresException>(()=>down.ExecuteNonQueryAsync());
        }finally{await using var drop=new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE",admin);await drop.ExecuteNonQueryAsync();}
    }
}
