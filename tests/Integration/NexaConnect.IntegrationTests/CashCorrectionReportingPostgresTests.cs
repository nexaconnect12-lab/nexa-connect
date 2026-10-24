extern alias POS;
extern alias REPORTING;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Infrastructure.Messaging;
using Npgsql;
using RabbitMQ.Client;
using R = REPORTING::NexaConnect.Services.Reporting.Application;
using RDb = REPORTING::NexaConnect.Services.Reporting.Infrastructure.Persistence;
using PDb = POS::NexaConnect.Services.POS.Infrastructure.DayClose;
using Replay = POS::NexaConnect.Services.POS.Application.DayClose;
using Consumer = REPORTING::NexaConnect.Services.Reporting.Infrastructure.Messaging.CashCorrectionFinancialConsumer;

namespace NexaConnect.IntegrationTests;

public sealed partial class DaySealPostgresTests
{
    private async Task<CashCorrectionManifest> PostedCorrectionManifest()
    {
        var (_, work, _) = await CorrectionCase(); var app = CorrectionApplication(); var command = await CorrectionCommand(app, work);
        await app.PostAsync(organization, branch, Date, command, new("manager", "token"), Guid.NewGuid(), default);
        return await new PDb.PostgresCashCorrectionInventoryStore(pos).ReadAsync(organization, restaurant, branch,
            DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, default);
    }
    [ReportingDatabaseFact]
    public async Task Correction_reporting_detects_delayed_delivery_and_projects_original_once_under_concurrency()
    {
        var manifest = await PostedCorrectionManifest(); var repository = new RDb.PostgresCashCorrectionFactRepository(reporting);
        var gap = await repository.CompareAsync(manifest, default); Assert.Equal("gaps", gap.Status); Assert.Equal(1, gap.Missing);
        Assert.Equal(-10, gap.SourceAdjustment); Assert.Equal(0, gap.ProjectedAdjustment);
        var fact = R.CashCorrectionReporting.Translate(Assert.Single(manifest.Events));
        var applied = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => repository.ProjectAsync(fact, default)));
        Assert.Single(applied, x => x); Assert.Equal(1, await Count(reporting, "cash_correction_event_receipts"));
        var result = await new RDb.PostgresCashCorrectionFactRepository(reporting).CompareAsync(manifest, default);
        Assert.Equal("matched", result.Status); Assert.Equal(-10, result.ProjectedAdjustment); Assert.Equal("matched", Assert.Single(result.Items).Status);
        await Assert.ThrowsAsync<ArgumentException>(() => repository.ProjectAsync(fact with { Adjustment = -11 }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.ProjectAsync(fact with { EventId = Guid.NewGuid() }, default));
        Assert.Equal(1, await Count(reporting, "cash_correction_event_receipts")); Assert.Equal(1, await Count(pos, "late_cash_corrections"));
        Assert.Equal(0, await Count(pos, "cash_movements"));
    }
    [ReportingDatabaseFact]
    public async Task Correction_reporting_scope_conflicts_do_not_leak_other_tenant_financial_details()
    {
        var manifest = await PostedCorrectionManifest(); var repository = new RDb.PostgresCashCorrectionFactRepository(reporting);
        var expected = R.CashCorrectionReporting.Translate(manifest.Events.Single());
        var other = expected with { OrganizationId = Guid.NewGuid(), CorrectionId = Guid.NewGuid(), Adjustment = -999 };
        await repository.ProjectAsync(other, default);
        var compared = await repository.CompareAsync(manifest, default); Assert.Equal(1, compared.Conflicting);
        Assert.Equal(0, compared.ProjectedAdjustment); Assert.Equal(expected.CorrectionId, Assert.Single(compared.Items).CorrectionId);
        Assert.DoesNotContain(other.CorrectionId.ToString(), JsonSerializer.Serialize(compared));
        var noSource = manifest with { Events = Array.Empty<PosLateCashCorrectionPostedV1>() };
        Assert.Equal("matched", (await repository.CompareAsync(noSource, default)).Status);
        await repository.ProjectAsync(expected with { EventId = Guid.NewGuid() }, default);
        Assert.Equal(1, (await repository.CompareAsync(noSource, default)).Unexpected);
        var outside = manifest with { FromUtc = manifest.ToUtc, ToUtc = DateTimeOffset.UtcNow, ObservedAtUtc = DateTimeOffset.UtcNow, Events = [] };
        Assert.Empty((await repository.CompareAsync(outside, default)).Items);
    }
    [ReportingDatabaseFact]
    public async Task Correction_reporting_requires_live_source_permissions_and_consistent_retained_publication()
    {
        var manifest = await PostedCorrectionManifest();
        var source = new Replay.CashCorrectionInventory(new PDb.PostgresCashCorrectionInventoryStore(pos), this, new CorrectionPermissions(this));
        allowed = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadAsync(organization, branch, manifest.FromUtc, manifest.ToUtc, new("manager", "token"), default));
        allowed = true;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadAsync(Guid.NewGuid(), branch, manifest.FromUtc, manifest.ToUtc, new("manager", "token"), default));
        var report = new R.CashCorrectionReporting(new RDb.PostgresCashCorrectionFactRepository(reporting), new CorrectionSource(this), this);
        allowed = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => report.ReadAsync(new(organization, branch, manifest.FromUtc, manifest.ToUtc), "Bearer token", default)); allowed = true;
        await Sql(pos, "UPDATE outbox_messages SET payload='{}'::jsonb WHERE event_type='pos.late-cash-correction-posted.v1'");
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadAsync(organization, branch, manifest.FromUtc, manifest.ToUtc, new("manager", "token"), default));
    }
    [ReportingDatabaseFact]
    public async Task Correction_reporting_replay_is_manifest_pinned_audited_and_restart_safe_after_uncertain_delivery()
    {
        var manifest = await PostedCorrectionManifest(); var transport = new CorrectionTransport { FailAfterPublish = true };
        var replay = new Replay.CashCorrectionReplay(new POS::NexaConnect.Services.POS.Infrastructure.Persistence.PostgresCashCorrectionReplayStore(pos), transport);
        var request = new Replay.CashCorrectionReplayRequest(organization, branch, restaurant, manifest.FromUtc, manifest.ToUtc, 1);
        var plan = await replay.PreviewAsync(request, default); Assert.Equal(1, plan.Count); Assert.Empty(transport.Events);
        await Assert.ThrowsAsync<InvalidOperationException>(() => replay.ExecuteAsync(request, Guid.NewGuid(), "retry", new string('A', 64), default));
        Assert.Equal(0, await Count(pos, "cash_correction_replay_runs"));
        var interrupted = await Assert.ThrowsAsync<Replay.CashCorrectionReplayInterruptedException>(() => replay.ExecuteAsync(request, Guid.NewGuid(), "retry", plan.Manifest, default));
        Assert.NotEqual(Guid.Empty, interrupted.RunId); Assert.Equal(1, await Count(pos, "cash_correction_replay_attempts"));
        transport.FailAfterPublish = false; await replay.ExecuteAsync(request, Guid.NewGuid(), "retry", plan.Manifest, default);
        Assert.Equal(transport.Events[0], transport.Events[1]); Assert.Equal(manifest.Events.Single().EventId, transport.Events[0].Id);
        Assert.Equal(2, await Count(pos, "cash_correction_replay_runs")); Assert.Equal(3, await Count(pos, "cash_correction_replay_attempts"));
        foreach (var sql in new[] { "DELETE FROM cash_correction_replay_runs", "TRUNCATE cash_correction_replay_attempts", "UPDATE cash_correction_replay_runs SET reason='rebuild'" })
            await Assert.ThrowsAsync<PostgresException>(() => Sql(pos, sql));
        await Assert.ThrowsAsync<PostgresException>(() => Sql(pos, File.ReadAllText(Path.Combine(Root(), "src/Tools/NexaConnect.DataMigration/Scripts/POS/0021_cash_correction_replay/down.sql"))));
        Assert.Equal(1, await Count(pos, "late_cash_corrections"));
    }
    [ReportingDatabaseFact]
    public async Task Correction_reporting_receipts_are_immutable_and_controlled_rebuild_uses_original_event()
    {
        var manifest = await PostedCorrectionManifest(); var fact = R.CashCorrectionReporting.Translate(manifest.Events.Single());
        var repository = new RDb.PostgresCashCorrectionFactRepository(reporting); await repository.ProjectAsync(fact, default);
        foreach (var sql in new[] { "UPDATE cash_correction_facts SET adjustment=-11", "DELETE FROM cash_correction_event_receipts", "TRUNCATE cash_correction_facts" })
            await Assert.ThrowsAsync<PostgresException>(() => Sql(reporting, sql));
        var migration = Path.Combine(Root(), "src/Tools/NexaConnect.DataMigration/Scripts/Reporting/0021_cash_correction_facts");
        await Sql(reporting, File.ReadAllText(Path.Combine(migration, "down.sql"))); await Sql(reporting, File.ReadAllText(Path.Combine(migration, "up.sql")));
        Assert.Equal(1, (await repository.CompareAsync(manifest, default)).Missing);
        Assert.True(await repository.ProjectAsync(fact, default)); Assert.Equal("matched", (await repository.CompareAsync(manifest, default)).Status);
    }
    [ReportingRabbitFact]
    public async Task Hosted_correction_consumer_recovers_commit_before_ack_replay_conflict_and_restart()
    {
        var manifest = await PostedCorrectionManifest(); var receiptRepository = new CommitBeforeAck(new RDb.PostgresCashCorrectionFactRepository(reporting));
        var services = new ServiceCollection(); services.AddSingleton<R.ICashCorrectionFactRepository>(receiptRepository);
        services.AddSingleton<R.ICashCorrectionSource>(new CorrectionSource(this)); services.AddSingleton<R.IReportingCustomerAuthorizer>(this); services.AddScoped<R.CashCorrectionReporting>();
        using var provider = services.BuildServiceProvider(); string queue = "nexa.correction.it." + Guid.NewGuid().ToString("N"), exchange = queue + ".events";
        string uri = Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_INTEGRATION_URI")!;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["CashCorrectionConsumer:ConnectionString"] = uri,
            ["CashCorrectionConsumer:Exchange"] = exchange, ["CashCorrectionConsumer:Queue"] = queue, ["CashCorrectionConsumer:PrefetchCount"] = "1" }).Build();
        await using var rabbit = await new ConnectionFactory { Uri = new(uri) }.CreateConnectionAsync();
        await using var transport = new RabbitMqOutboxTransport(rabbit, Options.Create(new OutboxOptions { Exchange = exchange }));
        var publisher = new POS::NexaConnect.Services.POS.Infrastructure.Messaging.CashCorrectionReplayTransport(transport);
        var original = manifest.Events.Single(); var originalReplay = new Replay.CashCorrectionReplayEvent(original.EventId, JsonSerializer.Serialize(original));
        await using var originalPayload = pos.CreateCommand("SELECT payload::text FROM outbox_messages WHERE id=$1"); originalPayload.Parameters.AddWithValue(original.EventId);
        string retainedPayload = (string)(await originalPayload.ExecuteScalarAsync())!;
        Assert.Contains("\"eventId\"", retainedPayload);
        async Task Wait(Func<Task<bool>> predicate) { for (int i = 0; i < 200; i++) { if (await predicate()) return; await Task.Delay(50); } throw new TimeoutException("Correction consumer did not converge."); }
        try
        {
            using (var consumer = new Consumer(provider.GetRequiredService<IServiceScopeFactory>(), config, NullLogger<Consumer>.Instance))
            {
                await consumer.StartAsync(default); using var ready = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await consumer.WaitUntilReadyAsync(ready.Token);
                await transport.PublishAsync(new(original.EventId, Consumer.RoutingKey, 1, "late-cash-correction", original.CorrectionId,
                    retainedPayload, original.CorrelationId.ToString("D"), original.OccurredAtUtc), default);
                await Wait(() => Task.FromResult(Volatile.Read(ref receiptRepository.Calls) >= 2));
                Assert.Equal(1, await Count(reporting, "cash_correction_facts"));
                await publisher.PublishAsync(new(original.EventId, JsonSerializer.Serialize(original with { CashVarianceAdjustment = -11 })), default);
                await using var inspect = await rabbit.CreateChannelAsync(); await Wait(async () => (await inspect.QueueDeclarePassiveAsync(queue + ".dead")).MessageCount == 1);
                await consumer.StopAsync(default);
            }
            await publisher.PublishAsync(originalReplay, default);
            using (var consumer = new Consumer(provider.GetRequiredService<IServiceScopeFactory>(), config, NullLogger<Consumer>.Instance))
            {
                int before = receiptRepository.Calls; await consumer.StartAsync(default); using var ready = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await consumer.WaitUntilReadyAsync(ready.Token);
                await Wait(() => Task.FromResult(Volatile.Read(ref receiptRepository.Calls) > before)); await consumer.StopAsync(default);
            }
            Assert.Equal(1, await Count(reporting, "cash_correction_event_receipts")); Assert.Equal("matched", (await receiptRepository.CompareAsync(manifest, default)).Status);
        }
        finally { await using var cleanup = await rabbit.CreateChannelAsync(); await cleanup.QueueDeleteAsync(queue); await cleanup.QueueDeleteAsync(queue + ".dead"); await cleanup.ExchangeDeleteAsync(exchange); }
    }
    private sealed class CorrectionSource(DaySealPostgresTests owner) : R.ICashCorrectionSource
    {
        public Task<CashCorrectionManifest> ReadAsync(R.ReportingRange range, string authorization, CancellationToken ct) =>
            new PDb.PostgresCashCorrectionInventoryStore(owner.pos).ReadAsync(range.OrganizationId, owner.restaurant, range.BranchId, range.FromUtc, range.ToUtc, ct);
    }
    private sealed class CorrectionTransport : Replay.ICashCorrectionReplayTransport
    {
        public bool FailAfterPublish; public List<Replay.CashCorrectionReplayEvent> Events = [];
        public Task PublishAsync(Replay.CashCorrectionReplayEvent value, CancellationToken ct) { Events.Add(value); if (FailAfterPublish) throw new HttpRequestException(); return Task.CompletedTask; }
    }
    private sealed class CommitBeforeAck(R.ICashCorrectionFactRepository inner) : R.ICashCorrectionFactRepository
    {
        public int Calls;
        public async Task<bool> ProjectAsync(REPORTING::NexaConnect.Services.Reporting.Domain.CashCorrectionFact fact, CancellationToken ct)
        { bool changed = await inner.ProjectAsync(fact, ct); if (Interlocked.Increment(ref Calls) == 1) throw new HttpRequestException("Test interruption after commit"); return changed; }
        public Task<CashCorrectionReport> CompareAsync(CashCorrectionManifest source, CancellationToken ct) => inner.CompareAsync(source, ct);
    }
}
