extern alias MEDIA;
using Npgsql;
using MEDIA::NexaConnect.Services.Media.Application;
using MEDIA::NexaConnect.Services.Media.Infrastructure.Persistence;
namespace NexaConnect.IntegrationTests;

public sealed class MediaLibraryPersistenceTests
{
    [ConfiguredEnvironmentFact("NEXACONNECT_QR_INTEGRATION_DB")]
    public async Task Photo_pages_search_readiness_and_purpose_are_tenant_scoped()
    {
        var settings=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NEXACONNECT_QR_INTEGRATION_DB"));
        Assert.Equal("127.0.0.1",settings.Host);Assert.Matches("^nexa_qr_it_[a-f0-9]{32}$",settings.Database!);
        string schema="library_"+Guid.NewGuid().ToString("N");await using var admin=new NpgsqlConnection(settings.ConnectionString);await admin.OpenAsync();
        try
        {
            await using(var create=new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"",admin))await create.ExecuteNonQueryAsync();
            settings.SearchPath=schema;await using var source=NpgsqlDataSource.Create(settings.ConnectionString);
            string scripts=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../src/Tools/NexaConnect.DataMigration/Scripts/Media"));
            foreach(var directory in Directory.GetDirectories(scripts).Order()){await using var migration=source.CreateCommand(await File.ReadAllTextAsync(Path.Combine(directory,"up.sql")));await migration.ExecuteNonQueryAsync();}
            Guid org=Guid.NewGuid(),foreign=Guid.NewGuid();var management=new PostgresMediaManagementRepository(source);var library=new PostgresMediaAssetRepository(source);
            async Task<MediaAssetSummary> Seed(Guid owner,string name,string service="catalog",string type="product",Guid? ownerId=null)
            {
                Guid id=Guid.NewGuid();return await management.StartAsync(owner,id,$"organizations/{owner:D}/assets/{id:D}/original",new(service,type,ownerId??Guid.NewGuid(),name,"image/png",10,new string('a',64)),"fixture",DateTimeOffset.UtcNow.AddMinutes(10),new(100000,100),default);
            }
            for(int i=0;i<30;i++)await Seed(org,$"Photo {i:00}.png");
            await Seed(foreign,"Foreign.png");await Seed(org,"Bank QR.png","restaurant","promptpay_qr");
            var deleted=await Seed(org,"Deleted.png");await management.DeleteAsync(org,deleted.Id,deleted.ConcurrencyVersion,"fixture",default);
            var first=await library.BrowseAsync(org,new(),default);var second=await library.BrowseAsync(org,new(Page:2),default);
            Assert.Equal(24,first.Items.Count);Assert.True(first.HasMore);Assert.Equal(6,second.Items.Count);Assert.False(second.HasMore);
            Assert.Equal(30,first.Items.Concat(second.Items).Select(x=>x.Id).Distinct().Count());
            Assert.All(first.Items.Concat(second.Items),x=>Assert.StartsWith("Photo ",x.OriginalFileName));
            Assert.Empty((await library.BrowseAsync(org,new(ReadyOnly:true),default)).Items);
            var literal=await Seed(org,"Photo %_ ' OR 1=1 --.png");
            var found=await library.BrowseAsync(org,new("%_ ' OR 1=1 --"),default);Assert.Equal(literal.Id,Assert.Single(found.Items).Id);
            Assert.Empty((await library.BrowseAsync(foreign,new("Photo"),default)).Items);
            await using(var ready=source.CreateCommand("UPDATE media_assets SET processing_status='ready',processed_at_utc=now() WHERE id=$1")){ready.Parameters.AddWithValue(literal.Id);await ready.ExecuteNonQueryAsync();}
            Assert.Equal(literal.Id,Assert.Single((await library.BrowseAsync(org,new(ReadyOnly:true),default)).Items).Id);
            var shared=await Seed(org,"Shared photo.png","media","photo_library",org);
            await Seed(org,"Shared wrong owner.png","media","photo_library",foreign);
            await Seed(foreign,"Shared foreign.png","media","photo_library",foreign);
            Assert.Equal(shared.Id,Assert.Single((await library.BrowseAsync(org,new("Shared"),default)).Items).Id);
        }
        finally
        {
            Assert.Matches("^library_[a-f0-9]{32}$",schema);await using var drop=new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE",admin);await drop.ExecuteNonQueryAsync();
        }
    }
}
