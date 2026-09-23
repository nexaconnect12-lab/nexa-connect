extern alias POS;
extern alias REPORTING;
using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using Npgsql;
using Source = POS::NexaConnect.Services.POS.Infrastructure.Persistence;
using Sink = REPORTING::NexaConnect.Services.Reporting.Infrastructure.Persistence;
using Reports = REPORTING::NexaConnect.Services.Reporting.Application;

namespace NexaConnect.IntegrationTests;

public sealed class CashCloseProjectionPostgresTests : IAsyncLifetime
{
    private NpgsqlDataSource? source, sink;
    private readonly string sourceSchema = "cashclose_src_" + Guid.NewGuid().ToString("N"), sinkSchema = "cashclose_dst_" + Guid.NewGuid().ToString("N");
    private static string Script(string service, string directory, string direction) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripts", service, directory, direction + ".sql"));

    [CashCloseDatabaseFact]
    public async Task Close_review_late_settlement_outbox_rollback_projection_replay_and_rebuild()
    {
        Guid org = Guid.NewGuid(), restaurant = Guid.NewGuid(), branch = Guid.NewGuid(), store = Guid.NewGuid(), terminal = Guid.NewGuid();
        await Sql(source!, "INSERT INTO stores(id,restaurant_id,branch_id,code,name,operational_status,created_at_utc,created_by,updated_at_utc,updated_by) VALUES($1,$2,$3,'test','Test','active',now(),'test',now(),'test')", store, restaurant, branch);
        await Sql(source!, "INSERT INTO terminals(id,restaurant_id,store_id,code,device_type,registration_status,registered_at_utc,created_at_utc,updated_at_utc) VALUES($1,$2,$3,'test','pos','active',now(),now(),now())", terminal, restaurant, store);
        var shift = POS::NexaConnect.Services.POS.Domain.Shifts.Shift.Open(Guid.NewGuid(), store, terminal, "cashier", "TEST", Guid.NewGuid(), DateTimeOffset.UtcNow);
        await new Source.PostgresShiftStore(source!).CreateAsync(shift, default);
        var cash = new Source.PostgresCashSessionStore(source!);
        Guid session = await cash.OpenAsync(shift.Id, store, "THB", 100m, default);
        DateTimeOffset occurred = DateTimeOffset.UtcNow;
        await cash.CloseAsync(session, 95m, 1, "cashier", terminal, default);
        var publisher = new Source.PostgresCashClosePublicationStore(source!);
        var candidate = Assert.Single(await publisher.FindAsync(null, default));
        Assert.True(await publisher.PublishAsync(candidate, org, Guid.NewGuid(), default));
        Assert.False(await publisher.PublishAsync(candidate, org, Guid.NewGuid(), default));
        await Assert.ThrowsAsync<PostgresException>(() => Sql(source!, Script("POS", "0006_cash_close_publication", "down")));

        var reviews = new Source.PostgresCashReviewStore(source!);
        var scope = new POS::NexaConnect.Services.POS.Application.CashReviews.CashReviewScope(org, restaurant, branch, store);
        await reviews.ResolveAsync(scope, session, POS::NexaConnect.Services.POS.Domain.CashReviews.CashReviewDecision.Create("approve", "checked"),
            "supervisor", Guid.NewGuid(), 2, 0, Guid.NewGuid(), new string('a', 64), DateTimeOffset.UtcNow, default);
        Assert.True(await publisher.PublishAsync(candidate, org, Guid.NewGuid(), default));
        var late = new OrderManualTenderSettledV1(Guid.NewGuid(), Guid.NewGuid(), occurred, org, restaurant, branch, Guid.NewGuid(), Guid.NewGuid(), terminal, "cash", 10m, "THB");
        await new Source.PostgresOrderSettlementProjectionStore(source!).ProjectAsync(late, default);
        Assert.True(await publisher.PublishAsync(candidate, org, Guid.NewGuid(), default));
        var events = await Events(); Assert.Equal(3, events.Count); Assert.Equal("review_required", events[^1].ReviewStatus);
        Assert.Equal(-15m, events[^1].VarianceAmount);
        var repository = new Sink.PostgresCashCloseRepository(sink!);
        Assert.True(await repository.ProjectAsync(events[2].EventId, Reports.CashCloseReporting.Translate(events[2]), default));
        Assert.False(await repository.ProjectAsync(events[0].EventId, Reports.CashCloseReporting.Translate(events[0]), default));
        Assert.False(await repository.ProjectAsync(events[2].EventId, Reports.CashCloseReporting.Translate(events[2]), default));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.ProjectAsync(Guid.NewGuid(), Reports.CashCloseReporting.Translate(events[2] with { OrganizationId = Guid.NewGuid() }), default));
        var query = new Reports.CashCloseQuery(org, branch, store, occurred.AddDays(-1), occurred.AddDays(1), 10, null, null);
        Assert.Equal(3, Assert.Single(await repository.ReadAsync(query, default)).Snapshot.SnapshotVersion);
        Assert.Empty(await repository.ReadAsync(query with { OrganizationId = Guid.NewGuid() }, default));
        Assert.Empty(await repository.ReadAsync(query with { StoreId = Guid.NewGuid() }, default));

