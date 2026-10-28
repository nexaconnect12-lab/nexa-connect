extern alias RESTAURANT;
using NexaConnect.Contracts.Media;
using Npgsql;
using Repo=RESTAURANT::NexaConnect.Services.Restaurant.Infrastructure.Persistence.PostgresBranchGalleryRepository;
using PricingRepo=RESTAURANT::NexaConnect.Services.Restaurant.Infrastructure.Persistence.PostgresBranchProductConfigurationRepository;
using PricingUpdate=RESTAURANT::NexaConnect.Services.Restaurant.Application.Configuration.UpdateBranchProductConfigurationCommand;

namespace NexaConnect.IntegrationTests;

public sealed class BranchGalleryPersistenceTests
{
    [ConfiguredEnvironmentFact("NEXACONNECT_GALLERY_INTEGRATION_DB")]
    public async Task Gallery_is_tenant_scoped_version_fenced_preserves_pricing_and_rolls_back_with_audit_outbox()
    {
        var connection=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NEXACONNECT_GALLERY_INTEGRATION_DB"));
        Assert.Equal("127.0.0.1",connection.Host);
        Assert.Matches("^nexa_gallery_it_[a-f0-9]{32}$",connection.Database!);
        string schema="gallery_"+Guid.NewGuid().ToString("N");
        await using var admin=new NpgsqlConnection(connection.ConnectionString);await admin.OpenAsync();
        try{
            await using(var create=new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"",admin))await create.ExecuteNonQueryAsync();
            connection.SearchPath=schema;await using var source=NpgsqlDataSource.Create(connection.ConnectionString);
            string scripts=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../src/Tools/NexaConnect.DataMigration/Scripts/Restaurant"));
            foreach(var directory in Directory.GetDirectories(scripts).Order()){
                await using var migration=source.CreateCommand(await File.ReadAllTextAsync(Path.Combine(directory,"up.sql")));await migration.ExecuteNonQueryAsync();
            }
            var org=Guid.NewGuid();var restaurant=Guid.NewGuid();var branch=Guid.NewGuid();
            await using(var seed=source.CreateCommand("INSERT INTO restaurants(id,organization_id,code,name,default_currency,default_time_zone,status,created_at_utc,updated_at_utc,created_by,updated_by) VALUES($1,$2,'fixture','Fixture','THB','Asia/Singapore','active',now(),now(),'fixture','fixture');")){
                seed.Parameters.AddWithValue(restaurant);seed.Parameters.AddWithValue(org);await seed.ExecuteNonQueryAsync();
            }
            await using(var seed=source.CreateCommand("INSERT INTO branches(id,restaurant_id,code,name,time_zone,currency,status,created_at_utc,updated_at_utc,created_by,updated_by) VALUES($1,$2,'fixture','Fixture','Asia/Singapore','THB','active',now(),now(),'fixture','fixture');")){
                seed.Parameters.AddWithValue(branch);seed.Parameters.AddWithValue(restaurant);await seed.ExecuteNonQueryAsync();
            }
            var repo=new Repo(source);var update=new UpdateBranchGallery(1,[new(Guid.NewGuid(),"Meal","")],[]);
            Assert.Equal(restaurant,await repo.RestaurantAsync(org,branch,default));
            Assert.Null(await repo.RestaurantAsync(Guid.NewGuid(),branch,default));
            Assert.Null(await repo.RestaurantAsync(org,Guid.NewGuid(),default));
            foreach(string table in new[]{"branches","restaurants"}){
                // Identifiers are the two fixed fixture-owned tables; runtime values remain parameters.
                await using var status=source.CreateCommand($"UPDATE {table} SET status=$1 WHERE id=$2;");
                status.Parameters.AddWithValue("suspended");status.Parameters.AddWithValue(table=="branches"?branch:restaurant);
                await status.ExecuteNonQueryAsync();Assert.Null(await repo.RestaurantAsync(org,branch,default));
                status.Parameters[0].Value="active";await status.ExecuteNonQueryAsync();
            }
            Assert.Null(await repo.GetAsync(Guid.NewGuid(),branch,default));
            Assert.Null(await repo.SaveAsync(Guid.NewGuid(),branch,update,"fixture",default));
            var first=await repo.SaveAsync(org,branch,update,"fixture",default);Assert.Equal(2,first!.Version);
            Assert.Null(await repo.SaveAsync(org,branch,update,"fixture",default));
            var races=await Task.WhenAll(repo.SaveAsync(org,branch,update with{ExpectedVersion=2},"fixture",default),repo.SaveAsync(org,branch,update with{ExpectedVersion=2,Staff=[]},"fixture",default));
            Assert.Single(races,r=>r is not null);
            var before=await repo.GetAsync(org,branch,default);
            var pricing=new PricingRepo(source);Assert.NotNull(await pricing.UpdateAsync(org,branch,new PricingUpdate(true,true,false,10,3,7,false),"fixture",default));
            var after=await repo.GetAsync(org,branch,default);Assert.Equal(4,after!.Version);Assert.Equal(before!.Staff,after.Staff);
            await using(var fail=source.CreateCommand("ALTER TABLE outbox_messages ADD CONSTRAINT gallery_test_reject CHECK(false) NOT VALID;"))await fail.ExecuteNonQueryAsync();
            await Assert.ThrowsAsync<PostgresException>(()=>repo.SaveAsync(org,branch,update with{ExpectedVersion=4},"fixture",default));
            Assert.Equal(4,(await repo.GetAsync(org,branch,default))!.Version);
            await using var count=source.CreateCommand("SELECT (SELECT count(*) FROM branch_management_audit),(SELECT count(*) FROM outbox_messages);");
            await using var reader=await count.ExecuteReaderAsync();await reader.ReadAsync();Assert.Equal(3,reader.GetInt64(0));Assert.Equal(3,reader.GetInt64(1));
        }finally{await using var drop=new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE",admin);await drop.ExecuteNonQueryAsync();}
    }
}
