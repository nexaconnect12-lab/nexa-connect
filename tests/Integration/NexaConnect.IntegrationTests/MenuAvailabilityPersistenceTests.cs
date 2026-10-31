extern alias CATALOG;
using Npgsql;
using CATALOG::NexaConnect.Services.Catalog.Domain.Availability;
using CATALOG::NexaConnect.Services.Catalog.Infrastructure;
namespace NexaConnect.IntegrationTests;

public sealed class MenuAvailabilityPersistenceTests
{
    [ConfiguredEnvironmentFact("NEXACONNECT_QR_INTEGRATION_DB")] public async Task Owning_availability_CAS_audit_outbox_rollback_and_upsert_preserve_branch_menu()
    {
        var settings=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NEXACONNECT_QR_INTEGRATION_DB"));
        Assert.Equal("127.0.0.1",settings.Host);Assert.Matches("^nexa_qr_it_[a-f0-9]{32}$",settings.Database!);
        string schema="availability_"+Guid.NewGuid().ToString("N");
        await using var admin=new NpgsqlConnection(settings.ConnectionString);await admin.OpenAsync();
        try
        {
            await using(var create=new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"",admin))await create.ExecuteNonQueryAsync();
            settings.SearchPath=schema;await using var source=NpgsqlDataSource.Create(settings.ConnectionString);
            string scripts=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../src/Tools/NexaConnect.DataMigration/Scripts/Catalog"));
            foreach(var dir in Directory.GetDirectories(scripts).Order()){await using var up=source.CreateCommand(await File.ReadAllTextAsync(Path.Combine(dir,"up.sql")));await up.ExecuteNonQueryAsync();}
            foreach(string file in new[]{"down.sql","up.sql"}){await using var migration=source.CreateCommand(await File.ReadAllTextAsync(Path.Combine(scripts,"0008_menu_availability",file)));await migration.ExecuteNonQueryAsync();}
            Guid org=Guid.NewGuid(),branch=Guid.NewGuid(),other=Guid.NewGuid(),product=Guid.NewGuid();
            var menu=new PostgresMenuCatalog(source);var repo=new PostgresMenuAvailabilityRepository(source,TimeProvider.System);
            menu.AddForOrganizationBranch(org,branch,new(product,"Meal",107,"THB","kitchen"));
            menu.AddForOrganizationBranch(org,other,new(product,"Other branch meal",111,"THB","kitchen"));
            async Task<bool> Change(){try{await repo.ChangeAsync(org,branch,product,new(false,0),new("manager",Guid.NewGuid()),default);return true;}catch(MenuAvailabilityConflictException){return false;}}
            Assert.Single(await Task.WhenAll(Change(),Change()),success=>success);
            Assert.False(menu.GetGuestMenu(org,branch).Single().Available);Assert.True(menu.GetGuestMenu(org,other).Single().Available);
            Assert.Null(await repo.ChangeAsync(Guid.NewGuid(),branch,product,new(true,1),new("manager",Guid.NewGuid()),default));
            await repo.ChangeAsync(org,branch,product,new(false,1),new("manager",Guid.NewGuid()),default);
            var edited=menu.AddForOrganizationBranch(org,branch,new(product,"Renamed",108,"THB","bar"));Assert.False(edited.Available);Assert.Equal(108,edited.UnitPrice);
            await using(var fail=source.CreateCommand("ALTER TABLE outbox_messages RENAME TO outbox_unavailable"))await fail.ExecuteNonQueryAsync();
            try{await Assert.ThrowsAsync<PostgresException>(()=>repo.ChangeAsync(org,branch,product,new(true,1),new("manager",Guid.NewGuid()),default));}
            finally{await using var restore=source.CreateCommand("ALTER TABLE outbox_unavailable RENAME TO outbox_messages");await restore.ExecuteNonQueryAsync();}
            Assert.False((await repo.ReadAsync(org,branch,default)).Single().Available);
            Assert.Equal(1,(await repo.ReadAsync(org,branch,default)).Single().Version);
            await using(var count=source.CreateCommand("SELECT (SELECT count(*) FROM catalog_menu_availability_audit)=1 AND (SELECT count(*) FROM outbox_messages WHERE aggregate_type='catalog-menu-availability')=1"))Assert.True((bool)(await count.ExecuteScalarAsync())!);
            await using(var tamper=source.CreateCommand("DELETE FROM catalog_menu_availability_audit"))await Assert.ThrowsAsync<PostgresException>(()=>tamper.ExecuteNonQueryAsync());
            await using(var down=source.CreateCommand(await File.ReadAllTextAsync(Path.Combine(scripts,"0008_menu_availability/down.sql"))))await Assert.ThrowsAsync<PostgresException>(()=>down.ExecuteNonQueryAsync());
        }
        finally{await using var drop=new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE",admin);await drop.ExecuteNonQueryAsync();}
    }
}
