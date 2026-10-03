extern alias ORDER;
using Npgsql;
using ORDER::NexaConnect.Services.Order.Application.Cancellations;
using ORDER::NexaConnect.Services.Order.Domain;
using ORDER::NexaConnect.Services.Order.Infrastructure.Persistence;

namespace NexaConnect.IntegrationTests;

public sealed class OrderCancellationPostgresTests
{
    [PricingDatabaseFact]
    public async Task Cancellation_is_scoped_idempotent_fenced_audited_and_immutable()
    {
        string schema = $"cancel_it_{Guid.NewGuid():N}";
        var builder = new NpgsqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("NEXACONNECT_ORDER_INTEGRATION_DB")!) { SearchPath = schema };
        await using var source = NpgsqlDataSource.Create(builder.ConnectionString);
        await using var connection = await source.OpenConnectionAsync();
        await new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection).ExecuteNonQueryAsync();
        try
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(root.FullName, "NexaConnect.sln"))) root = root.Parent!;
            string scripts = Path.Combine(root.FullName, "src/Tools/NexaConnect.DataMigration/Scripts/Order");
            foreach (string directory in Directory.GetDirectories(scripts).Order())
                await new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(directory, "up.sql")), connection)
                    .ExecuteNonQueryAsync();

            var orders = new PostgresOrderRepository(source);
            var cancellations = new PostgresOrderCancellationRepository(source);
            var lines = new OrderLine[] { new(Guid.NewGuid(), "Rice", 100, 1, "kitchen") };
            var order = OrderAggregate.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), lines, "THB",
                idempotencyKey: Guid.NewGuid().ToString("N"));
            order.Submit(); order.MarkInventoryReserved(); order.MarkKitchenAccepted();
            await orders.SaveAsync(order, default);

            Guid operation = Guid.NewGuid();
            var command = new CancelOrderCommand(order.Id, order.OrganizationId, order.BranchId, operation,
                "Customer changed order", "cashier-1", Guid.NewGuid(), Guid.NewGuid());
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var first = await cancellations.BeginAsync(command, now, default);
            Assert.NotNull(first); Assert.False(first.Value.Replayed);
            var replay = await cancellations.BeginAsync(command, now, default);
            Assert.True(replay!.Value.Replayed);
            await Assert.ThrowsAsync<OrderCancellationConflictException>(() => cancellations.BeginAsync(
                command with { Reason = "Different reason" }, now, default));
            Assert.Null(await cancellations.BeginAsync(command with { OrganizationId = Guid.NewGuid() }, now, default));

            ClaimedOrderCancellation claim = (await cancellations.ClaimAsync(order.Id, now,
                TimeSpan.FromMinutes(1), default))!;
            Assert.False(await cancellations.CompleteAsync(claim with { ClaimId = Guid.NewGuid() }, now, default));
            Assert.True(await cancellations.CompleteAsync(claim, now.AddSeconds(1), default));
            Assert.Equal(OrderStatus.Cancelled, (await orders.GetAsync(order.Id, default))!.Status);
            Assert.Equal("completed", (await cancellations.GetAsync(order.Id, default))!.Status);
            Assert.Equal(4L, await new NpgsqlCommand("SELECT count(*) FROM outbox_messages", connection).ExecuteScalarAsync());
            Assert.Equal(2L, await new NpgsqlCommand("SELECT count(*) FROM outbox_messages WHERE event_type='order.audit.v1'", connection).ExecuteScalarAsync());
            await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand(
                "UPDATE order_cancellations SET reason='changed'", connection).ExecuteNonQueryAsync());
            await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand(
                "DELETE FROM order_cancellations", connection).ExecuteNonQueryAsync());
            await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand(
                File.ReadAllText(Path.Combine(scripts, "0010_pre_payment_cancellation/down.sql")), connection)
                .ExecuteNonQueryAsync());
        }
        finally
        {
            await new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", connection).ExecuteNonQueryAsync();
        }
    }
}