        await reviews.ResolveAsync(scope, session, POS::NexaConnect.Services.POS.Domain.CashReviews.CashReviewDecision.Create("investigate", "late movement"),
            "supervisor", Guid.NewGuid(), 3, 1, Guid.NewGuid(), new string('b', 64), DateTimeOffset.UtcNow, default);
        await Sql(source!, "ALTER TABLE outbox_messages RENAME TO unavailable_outbox");
        try { await Assert.ThrowsAsync<PostgresException>(() => publisher.PublishAsync(candidate, org, Guid.NewGuid(), default)); }
        finally { await Sql(source!, "ALTER TABLE unavailable_outbox RENAME TO outbox_messages"); }
        Assert.Equal(3, (await Events()).Count);
        Assert.True(await publisher.PublishAsync(candidate, org, Guid.NewGuid(), default));
        events = await Events(); Assert.Equal(4, events[^1].SnapshotVersion);
        await Sql(sink!, Script("Reporting", "0015_cash_close_projection", "down"));
        await Sql(sink!, Script("Reporting", "0015_cash_close_projection", "up"));
        foreach (var e in events.AsEnumerable().Reverse()) await repository.ProjectAsync(e.EventId, Reports.CashCloseReporting.Translate(e), default);
        Assert.Equal("investigating", Assert.Single(await repository.ReadAsync(query, default)).Snapshot.ReviewStatus);
    }

    private async Task<List<PosCashCloseSnapshotV1>> Events()
    {
        await using var c = await source!.OpenConnectionAsync();
        await using var q = new NpgsqlCommand("SELECT payload::text FROM outbox_messages WHERE event_type='pos.cash-close.snapshot.v1' ORDER BY (payload->>'SnapshotVersion')::bigint", c);
        await using var r = await q.ExecuteReaderAsync(); var result = new List<PosCashCloseSnapshotV1>();
        while (await r.ReadAsync()) result.Add(JsonSerializer.Deserialize<PosCashCloseSnapshotV1>(r.GetString(0))!);
        return result;
    }
    public async Task InitializeAsync()
    {
        if (!CashCloseDatabaseFactAttribute.Ready()) return;
        source = await Create("NEXACONNECT_POS_INTEGRATION_DB", sourceSchema);
        sink = await Create("NEXACONNECT_REPORTING_INTEGRATION_DB", sinkSchema);
        foreach (string dir in Directory.GetDirectories(Path.Combine(AppContext.BaseDirectory, "Scripts", "POS")).Order())
            await Sql(source, File.ReadAllText(Path.Combine(dir, "up.sql")));
        await Sql(source, Script("POS", "0006_cash_close_publication", "down"));
        await Sql(source, Script("POS", "0006_cash_close_publication", "up"));
        await Sql(sink, Script("Reporting", "0015_cash_close_projection", "up"));
    }
    private static async Task<NpgsqlDataSource> Create(string setting, string schema)
    {
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(setting)) { SearchPath = schema };
        var result = NpgsqlDataSource.Create(builder.ConnectionString);
        await Sql(result, $"CREATE SCHEMA \"{schema}\""); return result;
    }
    public async Task DisposeAsync()
    {
        if (source is not null) { await Sql(source, $"DROP SCHEMA IF EXISTS \"{sourceSchema}\" CASCADE"); await source.DisposeAsync(); }
        if (sink is not null) { await Sql(sink, $"DROP SCHEMA IF EXISTS \"{sinkSchema}\" CASCADE"); await sink.DisposeAsync(); }
    }
    private static async Task Sql(NpgsqlDataSource ds, string sql, params object[] values)
    {
        await using var q = ds.CreateCommand(sql); foreach (object value in values) q.Parameters.AddWithValue(value); await q.ExecuteNonQueryAsync();
    }
}
public sealed class CashCloseDatabaseFactAttribute : FactAttribute
{
    public static bool Ready() => Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT") is "Testing" or "Test" or "Development" &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NEXACONNECT_POS_INTEGRATION_DB")) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NEXACONNECT_REPORTING_INTEGRATION_DB"));
    public CashCloseDatabaseFactAttribute() { if (!Ready()) Skip = "Requires disposable POS/Reporting databases and safe environment."; }
}
