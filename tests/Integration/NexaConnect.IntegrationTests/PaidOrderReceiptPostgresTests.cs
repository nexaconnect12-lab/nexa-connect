extern alias ORDER;
using Npgsql;
using NexaConnect.Contracts.IntegrationEvents;
using ORDER::NexaConnect.Services.Order.Domain;
using ORDER::NexaConnect.Services.Order.Infrastructure.Persistence;

namespace NexaConnect.IntegrationTests;
public sealed class PaidOrderReceiptPostgresTests
{
    [PricingDatabaseFact]
    public async Task Historical_receipt_backfill_is_scoped_attributed_and_promptpay_uses_settlement_identity()
    {
        string schema = $"sale_history_it_{Guid.NewGuid():N}";
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NEXACONNECT_ORDER_INTEGRATION_DB")!) { SearchPath = schema };
        await using var source = NpgsqlDataSource.Create(builder.ConnectionString);
        await using var connection = await source.OpenConnectionAsync();
        await new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"",connection).ExecuteNonQueryAsync();
        try
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(root.FullName,"NexaConnect.sln"))) root = root.Parent!;
            foreach (string directory in Directory.GetDirectories(Path.Combine(root.FullName,"src/Tools/NexaConnect.DataMigration/Scripts/Order")).Order())
                await new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(directory,"up.sql")),connection).ExecuteNonQueryAsync();
            var repo = new PostgresOrderRepository(source);
            Guid org = Guid.NewGuid(), restaurant = Guid.NewGuid(), branch = Guid.NewGuid(), payment = Guid.NewGuid();
            OrderAggregate Create(string method)
            {
                var value = OrderAggregate.Create(Guid.NewGuid(),org,branch,[new OrderLine(Guid.NewGuid(),"Rice",10m,1,"kitchen")],"THB",restaurantId:restaurant,workflowPaymentMethod:method);
                value.Submit(); value.MarkInventoryReserved(); value.MarkKitchenAccepted(); return value;
            }
            var historical = Create("card_omise_test"); await repo.SaveAsync(historical,default);
            historical.MarkPaid(payment); historical.IssueReceipt(DateTimeOffset.UtcNow.AddMinutes(-1),"card_omise_test");
            // Simulate the old binary committing receipt evidence before publication migration deployment.
            await using (var update = new NpgsqlCommand("UPDATE orders SET status='completed',payment_intent_id=$2,receipt_snapshot=$3::jsonb WHERE id=$1",connection))
            {
                update.Parameters.AddWithValue(historical.Id); update.Parameters.AddWithValue(payment);
                update.Parameters.AddWithValue(System.Text.Json.JsonSerializer.Serialize(historical.Receipt)); await update.ExecuteNonQueryAsync();
            }
            var missing = Create("card_omise_test"); await repo.SaveAsync(missing,default);
            await using (var update = new NpgsqlCommand("UPDATE orders SET status='completed' WHERE id=$1",connection))
            { update.Parameters.AddWithValue(missing.Id); await update.ExecuteNonQueryAsync(); }
            DateTimeOffset from = DateTimeOffset.UtcNow.AddDays(-1), to = from.AddDays(2);
            var replay = new PostgresSaleReplay(source);
            var dry = await replay.RunAsync(org,branch,from,to,false,default);
            Assert.Equal(2,dry.Candidates); Assert.Equal(1,dry.MissingReceipts); Assert.Empty(dry.Publications);
            await Assert.ThrowsAsync<ArgumentException>(() => replay.RunAsync(org,branch,from,to,true,default));
            var applied = await replay.RunAsync(org,branch,from,to,true,default,"test-operator");
            Assert.Equal(1,applied.Published); Assert.Equal(1,applied.Requeued); Assert.Equal(1,applied.MissingReceipts);
            Assert.Equal(historical.Receipt!.PaidAtUtc,applied.Publications.Single().PaidAtUtc);
            Assert.Equal(payment,applied.Publications.Single().PaymentId);
            Assert.Equal(1L,await new NpgsqlCommand("SELECT count(*) FROM order_sale_replay_audit WHERE actor='test-operator'",connection).ExecuteScalarAsync());
            var promptpay = Create("promptpay_manual"); await repo.SaveAsync(promptpay,default);
            var settlement = await new ORDER::NexaConnect.Services.Order.Application.ManualTenders.ManualTenderApplicationService(repo)
                .ConfirmAsync(new(org,branch,promptpay.Id,Guid.NewGuid(),Guid.NewGuid(),"promptpay_manual",10m,"THB",true,"test-ref","cashier",Guid.NewGuid(),Guid.NewGuid()),default);
            var evidence = (await replay.RunAsync(org,branch,from,to,false,default)).Publications.Single(v => v.OrderId == promptpay.Id);
            Assert.Equal("manual_settlement",evidence.PaymentOrigin); Assert.Equal("promptpay_manual",evidence.Method);
            Assert.Equal(settlement!.SettlementId,evidence.PaymentId);
            await new NpgsqlCommand("UPDATE outbox_messages SET payload='{}'::jsonb WHERE event_type='order.sale-completed.v1'",connection).ExecuteNonQueryAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => replay.RunAsync(org,branch,from,to,true,default,"test-operator"));
        }
        finally { await new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE",connection).ExecuteNonQueryAsync(); }
    }

    [PricingDatabaseFact]
    public async Task Receipt_is_atomic_immutable_scoped_and_unique_under_concurrent_completion()
    {
        string schema = $"receipt_it_{Guid.NewGuid():N}";
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NEXACONNECT_ORDER_INTEGRATION_DB")!) { SearchPath = schema };
        await using var source = NpgsqlDataSource.Create(builder.ConnectionString);
        await using var connection = await source.OpenConnectionAsync();
        await new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection).ExecuteNonQueryAsync();
        try
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(root.FullName,"NexaConnect.sln"))) root = root.Parent!;
            string scripts = Path.Combine(root.FullName,"src/Tools/NexaConnect.DataMigration/Scripts/Order");
            foreach (string directory in Directory.GetDirectories(scripts).Order())
                await new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(directory,"up.sql")),connection).ExecuteNonQueryAsync();
            var repo = new PostgresOrderRepository(source);
            var lines = new OrderLine[] { new(Guid.NewGuid(),"Original rice",107,1,"kitchen") };
            var pricing = OrderPricing.Calculate(lines,"THB",new(1,7,true,10));
            var order = OrderAggregate.Create(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),lines,"THB",
                idempotencyKey:Guid.NewGuid().ToString("N"),pricing:pricing,pricingFingerprint:new string('A',64),workflowPaymentMethod:"card_omise_test");
            order.Submit(); order.MarkInventoryReserved(); order.MarkKitchenAccepted();
            await repo.SaveAsync(order,default);
            Assert.Null(await repo.GetReceiptAsync(order.OrganizationId,order.BranchId,order.Id,default));
            Guid paymentId=Guid.NewGuid(), eventId=Guid.NewGuid();
            var completion = new PaymentCompletedV1(eventId,Guid.NewGuid(),DateTimeOffset.UtcNow,order.Id,paymentId,order.TotalAmount,"THB","card_omise_test");
            var first = (await repo.GetAsync(order.Id,default))!;
            var second = (await repo.GetAsync(order.Id,default))!;
            first.MarkPaid(paymentId); first.IssueReceipt(DateTimeOffset.UtcNow,"card_omise_test");
            second.MarkPaid(paymentId); second.IssueReceipt(DateTimeOffset.UtcNow.AddSeconds(1),"card_omise_test");
            // A failed outbox insert must roll back both status and receipt.
            await new NpgsqlCommand("CREATE FUNCTION fail_receipt_outbox() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'acceptance failure'; END $$; CREATE TRIGGER receipt_test_failure BEFORE INSERT ON outbox_messages FOR EACH ROW EXECUTE FUNCTION fail_receipt_outbox()",connection).ExecuteNonQueryAsync();
            await Assert.ThrowsAsync<PostgresException>(()=>repo.SaveWithEventAsync(first,completion,default));
            Assert.Equal(OrderStatus.KitchenAccepted,(await repo.GetAsync(order.Id,default))!.Status);
            Assert.Null(await repo.GetReceiptAsync(order.OrganizationId,order.BranchId,order.Id,default));
            await new NpgsqlCommand("DROP TRIGGER receipt_test_failure ON outbox_messages; DROP FUNCTION fail_receipt_outbox()",connection).ExecuteNonQueryAsync();
            await Task.WhenAll(repo.SaveWithEventAsync(first,completion,default),repo.SaveWithEventAsync(second,completion,default));
            var receipt = (await repo.GetReceiptAsync(order.OrganizationId,order.BranchId,order.Id,default))!;
            Assert.Equal(order.TotalAmount,receipt.TotalAmount);
            Assert.Equal("Original rice",receipt.Lines.Single().Name);
            Assert.Contains(receipt.PaidAtUtc,new[]{first.Receipt!.PaidAtUtc,second.Receipt!.PaidAtUtc});
            Assert.Null(await repo.GetReceiptAsync(Guid.NewGuid(),order.BranchId,order.Id,default));
            Assert.Null(await repo.GetReceiptAsync(order.OrganizationId,Guid.NewGuid(),order.Id,default));
            // Rehydrate/re-save never replaces the committed receipt.
            await repo.SaveAsync((await repo.GetAsync(order.Id,default))!,default);
            Assert.Equal(receipt.PaidAtUtc,(await repo.GetReceiptAsync(order.OrganizationId,order.BranchId,order.Id,default))!.PaidAtUtc);
            Assert.Equal(1L,await new NpgsqlCommand("SELECT count(*) FROM orders WHERE receipt_snapshot IS NOT NULL",connection).ExecuteScalarAsync());
            Assert.Equal(2L,await new NpgsqlCommand("SELECT count(*) FROM outbox_messages",connection).ExecuteScalarAsync());
            Assert.Equal(1L,await new NpgsqlCommand("SELECT count(*) FROM order_sale_publications",connection).ExecuteScalarAsync());
            var replay = new PostgresSaleReplay(source);
            var dry = await replay.RunAsync(order.OrganizationId,order.BranchId,receipt.PaidAtUtc.AddDays(-1),receipt.PaidAtUtc.AddDays(1),false,default);
            Assert.Single(dry.Publications); Assert.Equal(0,dry.Requeued);
            Assert.Equal(receipt.PaidAtUtc,dry.Publications[0].PaidAtUtc);
            var applied = await replay.RunAsync(order.OrganizationId,order.BranchId,receipt.PaidAtUtc.AddDays(-1),receipt.PaidAtUtc.AddDays(1),true,default,"acceptance");
            Assert.Equal(0,applied.Published); Assert.Equal(1,applied.Requeued);
            Assert.Empty((await replay.RunAsync(Guid.NewGuid(),order.BranchId,receipt.PaidAtUtc.AddDays(-1),receipt.PaidAtUtc.AddDays(1),true,default,"acceptance")).Publications);
            await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand("UPDATE order_sale_publications SET payload=payload",connection).ExecuteNonQueryAsync());
            await Assert.ThrowsAsync<PostgresException>(async () => await new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(scripts,"0011_sale_publications/down.sql")),connection).ExecuteNonQueryAsync());
            await Assert.ThrowsAsync<PostgresException>(()=>new NpgsqlCommand("UPDATE orders SET receipt_snapshot=NULL",connection).ExecuteNonQueryAsync());
            await Assert.ThrowsAsync<PostgresException>(()=>new NpgsqlCommand("DELETE FROM orders",connection).ExecuteNonQueryAsync());
            var malformedOrder = OrderAggregate.Create(Guid.NewGuid(),order.OrganizationId,order.BranchId,lines,"THB",
                idempotencyKey:Guid.NewGuid().ToString("N"),pricing:pricing,pricingFingerprint:new string('B',64));
            malformedOrder.Submit(); malformedOrder.MarkInventoryReserved(); malformedOrder.MarkKitchenAccepted(); malformedOrder.MarkPaid();
            malformedOrder.IssueReceipt(DateTimeOffset.UtcNow,"cash");
            var malformedNode = System.Text.Json.Nodes.JsonNode.Parse(
                System.Text.Json.JsonSerializer.Serialize(malformedOrder.Receipt!))!;
            malformedNode["SubtotalAmount"] = 99;
            string malformed = malformedNode.ToJsonString();
            await Assert.ThrowsAsync<PostgresException>(()=>new NpgsqlCommand($"INSERT INTO orders(id,organization_id,restaurant_id,branch_id,order_number,currency,channel,service_type,subtotal_amount,service_charge_amount,tax_amount,total_amount,status,receipt_snapshot,created_at_utc,created_by,updated_at_utc,updated_by) VALUES('{malformedOrder.Id}','{malformedOrder.OrganizationId}','{malformedOrder.RestaurantId}','{malformedOrder.BranchId}','{malformedOrder.OrderNumber}','THB','pos','takeaway',{pricing.SubtotalAmount},{pricing.ServiceChargeAmount},{pricing.TaxAmount},{pricing.TotalAmount},'completed',$json${malformed}$json$,now(),'test',now(),'test')",connection).ExecuteNonQueryAsync());
            await Assert.ThrowsAsync<PostgresException>(()=>new NpgsqlCommand(File.ReadAllText(Path.Combine(scripts,"0009_paid_order_receipts/down.sql")),connection).ExecuteNonQueryAsync());
        }
        finally { await new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE",connection).ExecuteNonQueryAsync(); }
    }
}
