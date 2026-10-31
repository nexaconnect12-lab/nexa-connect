extern alias CATALOG;
using Npgsql;
using CATALOG::NexaConnect.Services.Catalog.Domain.Details;
using CATALOG::NexaConnect.Services.Catalog.Infrastructure;
namespace NexaConnect.IntegrationTests;
public sealed class MenuDetailsPersistenceTests
{
    [ConfiguredEnvironmentFact("NEXACONNECT_QR_INTEGRATION_DB")]
    public async Task Details_CAS_audit_events_rollback_and_legacy_edits_preserve_independent_fields()
    {
        var settings=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NEXACONNECT_QR_INTEGRATION_DB"));Assert.Equal("127.0.0.1",settings.Host);Assert.Matches("^nexa_qr_it_[a-f0-9]{32}$",settings.Database!);
        string schema="details_"+Guid.NewGuid().ToString("N");await using var admin=new NpgsqlConnection(settings.ConnectionString);await admin.OpenAsync();
        try
        {
            await using(var create=new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"",admin))await create.ExecuteNonQueryAsync();settings.SearchPath=schema;await using var source=NpgsqlDataSource.Create(settings.ConnectionString);
            string scripts=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../src/Tools/NexaConnect.DataMigration/Scripts/Catalog"));
            foreach(var dir in Directory.GetDirectories(scripts).Order()){await using var up=source.CreateCommand(await File.ReadAllTextAsync(Path.Combine(dir,"up.sql")));await up.ExecuteNonQueryAsync();}
            foreach(string file in new[]{"down.sql","up.sql"}){await using var migrate=source.CreateCommand(await File.ReadAllTextAsync(Path.Combine(scripts,"0009_menu_details",file)));await migrate.ExecuteNonQueryAsync();}
            Guid org=Guid.NewGuid(),branch=Guid.NewGuid(),other=Guid.NewGuid(),product=Guid.NewGuid();var actor=new CATALOG::NexaConnect.Services.Catalog.Application.Menu.MenuMutationContext("manager",Guid.NewGuid());
            var menu=new PostgresMenuCatalog(source);var repo=new PostgresMenuDetailsRepository(source,TimeProvider.System);var availability=new PostgresMenuAvailabilityRepository(source,TimeProvider.System);
            menu.AddForOrganizationBranch(org,branch,new(product,"Meal",107,"THB","kitchen"));menu.AddForOrganizationBranch(org,other,new(product,"Other meal",10,"THB","bar"));
            var photos=new PostgresMenuPhotoRepository(source,TimeProvider.System);var asset=Guid.NewGuid();
            await photos.ChangeAsync(org,branch,product,c=>CATALOG::NexaConnect.Services.Catalog.Domain.Photos.MenuPhotoAssignment.Change(c.AssetId,c.Version,asset,c.Version,true),actor,default);
            var barcodes=new PostgresBarcodeRepository(source,TimeProvider.System);
            await barcodes.ChangeAsync(org,branch,product,"001234",s=>CATALOG::NexaConnect.Services.Catalog.Domain.Barcodes.BarcodeAssignment.Change(s,product,"001234",s.Version),actor,default);
            var layout=new PostgresMenuOrganizationRepository(source,TimeProvider.System);
            await layout.ChangeAsync(org,branch,(s,p)=>s.Change(0,[],[new(product,null)],p),actor,default);
            await availability.ChangeAsync(org,branch,product,new(false,0),actor,default);
            async Task<bool> Save(){try{await repo.ChangeAsync(org,branch,product,new("Renamed",108.25m,"bar",0),actor,default);return true;}catch(MenuDetailsConflictException){return false;}}
            Assert.Single(await Task.WhenAll(Save(),Save()),success=>success);
            var saved=(await repo.ReadAsync(org,branch,default)).Single();Assert.False(saved.Available);Assert.Equal(108.25m,saved.UnitPrice);Assert.Equal(1,saved.Version);
            Assert.Equal(1,(await availability.ReadAsync(org,branch,default)).Single().Version);Assert.Equal(asset,(await photos.ReadAsync(org,branch,product,default))!.AssetId);Assert.Equal(1,(await layout.ReadAsync(org,branch,default)).Version);
            Assert.Equal("001234",(await barcodes.ReadAsync(org,branch,product,default))!.Barcode);
            Assert.Equal(10,menu.GetForOrganizationBranch(org,other).Single().UnitPrice);Assert.Null(await repo.ChangeAsync(Guid.NewGuid(),branch,product,new("Foreign",1,"bar",0),actor,default));
            await repo.ChangeAsync(org,branch,product,new(saved.Name,saved.UnitPrice,saved.PreparationStation,1),actor,default);
            await using(var fail=source.CreateCommand("ALTER TABLE outbox_messages RENAME TO outbox_unavailable"))await fail.ExecuteNonQueryAsync();
            try{await Assert.ThrowsAsync<PostgresException>(()=>repo.ChangeAsync(org,branch,product,new("Lost",1,"kitchen",1),actor,default));}
            finally{await using var restore=source.CreateCommand("ALTER TABLE outbox_unavailable RENAME TO outbox_messages");await restore.ExecuteNonQueryAsync();}
            Assert.Equal(saved,(await repo.ReadAsync(org,branch,default)).Single());
            await using(var count=source.CreateCommand("SELECT (SELECT count(*) FROM catalog_menu_details_audit)=1 AND (SELECT count(*) FROM outbox_messages WHERE aggregate_type='catalog-menu-details' AND event_type='catalog.audit.v1')=1 AND (SELECT count(*) FROM outbox_messages WHERE aggregate_type='catalog-menu-item' AND event_type='catalog.menu-item.changed.v1' AND payload->>'Name'='Renamed')=1"))Assert.True((bool)(await count.ExecuteScalarAsync())!);
            menu.AddForOrganizationBranch(org,branch,new(product,"Legacy",109,"THB","kitchen"));
            Assert.Equal(2,(await repo.ReadAsync(org,branch,default)).Single().Version);Assert.False(menu.GetForOrganizationBranch(org,branch).Single().Available);
            await Assert.ThrowsAsync<MenuDetailsConflictException>(()=>repo.ChangeAsync(org,branch,product,new("Stale",1,"bar",1),actor,default));
            await using(var tamper=source.CreateCommand("DELETE FROM catalog_menu_details_audit"))await Assert.ThrowsAsync<PostgresException>(()=>tamper.ExecuteNonQueryAsync());
            await using(var down=source.CreateCommand(await File.ReadAllTextAsync(Path.Combine(scripts,"0009_menu_details/down.sql"))))await Assert.ThrowsAsync<PostgresException>(()=>down.ExecuteNonQueryAsync());
        }
        finally{await using var drop=new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE",admin);await drop.ExecuteNonQueryAsync();}
    }
}
