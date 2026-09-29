extern alias RESTAURANT;
extern alias ORDER;
using Npgsql;
using NexaConnect.Contracts.IntegrationEvents;
using ORDER::NexaConnect.Services.Order.Domain;
using ORDER::NexaConnect.Services.Order.Application.Orders;
using ORDER::NexaConnect.Services.Order.Application.ManualTenders;
using ORDER::NexaConnect.Services.Order.Infrastructure.Persistence;

namespace NexaConnect.IntegrationTests;

public sealed class OrderPricingPostgresTests
{
    [PricingDatabaseFact]
    public async Task Restaurant_configuration_is_scoped_versioned_audited_and_preserves_omitted_tax()
    {
        string schema = $"pricing_config_it_{Guid.NewGuid():N}";
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NEXACONNECT_ORDER_INTEGRATION_DB")!) { SearchPath = schema };
        await using var source = NpgsqlDataSource.Create(builder.ConnectionString);
        await using var connection = await source.OpenConnectionAsync();
        await new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection).ExecuteNonQueryAsync();
        try
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(root.FullName, "NexaConnect.sln"))) root = root.Parent!;
            foreach (string directory in Directory.GetDirectories(Path.Combine(root.FullName, "src/Tools/NexaConnect.DataMigration/Scripts/Restaurant")).Order())
                await new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(directory, "up.sql")), connection).ExecuteNonQueryAsync();
            var provision = new RESTAURANT::NexaConnect.Services.Restaurant.Infrastructure.Persistence.PostgresRestaurantProvisioningRepository(source);
            Guid organization = Guid.NewGuid();
            var restaurant = await provision.CreateRestaurantAsync(new(organization, "pricing", "Pricing", "THB", "Asia/Bangkok"), "test", default);
            var branch = (await provision.CreateBranchAsync(restaurant.RestaurantId, new("pricing", "Pricing", "THB", "Asia/Bangkok"), "test", default))!;
            var repository = new RESTAURANT::NexaConnect.Services.Restaurant.Infrastructure.Persistence.PostgresBranchProductConfigurationRepository(source);
            var service = new RESTAURANT::NexaConnect.Services.Restaurant.Application.Configuration.BranchProductConfigurationService(repository);
            var original = (await service.GetPricingAsync(organization, branch.BranchId, default))!;
            Assert.Equal(0, original.TaxPercent);
            Assert.Null(await service.GetPricingAsync(Guid.NewGuid(), branch.BranchId, default));
            var updated = (await service.UpdateAsync(organization, branch.BranchId, new(true, true, false, 10, original.ConcurrencyVersion, 7, true), "test", default))!;
            Assert.Equal(7, updated.TaxPercent);
            Assert.True(updated.TaxInclusive);
            Assert.Null(await service.UpdateAsync(organization, branch.BranchId, new(true, true, false, 0, original.ConcurrencyVersion, 0, false), "test", default));
            Assert.Null(await service.UpdateAsync(Guid.NewGuid(), branch.BranchId, new(true, true, false, 0, updated.ConcurrencyVersion), "test", default));
            var omitted = (await service.UpdateAsync(organization, branch.BranchId, new(true, true, false, 5, updated.ConcurrencyVersion), "test", default))!;
            Assert.Equal(7, omitted.TaxPercent);
            Assert.True(omitted.TaxInclusive);
            Assert.Equal(2L, await new NpgsqlCommand("SELECT count(*) FROM branch_management_audit WHERE action='branch.configuration.updated'", connection).ExecuteScalarAsync());
            await new NpgsqlCommand("UPDATE branches SET status='suspended'", connection).ExecuteNonQueryAsync();
            Assert.Null(await service.GetPricingAsync(organization, branch.BranchId, default));
        }
        finally { await new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", connection).ExecuteNonQueryAsync(); }
    }

    [PricingDatabaseFact]
    public async Task Pricing_roundtrip_concurrent_identity_settlement_and_rollback_guards()
    {
        string schema = $"pricing_it_{Guid.NewGuid():N}";
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NEXACONNECT_ORDER_INTEGRATION_DB")!) { SearchPath = schema };
        await using var source = NpgsqlDataSource.Create(builder.ConnectionString);
        await using var connection = await source.OpenConnectionAsync();
        await new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection).ExecuteNonQueryAsync();
        try
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(root.FullName, "NexaConnect.sln"))) root = root.Parent!;
            string scripts = Path.Combine(root.FullName, "src/Tools/NexaConnect.DataMigration/Scripts/Order");
            foreach (string directory in Directory.GetDirectories(scripts).Order())
                await new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(directory, "up.sql")), connection).ExecuteNonQueryAsync();
            string down = await File.ReadAllTextAsync(Path.Combine(scripts, "0008_authoritative_pricing/down.sql"));
            await new NpgsqlCommand(down, connection).ExecuteNonQueryAsync();
            await new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(scripts, "0008_authoritative_pricing/up.sql")), connection).ExecuteNonQueryAsync();

            var repository = new PostgresOrderRepository(source);
            OrderLine[] lines = [new(Guid.NewGuid(), "Item", 107, 1, "grill")];
            var pricing = OrderPricing.Calculate(lines, "THB", new(5, 7, true, 10));
            Guid organization = Guid.NewGuid(), branch = Guid.NewGuid(), restaurant = Guid.NewGuid();
            OrderAggregate Create() => OrderAggregate.Create(Guid.NewGuid(), organization, branch, lines, "THB", restaurant,
                idempotencyKey: "same-checkout", workflowPaymentMethod: "cash_manual", pricing: pricing, pricingFingerprint: new string('A', 64));
            async Task<bool> Submit(OrderAggregate candidate)
            {
                candidate.Submit();
                try
                {
                    await repository.SaveWithEventAsync(candidate, new OrderSubmittedV1(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
                        candidate.Id, organization, branch, [], candidate.TotalAmount, "THB"), default);
                    return true;
                }
                catch (OrderPlacementConflictException) { return false; }
            }
            var winners = await Task.WhenAll(Submit(Create()), Submit(Create()));
            Assert.Single(winners, v => v);
            var stored = (await repository.FindByIdempotencyKeyAsync(restaurant, "same-checkout", default))!;
            Assert.Equal(pricing, stored.Pricing);
            Assert.Equal("grill", Assert.Single(stored.Lines).PreparationStation);
            Assert.Equal(1L, await new NpgsqlCommand("SELECT count(*) FROM orders", connection).ExecuteScalarAsync());
            Assert.Equal(1L, await new NpgsqlCommand("SELECT count(*) FROM outbox_messages", connection).ExecuteScalarAsync());
            stored.MarkInventoryReserved(); await repository.SaveAsync(stored, default);
            stored.MarkKitchenAccepted(); await repository.SaveAsync(stored, default);
            var recovered = (await new PostgresOrderRepository(source).GetAsync(stored.Id, default))!;
            Assert.Equal(117.70m, recovered.TotalAmount);

            var tenders = new ManualTenderApplicationService(repository);
            var command = new ConfirmManualTenderCommand(organization, branch, stored.Id, Guid.NewGuid(), Guid.NewGuid(),
                "cash", recovered.TotalAmount, "THB", false, null, "cashier", Guid.NewGuid(), Guid.NewGuid());
            Assert.Null(await tenders.ConfirmAsync(command with { OrganizationId = Guid.NewGuid() }, default));
            await Assert.ThrowsAsync<ArgumentException>(() => tenders.ConfirmAsync(command with { Amount = 107 }, default));
            var results = await Task.WhenAll(tenders.ConfirmAsync(command, default), tenders.ConfirmAsync(command, default));
            Assert.All(results, result => Assert.Equal(117.70m, result!.Amount));
            Assert.Equal(1L, await new NpgsqlCommand("SELECT count(*) FROM order_manual_tender_settlements", connection).ExecuteScalarAsync());
            Assert.Equal(OrderStatus.Paid, (await repository.GetAsync(stored.Id, default))!.Status);
            var receipt = (await repository.GetReceiptAsync(organization, branch, stored.Id, default))!;
            Assert.Equal(117.70m, receipt.TotalAmount);
            Assert.Equal("cash", receipt.Tender);
            Assert.Equal(pricing, receipt.Pricing);
            Assert.InRange(Math.Abs((results[0]!.OccurredAtUtc - receipt.PaidAtUtc).Ticks), 0, 9);
            Assert.Equal(receipt.ReceiptNumber, (await repository.GetAsync(stored.Id, default))!.Receipt!.ReceiptNumber);
            await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand("UPDATE orders SET pricing_fingerprint=repeat('B',64)", connection).ExecuteNonQueryAsync());
            await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand(down, connection).ExecuteNonQueryAsync());
            Assert.Equal(pricing, (await repository.GetAsync(stored.Id, default))!.Pricing);
        }
        finally
        {
            await new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", connection).ExecuteNonQueryAsync();
        }
    }
}

public sealed class PricingDatabaseFactAttribute : FactAttribute
{
    public PricingDatabaseFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("NEXACONNECT_PRICING_ACCEPTANCE") != "1"
            || Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") != "Testing"
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NEXACONNECT_ORDER_INTEGRATION_DB")))
            Skip = "Requires explicit pricing acceptance opt-in and a disposable Testing PostgreSQL database.";
    }
}
