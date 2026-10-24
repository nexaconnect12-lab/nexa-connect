extern alias POS;
extern alias REPORTING;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Infrastructure.Messaging;
using Npgsql;
using RabbitMQ.Client;
using SR=REPORTING::NexaConnect.Services.Reporting.Application;
using SF=REPORTING::NexaConnect.Services.Reporting.Domain;
using SDb=REPORTING::NexaConnect.Services.Reporting.Infrastructure.Persistence;
using PDb=POS::NexaConnect.Services.POS.Infrastructure.DayClose;
using SConsumer=REPORTING::NexaConnect.Services.Reporting.Infrastructure.Messaging.SettlementFinancialConsumer;
namespace NexaConnect.IntegrationTests;

public sealed partial class DaySealPostgresTests
{
    private SettlementReportRange SettlementRange=>new(organization,branch,Date,Date.AddDays(1));
    private Task<SettlementManifest> SettlementManifest()=>new PDb.PostgresSettlementInventoryStore(pos).ReadAsync(SettlementRange,restaurant,default);
    private async Task<SettlementManifest> CommittedSettlementManifest(){await FinalizeSettlement(await PrepareSettlement());return await SettlementManifest();}
    [ReportingDatabaseFact]
    public async Task Settlement_reporting_delayed_delivery_preserves_receipt_and_exact_manifest_then_converges_after_projection()
    {
        var manifest=await CommittedSettlementManifest();var original=JsonSerializer.Serialize((await Settlements.ReadAsync(Day,default))!.Receipt);
        var repository=new SDb.PostgresSettlementFactRepository(reporting);var missing=await repository.CompareAsync(manifest,default);
        Assert.Equal(1,missing.Missing);Assert.Equal("missing",Assert.Single(missing.Items).Status);
        var value=Assert.Single(manifest.Events);Assert.Null(value.V1);Assert.Equal(2,SR.SettlementReporting.Translate(value).ContractVersion);
        Assert.True(await repository.ProjectAsync(SR.SettlementReporting.Translate(value),default));
        var complete=await repository.CompareAsync(manifest,default);Assert.Equal(missing.ManifestHash,complete.ManifestHash);Assert.Equal("matched",complete.Status);
        Assert.Equal("verified",complete.Items.Single().CorrectionEvidence);Assert.Empty(complete.Items.Single().Corrections!.Items);
        Assert.Equal(original,JsonSerializer.Serialize((await Settlements.ReadAsync(Day,default))!.Receipt));
    }
    [ReportingDatabaseFact]
    public async Task Settlement_reporting_concurrent_delivery_conflicts_roll_back_receipt_and_checkpoint_without_overwrite()
    {
        var manifest=await CommittedSettlementManifest();var fact=SR.SettlementReporting.Translate(manifest.Events.Single());var repository=new SDb.PostgresSettlementFactRepository(reporting);
        var projected=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>repository.ProjectAsync(fact,default)));Assert.Single(projected,x=>x);
        Assert.False(await new SDb.PostgresSettlementFactRepository(reporting).ProjectAsync(fact,default));
        await Assert.ThrowsAsync<ArgumentException>(()=>repository.ProjectAsync(fact with{CashVariance=1},default));
        await Assert.ThrowsAsync<ArgumentException>(()=>repository.ProjectAsync(fact with{EventId=Guid.NewGuid()},default));
        Assert.Equal(1,await Count(reporting,"settlement_event_receipts"));Assert.Equal(1,await Count(reporting,"settlement_facts"));
        await using var q=reporting.CreateCommand("SELECT position FROM projection_checkpoints WHERE projector_name='pos-day-settlement'");Assert.Equal(1L,await q.ExecuteScalarAsync());
    }
    [ReportingDatabaseFact]
    public async Task Settlement_reporting_foreign_conflict_is_counted_without_disclosing_foreign_financial_values()
    {
        var manifest=await CommittedSettlementManifest();var fact=SR.SettlementReporting.Translate(manifest.Events.Single());var repository=new SDb.PostgresSettlementFactRepository(reporting);
        await repository.ProjectAsync(fact with{OrganizationId=Guid.NewGuid(),RestaurantId=Guid.NewGuid(),BranchId=Guid.NewGuid(),GrossSales=333,CompletedRefunds=0,NetSales=333},default);
        var report=await repository.CompareAsync(manifest,default);Assert.Equal(1,report.Conflicting);Assert.Equal(organization,report.OrganizationId);
        Assert.Equal(fact.GrossSales.ToString("0.####",System.Globalization.CultureInfo.InvariantCulture),report.Items.Single().GrossSales);
        Assert.NotEqual("333",report.Items.Single().GrossSales);
    }
    [ReportingDatabaseFact]
    public async Task Settlement_reporting_unexpected_projected_identity_is_visible_only_in_the_authorized_branch_range()
    {
        var manifest=await CommittedSettlementManifest();var fact=SR.SettlementReporting.Translate(manifest.Events.Single());var repository=new SDb.PostgresSettlementFactRepository(reporting);
        await repository.ProjectAsync(fact,default);await repository.ProjectAsync(fact with{EventId=Guid.NewGuid(),SettlementId=Guid.NewGuid(),BusinessDate=Date.AddDays(-1)},default);
        var wider=await new PDb.PostgresSettlementInventoryStore(pos).ReadAsync(SettlementRange with{FromDate=Date.AddDays(-1)},restaurant,default);
        Assert.Equal(1,(await repository.CompareAsync(wider,default)).Unexpected);Assert.Equal(0,(await repository.CompareAsync(manifest,default)).Unexpected);
        var foreign=await new PDb.PostgresSettlementInventoryStore(pos).ReadAsync(SettlementRange with{OrganizationId=Guid.NewGuid()},restaurant,default);Assert.Empty(foreign.Events);
    }
    [ReportingDatabaseFact]
    public async Task Settlement_reporting_legacy_original_is_not_enriched_and_missing_or_changed_original_fails_closed()
    {
        var current=await CommittedSettlementManifest();var v2=current.Events.Single().V2!;
        await Sql(pos,"UPDATE outbox_messages SET event_type='pos.branch-day-settled.v1',contract_version=1,payload=$2::jsonb WHERE id=$1",v2.EventId,JsonSerializer.Serialize(v2.Settlement,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var legacy=await SettlementManifest();Assert.Null(legacy.Events.Single().V2);var repository=new SDb.PostgresSettlementFactRepository(reporting);
        await repository.ProjectAsync(SR.SettlementReporting.Translate(legacy.Events.Single()),default);
        var report=await repository.CompareAsync(legacy,default);Assert.Equal("historical_unavailable",report.Items.Single().CorrectionEvidence);Assert.Null(report.Items.Single().Corrections);
        await Sql(pos,"UPDATE outbox_messages SET payload=jsonb_set(payload,'{netSales}','999'::jsonb) WHERE id=$1",v2.EventId);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>SettlementManifest());
        await Sql(pos,"DELETE FROM outbox_messages WHERE id=$1",v2.EventId);await Assert.ThrowsAsync<InvalidOperationException>(()=>SettlementManifest());
    }
    [ReportingDatabaseFact]
    public async Task Settlement_reporting_missing_committed_receipt_cannot_appear_as_empty_success()
    {
        await CommittedSettlementManifest();
        // Disposable owning schema only: simulate damaged history normally prevented by the immutable guard.
        await Sql(pos,"ALTER TABLE branch_day_settlement_receipts DISABLE TRIGGER settlement_receipts_immutable");
        try{await Sql(pos,"DELETE FROM branch_day_settlement_receipts");}
        finally{await Sql(pos,"ALTER TABLE branch_day_settlement_receipts ENABLE TRIGGER settlement_receipts_immutable");}
        await Assert.ThrowsAsync<InvalidOperationException>(()=>SettlementManifest());
    }
    [ReportingDatabaseFact]
    public async Task Settlement_reporting_migration_empty_rollback_and_immutable_history_guards_are_enforced()
    {
        var directory=Path.Combine(Root(),"src/Tools/NexaConnect.DataMigration/Scripts/Reporting/0022_settlement_reporting");
        await Sql(reporting,File.ReadAllText(Path.Combine(directory,"down.sql")));await Sql(reporting,File.ReadAllText(Path.Combine(directory,"up.sql")));
        var manifest=await CommittedSettlementManifest();await new SDb.PostgresSettlementFactRepository(reporting).ProjectAsync(SR.SettlementReporting.Translate(manifest.Events.Single()),default);
        await Assert.ThrowsAsync<PostgresException>(()=>Sql(reporting,"DELETE FROM settlement_facts"));
        await Assert.ThrowsAsync<PostgresException>(()=>Sql(reporting,"UPDATE settlement_event_receipts SET payload_hash=repeat('a',64)"));
        await Assert.ThrowsAsync<PostgresException>(()=>Sql(reporting,File.ReadAllText(Path.Combine(directory,"down.sql"))));
    }
    [ReportingRabbitFact]
    public async Task Settlement_reporting_hosted_consumer_recovers_commit_before_ack_and_restart_and_rejects_conflict()
    {
        var manifest=await CommittedSettlementManifest();var e=manifest.Events.Single().V2!;
        var wrapped=new SettlementCommitBeforeAck(new SDb.PostgresSettlementFactRepository(reporting));var services=new ServiceCollection();
        services.AddSingleton<SR.ISettlementFactRepository>(wrapped);services.AddSingleton<SR.ISettlementSource>(new SettlementSourcePort(this));services.AddSingleton<SR.IReportingCustomerAuthorizer>(this);services.AddScoped<SR.SettlementReporting>();
        using var provider=services.BuildServiceProvider();var queue="nexa.settlement.it."+Guid.NewGuid().ToString("N");var exchange=queue+".events";
        var uri=Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_INTEGRATION_URI")!;
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["SettlementConsumer:ConnectionString"]=uri,["SettlementConsumer:Queue"]=queue,["SettlementConsumer:Exchange"]=exchange,["SettlementConsumer:PrefetchCount"]="1"}).Build();
        await using var rabbit=await new ConnectionFactory{Uri=new(uri)}.CreateConnectionAsync();
        await using var transport=new RabbitMqOutboxTransport(rabbit,Options.Create(new OutboxOptions{Exchange=exchange}));
        await using var originalQuery=pos.CreateCommand("SELECT payload::text FROM outbox_messages WHERE id=$1");originalQuery.Parameters.AddWithValue(e.EventId);var raw=(string)(await originalQuery.ExecuteScalarAsync())!;
        async Task Publish(string payload,int version=2,Guid? id=null)=>await transport.PublishAsync(new(id??e.EventId,"pos.branch-day-settled.v"+version,version,"branch-day-settlement",e.Settlement.SettlementId,payload,e.CorrelationId.ToString("D"),e.OccurredAtUtc),default);
        async Task Wait(Func<Task<bool>> check){for(int n=0;n<300;n++){if(await check())return;await Task.Delay(50);}throw new TimeoutException("Settlement projection did not converge.");}
        try
        {
            using(var consumer=new SConsumer(provider.GetRequiredService<IServiceScopeFactory>(),config,NullLogger<SConsumer>.Instance))
            {
                await consumer.StartAsync(default);using var ready=new CancellationTokenSource(TimeSpan.FromSeconds(15));await consumer.WaitUntilReadyAsync(ready.Token);
                await Publish(raw);await Wait(()=>Task.FromResult(Volatile.Read(ref wrapped.Calls)>=2));
                await Publish(JsonSerializer.Serialize(e with{Settlement=e.Settlement with{CashVariance=1}},new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                await using var inspect=await rabbit.CreateChannelAsync();await Wait(async()=> (await inspect.QueueDeclarePassiveAsync(queue+".dead")).MessageCount==1);
                // A genuinely older envelope is accepted without invented correction proof.
                var legacy=e.Settlement with{EventId=Guid.NewGuid(),SettlementId=Guid.NewGuid(),BusinessDate=Date.AddDays(-1)};
                await Publish(JsonSerializer.Serialize(legacy,new JsonSerializerOptions(JsonSerializerDefaults.Web)),1,legacy.EventId);
                await Wait(async()=>await Count(reporting,"settlement_facts")==2);await consumer.StopAsync(default);
            }
            await Publish(raw);
            using(var consumer=new SConsumer(provider.GetRequiredService<IServiceScopeFactory>(),config,NullLogger<SConsumer>.Instance))
            {var before=wrapped.Calls;await consumer.StartAsync(default);using var ready=new CancellationTokenSource(TimeSpan.FromSeconds(15));await consumer.WaitUntilReadyAsync(ready.Token);await Wait(()=>Task.FromResult(Volatile.Read(ref wrapped.Calls)>before));await consumer.StopAsync(default);}
            Assert.Equal(2,await Count(reporting,"settlement_event_receipts"));Assert.Equal("matched",(await wrapped.CompareAsync(manifest,default)).Status);
        }
        finally{await using var cleanup=await rabbit.CreateChannelAsync();await cleanup.QueueDeleteAsync(queue);await cleanup.QueueDeleteAsync(queue+".dead");await cleanup.ExchangeDeleteAsync(exchange);}
    }
    private sealed class SettlementSourcePort(DaySealPostgresTests owner):SR.ISettlementSource
    {public Task<SettlementManifest> ReadAsync(SettlementReportRange r,string bearer,CancellationToken ct)=>new PDb.PostgresSettlementInventoryStore(owner.pos).ReadAsync(r,owner.restaurant,ct);}
    private sealed class SettlementCommitBeforeAck(SR.ISettlementFactRepository inner):SR.ISettlementFactRepository
    {
        public int Calls;
        public async Task<bool> ProjectAsync(SF.SettlementFact fact,CancellationToken ct){var changed=await inner.ProjectAsync(fact,ct);if(Interlocked.Increment(ref Calls)==1)throw new HttpRequestException("Test interruption after commit");return changed;}
        public Task<SettlementReport> CompareAsync(SettlementManifest m,CancellationToken ct)=>inner.CompareAsync(m,ct);
    }
}
