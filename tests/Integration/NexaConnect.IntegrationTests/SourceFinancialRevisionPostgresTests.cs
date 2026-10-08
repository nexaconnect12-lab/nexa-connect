extern alias ORDER;
extern alias PAYMENT;
extern alias POS;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Infrastructure.Persistence;
using Npgsql;
using OrderDb=ORDER::NexaConnect.Services.Order.Infrastructure.Persistence;
using Orders=ORDER::NexaConnect.Services.Order.Domain;
using PaymentDb=PAYMENT::NexaConnect.Services.Payment.Infrastructure;
using Providers=PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers;
using Payments=PAYMENT::NexaConnect.Services.Payment.Application.Intents;
using PosDb=POS::NexaConnect.Services.POS.Infrastructure.Persistence;

namespace NexaConnect.IntegrationTests;

public sealed class SourceFinancialRevisionPostgresTests : IAsyncLifetime
{
    private readonly Dictionary<string,(NpgsqlDataSource Db,string Schema)> owned=[];
    private readonly Guid organization=Guid.NewGuid(),restaurant=Guid.NewGuid(),branch=Guid.NewGuid();
    private EndOfDayWindow Window=>new(organization,restaurant,branch,DateTimeOffset.UtcNow.AddDays(-2),DateTimeOffset.UtcNow.AddDays(-1));
    private NpgsqlDataSource Db(string owner)=>owned[owner].Db;
    private async Task<SourceFinancialRevision> Revision(string owner,EndOfDayWindow? window=null)
    {
        await using var c=await Db(owner).OpenConnectionAsync();
        await using var t=await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        return await PostgresFinancialRevision.ReadAsync(window??Window,c,t,default);
    }
    private async Task<Guid> Order()
    {
        var aggregate=Orders.OrderAggregate.Create(Guid.NewGuid(),organization,branch,
            [new Orders.OrderLine(Guid.NewGuid(),"Rice",100,1,"kitchen")],"THB",restaurantId:restaurant);
        await new OrderDb.PostgresOrderRepository(Db("Order")).SaveAsync(aggregate,default);
        return aggregate.Id;
    }
    private Guid Intent()
    {
        var db=new PaymentDb.PostgresPaymentIntents(Db("Payment"),Microsoft.Extensions.Options.Options.Create(new Providers.PaymentProviderOptions()));
        return db.Create(organization,new(restaurant,branch,Guid.NewGuid(),Guid.NewGuid().ToString(),100,"THB","card"),
            new Payments.PaymentMutationContext("acceptance",Guid.NewGuid())).Id;
    }
    private async Task<Guid> Store()
    {
        var id=Guid.NewGuid();
        await Sql("POS","INSERT INTO stores(id,restaurant_id,branch_id,code,name,operational_status,created_at_utc,created_by,updated_at_utc,updated_by) VALUES($1,$2,$3,'test','Test','active',now(),'test',now(),'test')",id,restaurant,branch);
        return id;
    }

    [ReportingDatabaseFact]
    public async Task Order_restored_values_still_supersede_manifest_and_rollback_never_advances_revision()
    {
        var id=await Order();var window=Window;
        await Sql("Order","UPDATE orders SET created_at_utc=$1 WHERE id=$2",window.FromUtc.AddHours(1),id);
        var store=new OrderDb.PostgresOrderCutoffStore(Db("Order"));
        var command=new SourceCutoffCommand(Guid.NewGuid(),window);
        var before=await store.CaptureAsync(command,"manager",default);
        await Sql("Order","UPDATE orders SET total_amount=total_amount+1,subtotal_amount=subtotal_amount+1 WHERE id=$1",id);
        await Sql("Order","UPDATE orders SET total_amount=total_amount-1,subtotal_amount=subtotal_amount-1 WHERE id=$1",id);
        var after=await store.CaptureAsync(command with{OperationId=Guid.NewGuid()},"manager",default);
        Assert.Equal(before.EvidenceVersion,after.EvidenceVersion);
        Assert.True(after.SourceRevision!.Revision>before.SourceRevision!.Revision);
        Assert.False((await store.ReadAsync(window,before.ManifestId,default))!.Current);
        Assert.Equal(before,(await store.CaptureAsync(command,"manager",default)) with{Summary=before.Summary,Sales=before.Sales,Refunds=before.Refunds,OwnerEvidence=before.OwnerEvidence});
        var revision=await Revision("Order");
        await using(var c=await Db("Order").OpenConnectionAsync())
        await using(var t=await c.BeginTransactionAsync())
        {
            await using var q=new NpgsqlCommand("UPDATE orders SET total_amount=total_amount+1,subtotal_amount=subtotal_amount+1 WHERE id=$1",c,t);
            q.Parameters.AddWithValue(id);await q.ExecuteNonQueryAsync();await t.RollbackAsync();
        }
        Assert.Equal(revision,await Revision("Order"));
        Assert.True((await store.ReadAsync(window,after.ManifestId,default))!.Current);
        Assert.Equal(0,(await Revision("Order",window with{BranchId=Guid.NewGuid()})).Revision);
        Assert.Null(await store.ReadAsync(window with{OrganizationId=Guid.NewGuid()},before.ManifestId,default));
    }

