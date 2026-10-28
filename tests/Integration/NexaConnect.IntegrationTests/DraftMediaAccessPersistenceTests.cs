extern alias AUTH;
using Npgsql;
using AUTH::NexaConnect.Services.Authorization.Infrastructure.Persistence;
using AUTH::NexaConnect.Services.Authorization.Application.Assignments;
using AUTH::NexaConnect.Services.Authorization.Application.Decisions;

namespace NexaConnect.IntegrationTests;

public sealed class DraftMediaAccessPersistenceTests
{
    [ConfiguredEnvironmentFact("NEXACONNECT_QR_INTEGRATION_DB")]
    public async Task Local_media_grants_are_narrow_idempotent_and_preserve_denials_and_revocations()
    {
        var settings=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NEXACONNECT_QR_INTEGRATION_DB"));
        Assert.Equal("127.0.0.1",settings.Host);Assert.Matches("^nexa_qr_it_[a-f0-9]{32}$",settings.Database!);
        string database="nexa_review_it_"+Guid.NewGuid().ToString("N")+"_authorization";
        await using var admin=new NpgsqlConnection(settings.ConnectionString);await admin.OpenAsync();
        await using(var create=new NpgsqlCommand($"CREATE DATABASE \"{database}\"",admin))await create.ExecuteNonQueryAsync();
        try
        {
            settings.Database=database;await using var source=NpgsqlDataSource.Create(settings.ConnectionString);
            string scripts=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../src/Tools/NexaConnect.DataMigration/Scripts/Authorization"));
            foreach(string directory in Directory.GetDirectories(scripts).Order())await Sql(source,await File.ReadAllTextAsync(Path.Combine(directory,"up.sql")));
            var organization=Guid.NewGuid();var manager=Guid.NewGuid().ToString();var other=Guid.NewGuid().ToString();
            await new AuthorizationAssignmentService(new PostgresAuthorizationAssignmentRepository(source)).AssignAsync(new(manager,organization,Guid.NewGuid(),null,"store-manager"),"fixture",default);
            var provisioner=new DraftMediaAccessProvisioner(source);
            var decisions=new AuthorizationDecisionService(new PostgresAuthorizationDecisionStore(source));
            async Task<bool> Granted(string actor,string permission,Guid? org=null)=>(await decisions.DecideAsync(actor,org??organization,null,null,permission,null,null,default)).Granted;
            Assert.False(await Granted(manager,"media.asset.manage"));
            await provisioner.EnsureAsync(organization,manager,default);await provisioner.EnsureAsync(organization,manager,default);
            Assert.True(await Granted(manager,"media.asset.read"));Assert.True(await Granted(manager,"media.asset.manage"));
            Assert.False(await Granted(manager,"order.manual-payment.confirm"));Assert.False(await Granted(other,"media.asset.manage"));
            Assert.False(await Granted(manager,"media.asset.manage",Guid.NewGuid()));
            await using(var count=source.CreateCommand("SELECT count(*) FROM authorization_user_permission_overrides o JOIN authorization_resource_scopes s ON s.id=o.scope_id WHERE s.restaurant_id IS NULL"))
                Assert.Equal(2L,await count.ExecuteScalarAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(()=>provisioner.EnsureAsync(organization,other,default));
            await Sql(source,"UPDATE authorization_user_permission_overrides SET effect='deny' WHERE permission_code='media.asset.manage'");
            await Sql(source,"UPDATE authorization_user_permission_overrides SET status='revoked' WHERE permission_code='media.asset.read'");
            await provisioner.EnsureAsync(organization,manager,default);
            Assert.False(await Granted(manager,"media.asset.manage"));Assert.False(await Granted(manager,"media.asset.read"));
        }
        finally
        {
            Assert.Matches("^nexa_review_it_[a-f0-9]{32}_authorization$",database);
            await using var drop=new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)",admin);await drop.ExecuteNonQueryAsync();
        }
    }
    private static async Task Sql(NpgsqlDataSource source,string sql){await using var command=source.CreateCommand(sql);await command.ExecuteNonQueryAsync();}
}
