extern alias ORDER;
using Npgsql;
using ORDER::NexaConnect.Services.Order.Application.Orders;
using ORDER::NexaConnect.Services.Order.Domain;
using ORDER::NexaConnect.Services.Order.Infrastructure.Persistence;

namespace NexaConnect.IntegrationTests;

public sealed class OrderListPersistenceTests
{
    [ConfiguredEnvironmentFact("NEXACONNECT_QR_INTEGRATION_DB")]
    public async Task Branch_history_has_stable_pages_exact_tenant_scope_and_parameterized_filters()
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NEXACONNECT_QR_INTEGRATION_DB"));
        Assert.Equal("127.0.0.1", settings.Host); Assert.Matches("^nexa_qr_it_[a-f0-9]{32}$", settings.Database!);
        string schema = "order_list_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(settings.ConnectionString); await admin.OpenAsync();
        try
        {
            await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin)) await create.ExecuteNonQueryAsync();
            settings.SearchPath = schema; await using var source = NpgsqlDataSource.Create(settings.ConnectionString);
            string scripts = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../../../src/Tools/NexaConnect.DataMigration/Scripts/Order"));
            foreach (string directory in Directory.GetDirectories(scripts).Order()) await Sql(source, await File.ReadAllTextAsync(System.IO.Path.Combine(directory, "up.sql")));
            var scope = new OrderListScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            var orders = new PostgresOrderRepository(source);
            var expected = new HashSet<Guid>();
            foreach (string channel in new[] { "pos", "qr", "pos", "qr", "web" })
            {
                var order = OrderAggregate.Create(Guid.NewGuid(), scope.OrganizationId, scope.BranchId,
                    [new OrderLine(Guid.NewGuid(), "Fixture", 80, 1, "kitchen")], "THB", scope.RestaurantId, channel);
                await orders.SaveAsync(order, default); expected.Add(order.Id);
            }
            foreach (var foreign in new[] { scope with { OrganizationId = Guid.NewGuid() }, scope with { RestaurantId = Guid.NewGuid() }, scope with { BranchId = Guid.NewGuid() } })
                await orders.SaveAsync(OrderAggregate.Create(Guid.NewGuid(), foreign.OrganizationId, foreign.BranchId,
                    [new OrderLine(Guid.NewGuid(), "Foreign", 80, 1, "kitchen")], "THB", foreign.RestaurantId), default);
            await Sql(source, "UPDATE orders SET created_at_utc=$1", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var repository = new PostgresOrderListRepository(source);
            var seen = new HashSet<Guid>(); OrderListCursor? after = null;
            while (true)
            {
                var page = await repository.ReadAsync(scope, "all", "all", after, 2, default);
                if (page.Count == 0) break;
                Assert.All(page, item => { Assert.Equal(scope, item.Scope); Assert.True(seen.Add(item.OrderId)); });
                after = new(page[^1].CreatedAtUtc, page[^1].OrderId);
            }
            Assert.True(expected.SetEquals(seen));
            Assert.Equal(2, (await repository.ReadAsync(scope, "qr", "all", null, 101, default)).Count);
            Assert.Equal(2, (await repository.ReadAsync(scope, "pos", "open", null, 101, default)).Count);
            Assert.Empty(await repository.ReadAsync(scope, "all", "paid", null, 101, default));
            Assert.Empty(await repository.ReadAsync(scope, "qr' OR 1=1--", "all", null, 101, default));
            Assert.Empty(await repository.ReadAsync(scope with { OrganizationId = Guid.NewGuid() }, "all", "all", null, 101, default));
        }
        finally
        {
            Assert.Matches("^order_list_[a-f0-9]{32}$", schema);
            await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", admin); await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task Sql(NpgsqlDataSource source, string text, params object[] values)
    {
        await using var command = source.CreateCommand(text);
        foreach (object value in values) command.Parameters.AddWithValue(value);
        await command.ExecuteNonQueryAsync();
    }
}