    [ReportingDatabaseFact]
    public async Task Concurrent_payment_mutations_advance_without_lost_updates_and_restored_totals_do_not_restore_readiness()
    {
        var first=Intent();var second=Intent();var window=Window;
        await Sql("Payment","UPDATE payment_intents SET created_at_utc=$1 WHERE id=$2 OR id=$3",window.FromUtc.AddHours(1),first,second);
        var store=new PaymentDb.PostgresPaymentCutoffStore(Db("Payment"));
        var before=await store.CaptureAsync(new(Guid.NewGuid(),window),"manager",default);
        var revision=await Revision("Payment");
        await Task.WhenAll(Sql("Payment","UPDATE payment_intents SET amount=amount+1 WHERE id=$1",first),
            Sql("Payment","UPDATE payment_intents SET amount=amount+1 WHERE id=$1",second));
        Assert.Equal(revision.Revision+2,(await Revision("Payment")).Revision);
        await Sql("Payment","UPDATE payment_intents SET amount=amount-1 WHERE id=$1 OR id=$2",first,second);
        var after=await store.CaptureAsync(new(Guid.NewGuid(),window),"manager",default);
        Assert.Equal(before.EvidenceVersion,after.EvidenceVersion);
        Assert.False((await store.ReadAsync(window,before.ManifestId,default))!.Current);
        Assert.True((await new PaymentDb.PostgresPaymentCutoffStore(Db("Payment")).ReadAsync(window,after.ManifestId,default))!.Current);
    }

    [ReportingDatabaseFact]
    public async Task Pos_branch_revision_detects_restored_changes_outside_the_selected_day_and_isolates_other_branches()
    {
        var id=await Store();var window=Window;
        var store=new PosDb.PostgresPosCutoffStore(Db("POS"));
        var before=await store.CaptureAsync(new(Guid.NewGuid(),window),"manager",default);
        await Sql("POS","UPDATE stores SET name='Changed' WHERE id=$1",id);
        await Sql("POS","UPDATE stores SET name='Test' WHERE id=$1",id);
        var after=await store.CaptureAsync(new(Guid.NewGuid(),window),"manager",default);
        Assert.Equal(before.EvidenceVersion,after.EvidenceVersion);
        Assert.False((await store.ReadAsync(window,before.ManifestId,default))!.Current);
        Assert.Equal(before.SourceRevision!.Revision+2,after.SourceRevision!.Revision);
        await Sql("POS","UPDATE stores SET branch_id=$1 WHERE id=$2",Guid.NewGuid(),id);
        Assert.False((await store.ReadAsync(window,after.ManifestId,default))!.Current);
        Assert.Equal(after.SourceRevision.Revision+1,(await Revision("POS",window)).Revision);
    }

    [ReportingDatabaseFact]
    public async Task Revision_and_source_rows_share_repeatable_read_but_later_validation_observes_concurrent_commit()
    {
        var id=await Order();var window=Window;
        await using var c=await Db("Order").OpenConnectionAsync();
        await using var t=await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var before=await PostgresFinancialRevision.ReadAsync(window,c,t,default);
        await Sql("Order","UPDATE orders SET total_amount=total_amount+1,subtotal_amount=subtotal_amount+1 WHERE id=$1",id);
        Assert.Equal(before,await PostgresFinancialRevision.ReadAsync(window,c,t,default));
        await t.CommitAsync();
        Assert.Equal(before.Revision+1,(await Revision("Order",window)).Revision);
    }

