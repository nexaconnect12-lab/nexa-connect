extern alias CATALOG;
using Npgsql;
using CATALOG::NexaConnect.Services.Catalog.Infrastructure;

namespace NexaConnect.IntegrationTests;

public sealed class QrMenuPersistenceTests
{
    [ConfiguredEnvironmentFact("NEXACONNECT_QR_INTEGRATION_DB")]
    public async Task Guest_menu_query_enforces_owner_branch_and_201_row_overflow_sentinel()
    {
        var settings=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NEXACONNECT_QR_INTEGRATION_DB"));
        Assert.Equal("127.0.0.1",settings.Host);Assert.Matches("^nexa_qr_it_[a-f0-9]{32}$",settings.Database!);
        string schema="qr_menu_"+Guid.NewGuid().ToString("N");await using var admin=new NpgsqlConnection(settings.ConnectionString);await admin.OpenAsync();
        try
        {
            await using(var create=new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"",admin))await create.ExecuteNonQueryAsync();
            settings.SearchPath=schema;await using var source=NpgsqlDataSource.Create(settings.ConnectionString);
            string scripts=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../src/Tools/NexaConnect.DataMigration/Scripts/Catalog"));
            foreach(var dir in Directory.GetDirectories(scripts).Order())
            {await using var migrate=source.CreateCommand(await File.ReadAllTextAsync(Path.Combine(dir,"up.sql")));await migrate.ExecuteNonQueryAsync();}
            var repo=new PostgresMenuCatalog(source);Guid org=Guid.NewGuid(),branch=Guid.NewGuid(),product=Guid.NewGuid();
            repo.AddForOrganizationBranch(org,branch,new(product,"Visible",107,"THB","kitchen"));
            repo.AddForOrganizationBranch(Guid.NewGuid(),branch,new(product,"Other organization",1,"THB","kitchen"));
            repo.AddForOrganizationBranch(org,Guid.NewGuid(),new(product,"Other branch",1,"THB","kitchen"));
            Assert.Equal("Visible",Assert.Single(repo.GetGuestMenu(org,branch)).Name);Assert.Empty(repo.GetGuestMenu(Guid.NewGuid(),branch));
            await using(var seed=source.CreateCommand("INSERT INTO catalog_menu_items(organization_id,branch_id,product_id,name,unit_price,currency,preparation_station,available) SELECT $1,$2,gen_random_uuid(),'Item '||n,1,'THB','kitchen',true FROM generate_series(1,300) n"))
            {seed.Parameters.AddWithValue(org);seed.Parameters.AddWithValue(branch);await seed.ExecuteNonQueryAsync();}
            Assert.Equal(201,repo.GetGuestMenu(org,branch).Count);
        }
        finally{await using var drop=new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE",admin);await drop.ExecuteNonQueryAsync();}
    }
}
