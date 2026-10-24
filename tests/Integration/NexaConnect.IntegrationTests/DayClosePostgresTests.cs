extern alias POS;
extern alias AUTH;
using Npgsql;
using D=POS::NexaConnect.Services.POS.Domain.DayClose;
using A=POS::NexaConnect.Services.POS.Application.DayClose;
using Db=POS::NexaConnect.Services.POS.Infrastructure.DayClose;
namespace NexaConnect.IntegrationTests;

public sealed class DayClosePostgresTests:IAsyncLifetime
{
    private NpgsqlDataSource? source;private string? schema;
    private readonly D.DayIdentity day=new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new(2026,9,1));
    private readonly A.PreparationActor manager=new("manager",Guid.NewGuid());
    private static readonly DateTimeOffset Now=new(2026,9,3,0,0,0,TimeSpan.Zero);
    private D.PreparationCommand Command(long version=0)=>new(day.BranchId,day.BusinessDate,Guid.NewGuid(),version,"routine_close");
    private static D.DayEvidence Evidence()=>new("UTC","THB",Now.AddDays(-2),Now.AddDays(-1),100,25,75,0,[new("cash","THB",100)],new('a',64),new('b',64),new('c',64),0,0,0,0,0,0,["recorded_check_is_historical"],Now);
    [PosPostgresFact]public async Task Exact_replay_restart_conflicting_payload_and_immutable_audit_are_durable()
    {
        var store=new Db.PostgresDayCloseStore(source!);var command=Command();var started=await store.BeginAsync(day,command,manager,Now,default);
        var ready=await store.CompleteAsync(day,started.ClaimId!.Value,Evidence(),manager,Now,default);Assert.Equal("ready_for_review",ready.Status);
        var restarted=new Db.PostgresDayCloseStore(source!);var retry=await restarted.BeginAsync(day,command,manager,Now,default);Assert.Null(retry.ClaimId);Assert.Equal(ready.Version,retry.State.Version);
        await Assert.ThrowsAsync<D.DayCloseConflictException>(()=>restarted.BeginAsync(day,command with{ReasonCode="recheck"},manager,Now,default));
        await Assert.ThrowsAsync<D.DayCloseConflictException>(()=>restarted.BeginAsync(day,command,new("other-manager",Guid.NewGuid()),Now,default));
        Assert.Null(await restarted.ReadAsync(day with{OrganizationId=Guid.NewGuid()},default));Assert.Null(await restarted.ReadAsync(day with{RestaurantId=Guid.NewGuid()},default));Assert.Null(await restarted.ReadAsync(day with{BranchId=Guid.NewGuid()},default));
        Assert.Equal(2L,await Scalar("SELECT count(*) FROM branch_day_close_audit"));
        foreach(string sql in new[]{"UPDATE branch_day_close_audit SET subject_id='rewritten'","DELETE FROM branch_day_close_audit","TRUNCATE branch_day_close_audit"})
            await Assert.ThrowsAsync<PostgresException>(()=>Sql(sql));
        await Assert.ThrowsAsync<PostgresException>(()=>Sql(File.ReadAllText(Path.Combine(Root(),"src/Tools/NexaConnect.DataMigration/Scripts/POS/0008_day_close_preparations/down.sql"))));
        Assert.Equal(2L,await Scalar("SELECT count(*) FROM branch_day_close_audit"));
    }
    [PosPostgresFact]public async Task Two_managers_have_one_winner_and_expired_resume_fences_old_completion()
    {
        var store=new Db.PostgresDayCloseStore(source!);var first=Command();var second=Command();
        async Task<A.PreparationLease?> Try(D.PreparationCommand c,A.PreparationActor a){try{return await store.BeginAsync(day,c,a,Now,default);}catch(D.DayCloseConflictException){return null;}}
        var attempts=await Task.WhenAll(Try(first,manager),Try(second,new("manager-2",Guid.NewGuid())));Assert.Single(attempts,x=>x is not null);
        var winner=attempts.Single(x=>x is not null)!;var original=winner.State.PendingCommand!;var actor=winner.State.PreparingSubject==manager.Subject?manager:new A.PreparationActor("manager-2",Guid.NewGuid());
        await Assert.ThrowsAsync<D.DayCloseConflictException>(()=>store.BeginAsync(day,original,actor,Now.AddSeconds(29),default));
        var resumed=await new Db.PostgresDayCloseStore(source!).BeginAsync(day,original,actor,Now.AddSeconds(31),default);
        Assert.NotEqual(winner.ClaimId,resumed.ClaimId);
        await Assert.ThrowsAsync<D.DayCloseConflictException>(()=>store.CompleteAsync(day,winner.ClaimId!.Value,Evidence(),actor,Now.AddSeconds(32),default));
        var completed=await store.CompleteAsync(day,resumed.ClaimId!.Value,Evidence(),actor,Now.AddSeconds(32),default);Assert.Equal(3,completed.Version);
        Assert.Equal(3L,await Scalar("SELECT count(*) FROM branch_day_close_audit"));
    }
    [PosPostgresFact]public async Task New_operation_after_expiry_abandons_old_work_and_late_evidence_blocks_without_rewriting_snapshot()
    {
        var store=new Db.PostgresDayCloseStore(source!);var original=Command();var old=await store.BeginAsync(day,original,manager,Now,default);
        var replacement=await store.BeginAsync(day,Command(1),manager,Now.AddSeconds(31),default);
        await Assert.ThrowsAsync<D.DayCloseConflictException>(()=>store.BeginAsync(day,original,manager,Now.AddSeconds(32),default));
        await Assert.ThrowsAsync<D.DayCloseConflictException>(()=>store.CompleteAsync(day,old.ClaimId!.Value,Evidence(),manager,Now.AddSeconds(32),default));
        var ready=await store.CompleteAsync(day,replacement.ClaimId!.Value,Evidence(),manager,Now.AddSeconds(32),default);
        var blocked=await store.ValidateAsync(day,ready.Version,Evidence() with{PosVersion=new('d',64)},manager,Now.AddSeconds(33),default);
        Assert.Equal("blocked",blocked.Status);Assert.Contains("source_evidence_changed",blocked.Blockers);Assert.Equal(new('c',64),blocked.Snapshot!.PosVersion);
        await Assert.ThrowsAsync<D.DayCloseConflictException>(()=>store.ValidateAsync(day,ready.Version,Evidence(),manager,Now.AddSeconds(34),default));
        Assert.Equal(4L,await Scalar("SELECT count(*) FROM branch_day_close_audit"));
    }
    [PosPostgresFact]public async Task Empty_migration_can_downgrade_and_reapply()
    {
        await Sql(File.ReadAllText(Path.Combine(Root(),"src/Tools/NexaConnect.DataMigration/Scripts/POS/0008_day_close_preparations/down.sql")));
        await Sql(File.ReadAllText(Path.Combine(Root(),"src/Tools/NexaConnect.DataMigration/Scripts/POS/0008_day_close_preparations/up.sql")));
        Assert.Equal(0L,await Scalar("SELECT count(*) FROM branch_day_close_audit"));
    }
    [PosPostgresFact]public async Task Authorization_migration_backfills_roles_and_fresh_assignments_preserve_reader_preparer_separation()
    {
        string authSchema="day_close_auth_"+Guid.NewGuid().ToString("N");
        await using var auth=NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NEXACONNECT_POS_INTEGRATION_DB")!){SearchPath=authSchema+",public"}.ConnectionString);
        async Task Execute(string text){await using var command=auth.CreateCommand(text);await command.ExecuteNonQueryAsync();}
        await Execute("CREATE SCHEMA "+new NpgsqlCommandBuilder().QuoteIdentifier(authSchema));
        try
        {
            var folders=Directory.GetDirectories(Path.Combine(Root(),"src/Tools/NexaConnect.DataMigration/Scripts/Authorization")).Order().ToArray();
            foreach(var folder in folders.Take(9))await Execute(await File.ReadAllTextAsync(Path.Combine(folder,"up.sql")));
            var assignments=new AUTH::NexaConnect.Services.Authorization.Infrastructure.Persistence.PostgresAuthorizationAssignmentRepository(auth);
            foreach(var role in new[]{"store-manager","tenant-admin","accountant","cashier"})
                await assignments.AssignAsync(new(role,day.OrganizationId,day.RestaurantId,day.BranchId,role),"test-admin",default);
            // Simulate the persisted pre-10 assignment vocabulary without bypassing current assignment APIs.
            await Execute("DELETE FROM authorization_role_permissions WHERE permission_code LIKE 'pos.day-close.%'");
            await Execute("DELETE FROM authorization_user_permission_overrides WHERE permission_code LIKE 'pos.day-close.%'");
            await Execute(await File.ReadAllTextAsync(Path.Combine(folders[9],"up.sql")));
            var decisions=new AUTH::NexaConnect.Services.Authorization.Application.Decisions.AuthorizationDecisionService(new AUTH::NexaConnect.Services.Authorization.Infrastructure.Persistence.PostgresAuthorizationDecisionStore(auth));
            async Task<bool> Granted(string actor,string permission,Guid? organization=null)=>
                (await decisions.DecideAsync(actor,organization??day.OrganizationId,day.RestaurantId,day.BranchId,permission,null,null,default)).Granted;
            foreach(var managerRole in new[]{"store-manager","tenant-admin"}){Assert.True(await Granted(managerRole,"pos.day-close.read"));Assert.True(await Granted(managerRole,"pos.day-close.prepare"));}
            Assert.True(await Granted("accountant","pos.day-close.read"));Assert.False(await Granted("accountant","pos.day-close.prepare"));
            Assert.False(await Granted("cashier","pos.day-close.read"));Assert.False(await Granted("store-manager","pos.day-close.prepare",Guid.NewGuid()));
            await assignments.AssignAsync(new("fresh-manager",day.OrganizationId,day.RestaurantId,day.BranchId,"store-manager"),"test-admin",default);
            await assignments.AssignAsync(new("fresh-accountant",day.OrganizationId,day.RestaurantId,day.BranchId,"accountant"),"test-admin",default);
            Assert.True(await Granted("fresh-manager","pos.day-close.prepare"));Assert.True(await Granted("fresh-accountant","pos.day-close.read"));Assert.False(await Granted("fresh-accountant","pos.day-close.prepare"));
            await Execute("UPDATE authorization_user_permission_overrides SET effect='deny' WHERE subject_id='fresh-manager' AND permission_code='pos.day-close.prepare'");
            Assert.False(await Granted("fresh-manager","pos.day-close.prepare"));
            await Execute(await File.ReadAllTextAsync(Path.Combine(folders[9],"down.sql")));
            Assert.False(await Granted("store-manager","pos.day-close.prepare"));Assert.False(await Granted("fresh-accountant","pos.day-close.read"));
        }
        finally{await Execute("DROP SCHEMA "+new NpgsqlCommandBuilder().QuoteIdentifier(authSchema)+" CASCADE");}
    }

    public async Task InitializeAsync()
    {
        var connection=Environment.GetEnvironmentVariable("NEXACONNECT_POS_INTEGRATION_DB");if(string.IsNullOrWhiteSpace(connection))return;
        schema="day_close_"+Guid.NewGuid().ToString("N");source=NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(connection){SearchPath=schema+",public"}.ConnectionString);
        await Sql("CREATE SCHEMA "+new NpgsqlCommandBuilder().QuoteIdentifier(schema));
        foreach(var dir in Directory.GetDirectories(Path.Combine(Root(),"src/Tools/NexaConnect.DataMigration/Scripts/POS")).Order())await Sql(await File.ReadAllTextAsync(Path.Combine(dir,"up.sql")));
    }
    public async Task DisposeAsync(){if(source is null)return;try{await Sql("DROP SCHEMA "+new NpgsqlCommandBuilder().QuoteIdentifier(schema!)+" CASCADE");}finally{await source.DisposeAsync();}}
    private async Task Sql(string sql){await using var command=source!.CreateCommand(sql);await command.ExecuteNonQueryAsync();}
    private async Task<object?> Scalar(string sql){await using var command=source!.CreateCommand(sql);return await command.ExecuteScalarAsync();}
    private static string Root(){var dir=new DirectoryInfo(AppContext.BaseDirectory);while(dir is not null&&!File.Exists(Path.Combine(dir.FullName,"NexaConnect.sln")))dir=dir.Parent;return dir!.FullName;}
}