    [ReportingDatabaseFact]
    public async Task Revision_guards_and_migration_epochs_prevent_reset_or_reuse_and_history_blocks_downgrade()
    {
        foreach(var service in owned.Keys)
        {
            var before=await Revision(service);Assert.Equal(0,before.Revision);Assert.NotEqual(Guid.Empty,before.Epoch);
            foreach(var sql in new[]{"UPDATE source_financial_epoch SET epoch=gen_random_uuid()","DELETE FROM source_financial_epoch","TRUNCATE source_financial_epoch",
                "INSERT INTO source_financial_revisions VALUES($1,$2,1)","TRUNCATE source_financial_revisions"})
                await Assert.ThrowsAsync<PostgresException>(()=>Sql(service,sql,sql.Contains('$')?[restaurant,branch]:[]));
            var root=Path.Combine(Root(),"src/Tools/NexaConnect.DataMigration/Scripts",service);
            var directory=Directory.GetDirectories(root,"*_financial_revisions").Single();
            var seals=Directory.GetDirectories(root,"*_day_seals").Single();
            var attribution=Directory.GetDirectories(root,"*_day_change_attribution").Single();
            var exact=Directory.GetDirectories(root,"*_exact_day_attribution").Single();
            if(service=="POS")await Sql(service,File.ReadAllText(Path.Combine(Root(),"src/Tools/NexaConnect.DataMigration/Scripts/POS/0014_day_approvals/down.sql")));
            await Sql(service,await File.ReadAllTextAsync(Path.Combine(exact,"down.sql")));
            await Sql(service,await File.ReadAllTextAsync(Path.Combine(attribution,"down.sql")));
            await Sql(service,await File.ReadAllTextAsync(Path.Combine(seals,"down.sql")));
            await Sql(service,await File.ReadAllTextAsync(Path.Combine(directory,"down.sql")));
            await Sql(service,await File.ReadAllTextAsync(Path.Combine(directory,"up.sql")));
            await Sql(service,await File.ReadAllTextAsync(Path.Combine(seals,"up.sql")));
            await Sql(service,await File.ReadAllTextAsync(Path.Combine(attribution,"up.sql")));
            await Sql(service,await File.ReadAllTextAsync(Path.Combine(exact,"up.sql")));
            if(service=="POS")await Sql(service,File.ReadAllText(Path.Combine(Root(),"src/Tools/NexaConnect.DataMigration/Scripts/POS/0014_day_approvals/up.sql")));
            Assert.NotEqual(before.Epoch,(await Revision(service)).Epoch);
            var window=Window;
            if(service=="Order")await new OrderDb.PostgresOrderCutoffStore(Db(service)).CaptureAsync(new(Guid.NewGuid(),window),"manager",default);
            else if(service=="Payment")await new PaymentDb.PostgresPaymentCutoffStore(Db(service)).CaptureAsync(new(Guid.NewGuid(),window),"manager",default);
            else await new PosDb.PostgresPosCutoffStore(Db(service)).CaptureAsync(new(Guid.NewGuid(),window),"manager",default);
            await Assert.ThrowsAsync<PostgresException>(()=>Sql(service,File.ReadAllText(Path.Combine(directory,"down.sql"))));
        }
    }

