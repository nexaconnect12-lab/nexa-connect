extern alias REPORTING;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NexaConnect.Contracts.IntegrationEvents;
using Npgsql;
using RabbitMQ.Client;
using REPORTING::NexaConnect.Services.Reporting.Application;
using REPORTING::NexaConnect.Services.Reporting.Infrastructure.Persistence;
using REPORTING::NexaConnect.Services.Reporting.Infrastructure.Messaging;

namespace NexaConnect.IntegrationTests;

public sealed class SaleFinancialProjectionPostgresTests : IAsyncLifetime
{
    private NpgsqlDataSource? source;
    private readonly string schema = $"sale_financial_it_{Guid.NewGuid():N}";

    [ReportingDatabaseFact]
    public async Task Facts_are_atomic_concurrent_idempotent_conflict_safe_and_refund_order_independent()
    {
        await using var connection = await source!.OpenConnectionAsync();
        var service = new SaleFinancialReporting(new PostgresSaleFinancialFactRepository(source));
        var value = Event();
        var refund = new PaymentRefundedV1(Guid.NewGuid(), Guid.NewGuid(), value.PaidAtUtc.AddMinutes(1),
            value.OrganizationId, value.RestaurantId, value.BranchId, value.OrderId, value.PaymentId,
            Guid.NewGuid(), 25m, "THB", "customer_request", 25m, value.TotalAmount, "RF-TEST");
        await new RefundFinancialReporting(new PostgresRefundFinancialFactRepository(source)).ProjectAsync(refund, default);
        var results = await Task.WhenAll(service.ProjectAsync(value, default), service.ProjectAsync(value, default));
        Assert.Single(results, v => v);
        Assert.False(await service.ProjectAsync(value, default));
        Assert.False(await service.ProjectAsync(value with { SubtotalAmount = 100.0000m, TotalAmount = 117.7000m }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ProjectAsync(value with { SubtotalAmount = 101, TotalAmount = 118.7m }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ProjectAsync(value with { EventId = Guid.NewGuid() }, default));
        var collision = Event() with { PaymentId = value.PaymentId };
        await Assert.ThrowsAsync<ArgumentException>(() => service.ProjectAsync(collision, default));
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM sales_facts"));
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM sale_fact_event_receipts"));
        Assert.Equal(1L, await Count(connection, "SELECT position FROM projection_checkpoints WHERE projector_name='order-sale-financial'"));
        var report = await new ReportingQueries(new PostgresReportingReadRepository(source)).SalesAsync(value.OrganizationId,
            value.BranchId, value.OrderedAtUtc.AddDays(-1), value.PaidAtUtc.AddDays(1), default);
        Assert.Equal(117.7m, report.TotalSales); Assert.Equal(25m, report.RefundedAmount); Assert.Equal(92.7m, report.NetSales);
        var other = await new ReportingQueries(new PostgresReportingReadRepository(source)).SalesAsync(Guid.NewGuid(),
            value.BranchId, value.OrderedAtUtc.AddDays(-1), value.PaidAtUtc.AddDays(1), default);
        Assert.Equal(0, other.TotalSales);
        var check = new PostgresSaleReconciliation(source);
        Assert.Equal(1, (await check.CheckAsync([SaleFinancialReporting.Translate(value)], default)).Matched);
        await new NpgsqlCommand("UPDATE payment_facts SET paid_amount=1", connection).ExecuteNonQueryAsync();
        Assert.Equal(1, (await check.CheckAsync([SaleFinancialReporting.Translate(value)], default)).Conflicting);
        await Assert.ThrowsAsync<PostgresException>(async () => await new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(Scripts(),"0019_sale_payment_facts/down.sql")), connection).ExecuteNonQueryAsync());
    }

    [ReportingDatabaseFact]
    public async Task Failure_between_sale_and_payment_rolls_back_all_projection_state()
    {
        await using var connection = await source!.OpenConnectionAsync();
        await new NpgsqlCommand("CREATE FUNCTION fail_sale_test() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'test'; END $$; CREATE TRIGGER fail_sale_test BEFORE INSERT ON payment_facts FOR EACH ROW EXECUTE FUNCTION fail_sale_test()", connection).ExecuteNonQueryAsync();
        var value = Event();
        var projection = new SaleFinancialReporting(new PostgresSaleFinancialFactRepository(source));
        await Assert.ThrowsAsync<PostgresException>(() => projection.ProjectAsync(value, default));
        Assert.Equal(0L, await Count(connection, "SELECT count(*) FROM sales_facts"));
        Assert.Equal(0L, await Count(connection, "SELECT count(*) FROM sale_fact_event_receipts"));
        Assert.Equal(0L, await Count(connection, "SELECT count(*) FROM projection_checkpoints"));
        await new NpgsqlCommand("DROP TRIGGER fail_sale_test ON payment_facts", connection).ExecuteNonQueryAsync();
        Assert.True(await projection.ProjectAsync(value, default));
    }

