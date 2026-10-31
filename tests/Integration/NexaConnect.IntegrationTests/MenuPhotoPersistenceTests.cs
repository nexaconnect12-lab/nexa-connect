extern alias CATALOG;
using Npgsql;
using CATALOG::NexaConnect.Services.Catalog.Infrastructure;
using CATALOG::NexaConnect.Services.Catalog.Domain.Photos;
namespace NexaConnect.IntegrationTests;
public sealed class MenuPhotoPersistenceTests
{
    [ConfiguredEnvironmentFact("NEXACONNECT_QR_INTEGRATION_DB")]
    public async Task First_assignment_race_scope_audit_atomicity_and_destructive_roundtrip()
    {
        var settings=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NEXACONNECT_QR_INTEGRATION_DB"));
        Assert.Equal("127.0.0.1",settings.Host);Assert.Matches("^nexa_qr_it_[a-f0-9]{32}$",settings.Database!);
        string schema="photo_"+Guid.NewGuid().ToString("N");await using var admin=new NpgsqlConnection(settings.ConnectionString);await admin.OpenAsync();
        try{
            await using(var create=new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"",admin))await create.ExecuteNonQueryAsync();
            settings.SearchPath=schema;await using var source=NpgsqlDataSource.Create(settings.ConnectionString);
            string scripts=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../src/Tools/NexaConnect.DataMigration/Scripts/Catalog"));
            foreach(var dir in Directory.GetDirectories(scripts).Order()){await using var up=source.CreateCommand(await File.ReadAllTextAsync(Path.Combine(dir,"up.sql")));await up.ExecuteNonQueryAsync();}
            foreach(var file in new[]{"down.sql","up.sql"}){await using var migrate=source.CreateCommand(await File.ReadAllTextAsync(Path.Combine(scripts,"0006_menu_photos",file)));await migrate.ExecuteNonQueryAsync();}
            var menu=new PostgresMenuCatalog(source);var repo=new PostgresMenuPhotoRepository(source,TimeProvider.System);
            Guid org=Guid.NewGuid(),branch=Guid.NewGuid(),product=Guid.NewGuid(),asset=Guid.NewGuid();menu.AddForOrganizationBranch(org,branch,new(product,"Meal",107,"THB","kitchen"));
            async Task<bool> Save(){try{await repo.ChangeAsync(org,branch,product,c=>MenuPhotoAssignment.Change(c.AssetId,c.Version,asset,0,true),new("manager",Guid.NewGuid()),default);return true;}catch(MenuPhotoConflictException){return false;}}
            Assert.Single(await Task.WhenAll(Save(),Save()),x=>x);
            Assert.Null(await repo.ReadAsync(Guid.NewGuid(),branch,product,default));Assert.Null(await repo.ReadAsync(org,Guid.NewGuid(),product,default));
            await using(var fail=source.CreateCommand("ALTER TABLE outbox_messages RENAME TO outbox_unavailable"))await fail.ExecuteNonQueryAsync();
            try{await Assert.ThrowsAsync<PostgresException>(()=>repo.ChangeAsync(org,branch,product,c=>MenuPhotoAssignment.Change(c.AssetId,c.Version,null,1,true),new("manager",Guid.NewGuid()),default));}
            finally{await using var restore=source.CreateCommand("ALTER TABLE outbox_unavailable RENAME TO outbox_messages");await restore.ExecuteNonQueryAsync();}
            Assert.Equal(asset,(await repo.ReadAsync(org,branch,product,default))!.AssetId);
            await using(var count=source.CreateCommand("SELECT (SELECT count(*) FROM catalog_menu_photo_audit)=1 AND (SELECT count(*) FROM outbox_messages WHERE aggregate_type='catalog-menu-photo')=1"))Assert.True((bool)(await count.ExecuteScalarAsync())!);
            await using(var tamper=source.CreateCommand("DELETE FROM catalog_menu_photo_audit"))await Assert.ThrowsAsync<PostgresException>(()=>tamper.ExecuteNonQueryAsync());
            Assert.Equal(107,menu.GetForOrganizationBranch(org,branch).Single().UnitPrice);
            await using(var down=source.CreateCommand(await File.ReadAllTextAsync(Path.Combine(scripts,"0006_menu_photos/down.sql"))))await Assert.ThrowsAsync<PostgresException>(()=>down.ExecuteNonQueryAsync());
            Assert.Equal(1,(await repo.ReadAsync(org,branch,product,default))!.Version);Assert.Equal(107,menu.GetForOrganizationBranch(org,branch).Single().UnitPrice);
        }finally{await using var drop=new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE",admin);await drop.ExecuteNonQueryAsync();}
    }
}