    [ReportingDatabaseFact]
    public async Task Least_privilege_runtime_mutation_advances_revision_but_direct_reset_is_denied()
    {
        var role="revision_runtime_"+Guid.NewGuid().ToString("N");
        var quotedRole=new NpgsqlCommandBuilder().QuoteIdentifier(role);
        var quotedSchema=new NpgsqlCommandBuilder().QuoteIdentifier(owned["POS"].Schema);
        await using var c=await Db("POS").OpenConnectionAsync();
        await using var t=await c.BeginTransactionAsync();
        async Task Execute(string text,params object[] values)
        {await using var q=new NpgsqlCommand(text,c,t);foreach(var value in values)q.Parameters.AddWithValue(value);await q.ExecuteNonQueryAsync();}
        try
        {
            await Execute("CREATE ROLE "+quotedRole+" NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE");
            await Execute("GRANT USAGE ON SCHEMA "+quotedSchema+" TO "+quotedRole);
            await Execute("GRANT SELECT,INSERT,UPDATE,DELETE ON ALL TABLES IN SCHEMA "+quotedSchema+" TO "+quotedRole);
            await Execute("SET LOCAL ROLE "+quotedRole);
            await Execute("INSERT INTO stores(id,restaurant_id,branch_id,code,name,operational_status,created_at_utc,created_by,updated_at_utc,updated_by) VALUES($1,$2,$3,'runtime','Runtime','active',now(),'test',now(),'test')",Guid.NewGuid(),restaurant,branch);
            Assert.Equal(1,(await PostgresFinancialRevision.ReadAsync(Window,c,t,default)).Revision);
            await Assert.ThrowsAsync<PostgresException>(()=>Execute("UPDATE source_financial_revisions SET revision=revision+1"));
        }
        finally {await t.RollbackAsync();} // Role, grants and data are all run-owned transactional fixtures.
    }

    [ReportingDatabaseFact]
    public async Task Legacy_manifest_replay_preserves_history_but_cannot_claim_current_revision_evidence()
    {
        var window=Window;var db=Db("Order");var command=new SourceCutoffCommand(Guid.NewGuid(),window);
        var legacy=await new PostgresSnapshotRetention<OrderDaySummary>(db).CaptureAsync(command,"manager",async(c,t,ct)=>
        {
            var summary=await OrderDb.PostgresOrderDayReader.QueryAsync(window,c,t,ct);
            return new(Guid.Empty,Guid.Empty,0,window,DateTimeOffset.UtcNow,summary.EvidenceVersion!,summary,[],[]);
        },default);
        var store=new OrderDb.PostgresOrderCutoffStore(db);
        Assert.False((await store.ReadAsync(window,legacy.ManifestId,default))!.Current);
        Assert.Equal(legacy.ManifestId,(await store.CaptureAsync(command,"manager",default)).ManifestId);
        var fresh=await store.CaptureAsync(new(Guid.NewGuid(),window),"manager",default);
        Assert.Equal(2,fresh.EvidenceProtocolVersion);Assert.True((await store.ReadAsync(window,fresh.ManifestId,default))!.Current);
    }

    public async Task InitializeAsync()
    {
        var connection=Environment.GetEnvironmentVariable("NEXACONNECT_REPORTING_INTEGRATION_DB");
        if(string.IsNullOrWhiteSpace(connection))return;
        foreach(var service in new[]{"Order","Payment","POS"})
        {
            var schema="revision_"+Guid.NewGuid().ToString("N");
            var db=NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(connection){SearchPath=schema+",public"}.ConnectionString);
            owned.Add(service,(db,schema));await Sql(service,"CREATE SCHEMA "+new NpgsqlCommandBuilder().QuoteIdentifier(schema));
            foreach(var directory in Directory.GetDirectories(Path.Combine(Root(),"src/Tools/NexaConnect.DataMigration/Scripts",service)).Order())
                await Sql(service,await File.ReadAllTextAsync(Path.Combine(directory,"up.sql")));
        }
    }
    public async Task DisposeAsync()
    {
        foreach(var(service,(db,schema))in owned)
            try{await Sql(service,"DROP SCHEMA "+new NpgsqlCommandBuilder().QuoteIdentifier(schema)+" CASCADE");}finally{await db.DisposeAsync();}
    }
    private async Task Sql(string owner,string sql,params object[] values)
    {await using var q=Db(owner).CreateCommand(sql);foreach(var value in values)q.Parameters.AddWithValue(value);await q.ExecuteNonQueryAsync();}
    private static string Root(){var directory=new DirectoryInfo(AppContext.BaseDirectory);while(!File.Exists(Path.Combine(directory.FullName,"NexaConnect.sln")))directory=directory.Parent!;return directory.FullName;}
}