    [ReportingRabbitFact]
    public async Task Durable_consumer_handles_duplicates_out_of_order_restart_and_identity_dead_letters()
    {
        string exchange = $"sale_it_{Guid.NewGuid():N}", queue = exchange + ".queue";
        var services = new ServiceCollection(); services.AddSingleton(source!);
        services.AddScoped<ISaleFinancialFactRepository, PostgresSaleFinancialFactRepository>(); services.AddScoped<SaleFinancialReporting>();
        using var provider = services.BuildServiceProvider();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["OrderSaleConsumer:ConnectionString"] = Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_INTEGRATION_URI"),
            ["OrderSaleConsumer:Exchange"] = exchange, ["OrderSaleConsumer:Queue"] = queue, ["OrderSaleConsumer:PrefetchCount"] = "1"
        }).Build();
        await using var rabbit = await new ConnectionFactory { Uri = new Uri(configuration["OrderSaleConsumer:ConnectionString"]!) }.CreateConnectionAsync();
        await using var channel = await rabbit.CreateChannelAsync();
        await using var database = await source!.OpenConnectionAsync();
        var first = Event(); var earlier = Event() with { OrderedAtUtc = first.OrderedAtUtc.AddDays(-1), PaidAtUtc = first.PaidAtUtc.AddDays(-1), OccurredAtUtc = first.PaidAtUtc.AddDays(-1) };
        try
        {
            async Task Run(Func<Task> publish, int count)
            {
                using var consumer = new OrderSaleFinancialConsumer(provider.GetRequiredService<IServiceScopeFactory>(),configuration,NullLogger<OrderSaleFinancialConsumer>.Instance);
                await consumer.StartAsync(default);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await consumer.WaitUntilReadyAsync(timeout.Token);
                await publish();
                while (await Count(database,"SELECT count(*) FROM sales_facts") != count) await Task.Delay(50,timeout.Token);
                await consumer.StopAsync(default);
            }
            async Task Publish(OrderSaleCompletedV1 value) => await channel.BasicPublishAsync(exchange,OrderSaleFinancialConsumer.RoutingKey,true,
                new BasicProperties { Persistent = true },JsonSerializer.SerializeToUtf8Bytes(value));
            await Run(async () => { await Publish(first); await Publish(first); await Publish(earlier); },2);
            await Publish(Event()); // queued while stopped; durable queue survives consumer restart
            await Run(() => Task.CompletedTask,3);
            using (var consumer = new OrderSaleFinancialConsumer(provider.GetRequiredService<IServiceScopeFactory>(),configuration,NullLogger<OrderSaleFinancialConsumer>.Instance))
            {
                await consumer.StartAsync(default);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await consumer.WaitUntilReadyAsync(timeout.Token);
                await Publish(first with { EventId = Guid.NewGuid() });
                while ((await channel.QueueDeclarePassiveAsync(queue + ".dead")).MessageCount != 1) await Task.Delay(50,timeout.Token);
                await consumer.StopAsync(default);
            }
            Assert.Equal(3L,await Count(database,"SELECT count(*) FROM sale_fact_event_receipts"));
        }
        finally { await channel.QueueDeleteAsync(queue); await channel.QueueDeleteAsync(queue + ".dead"); await channel.ExchangeDeleteAsync(exchange); }
    }

    public async Task InitializeAsync()
    {
        string? cs = Environment.GetEnvironmentVariable("NEXACONNECT_REPORTING_INTEGRATION_DB");
        string? environment = Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        if (cs is null || environment is not ("Testing" or "Test" or "Development")) return;
        source = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(cs) { SearchPath = schema }.ConnectionString);
        await using var connection = await source.OpenConnectionAsync();
        await new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"",connection).ExecuteNonQueryAsync();
        foreach (var migration in new[] { "0001_initial_schema", "0018_payment_refund_facts", "0019_sale_payment_facts" })
            await new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(Scripts(),migration,"up.sql")),connection).ExecuteNonQueryAsync();
    }
    public async Task DisposeAsync()
    {
        if (source is null) return;
        await using var connection = await source.OpenConnectionAsync();
        await new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE",connection).ExecuteNonQueryAsync();
        await source.DisposeAsync();
    }
    private static OrderSaleCompletedV1 Event()
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-2);
        return new(Guid.NewGuid(),Guid.NewGuid(),now,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),
            "payment_intent","card_omise_test","THB","pos","takeaway",now.AddMinutes(-1),now,"R-TEST",100,10,7.7m,117.7m);
    }
    private static async Task<long> Count(NpgsqlConnection connection,string sql) => (long)(await new NpgsqlCommand(sql,connection).ExecuteScalarAsync())!;
    private static string Scripts()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName,"NexaConnect.sln"))) root = root.Parent!;
        return Path.Combine(root.FullName,"src/Tools/NexaConnect.DataMigration/Scripts/Reporting");
    }
}
