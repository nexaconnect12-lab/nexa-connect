extern alias RESTAURANT;
extern alias CATALOG;
extern alias ORDER;
extern alias INVENTORY;
extern alias KITCHEN;
extern alias POS;
extern alias REPORTING;
using System.Text.Json;
using Npgsql;
using RabbitMQ.Client;
using Microsoft.Extensions.Options;
using NexaConnect.Contracts.RestaurantQr;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Infrastructure.Messaging;
using ORDER::NexaConnect.Services.Order.Application.Orders;
using ORDER::NexaConnect.Services.Order.Application.ManualTenders;
using ORDER::NexaConnect.Services.Order.Application.Workflow;
using ORDER::NexaConnect.Services.Order.Domain.Qr;
using ORDER::NexaConnect.Services.Order.Domain;
using ORDER::NexaConnect.Services.Order.Infrastructure.Persistence;
using EntryRepo=RESTAURANT::NexaConnect.Services.Restaurant.Infrastructure.Persistence.PostgresQrEntryRepository;
using EntryService=RESTAURANT::NexaConnect.Services.Restaurant.Application.Qr.QrEntryService;
using ConfigRepo=RESTAURANT::NexaConnect.Services.Restaurant.Infrastructure.Persistence.PostgresBranchProductConfigurationRepository;
using CatalogRepo=CATALOG::NexaConnect.Services.Catalog.Infrastructure.PostgresMenuCatalog;
using StockRepo=INVENTORY::NexaConnect.Services.Inventory.Infrastructure.PostgresInventoryReservations;
using KitchenRepo=KITCHEN::NexaConnect.Services.Kitchen.Infrastructure.PostgresKitchenTicketStore;

namespace NexaConnect.IntegrationTests;

public sealed class QrJourneyPersistenceTests
{
    [ConfiguredEnvironmentFact("NEXACONNECT_QR_INTEGRATION_DB")]
    public async Task Original_live_visits_join_menu_acceptance_recovery_payment_broker_and_owning_financial_projections()
    {
        var settings=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NEXACONNECT_QR_INTEGRATION_DB"));
        Assert.Equal("127.0.0.1",settings.Host);Assert.Matches("^nexa_qr_it_[a-f0-9]{32}$",settings.Database!);
        var broker=new Uri(Environment.GetEnvironmentVariable("NEXACONNECT_QR_INTEGRATION_BROKER")!);
        Assert.Equal("amqp",broker.Scheme);Assert.Equal("127.0.0.1",broker.Host);Assert.True(broker.Port>1024);
        string suffix=Guid.NewGuid().ToString("N");string[] owners=["Restaurant","Catalog","Order","Inventory","Kitchen","POS","Reporting"];
        var schemas=owners.Select(o=>"qr_journey_"+o.ToLowerInvariant()+"_"+suffix).ToArray();
        await using var admin=NpgsqlDataSource.Create(settings.ConnectionString);var sources=new Dictionary<string,NpgsqlDataSource>();
        try
        {
            for(int i=0;i<owners.Length;i++)
            {
                await Sql(admin,$"CREATE SCHEMA \"{schemas[i]}\"");
                var source=NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(settings.ConnectionString){SearchPath=schemas[i]}.ConnectionString);sources.Add(owners[i],source);
                string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../src/Tools/NexaConnect.DataMigration/Scripts",owners[i]));
                foreach(var directory in Directory.GetDirectories(root).Order())await Sql(source,await File.ReadAllTextAsync(Path.Combine(directory,"up.sql")));
            }
            var restaurant=sources["Restaurant"];var orders=sources["Order"];var pos=sources["POS"];
            Guid org=Guid.NewGuid(),restaurantId=Guid.NewGuid(),branch=Guid.NewGuid(),product=Guid.NewGuid();
            await Sql(restaurant,"INSERT INTO restaurants(id,organization_id,code,name,default_currency,default_time_zone,status,created_at_utc,updated_at_utc,created_by,updated_by) VALUES($1,$2,'journey','Restaurant','THB','Asia/Singapore','active',now(),now(),'fixture','fixture')",restaurantId,org);
            await Sql(restaurant,"INSERT INTO branches(id,restaurant_id,code,name,time_zone,currency,status,created_at_utc,updated_at_utc,created_by,updated_by) VALUES($1,$2,'journey','Branch','Asia/Singapore','THB','active',now(),now(),'fixture','fixture')",branch,restaurantId);
            var configuration=new ConfigRepo(restaurant);var policy=(await configuration.GetAsync(org,branch,default))!;
            await configuration.UpdateAsync(org,branch,new(true,true,true,10,policy.ConcurrencyVersion,7,true),"fixture",default);
            var entries=new EntryService(new EntryRepo(restaurant),new EntryAuth(),new RESTAURANT::NexaConnect.Services.Restaurant.Infrastructure.QrSecrets(),TimeProvider.System);
            var table=await entries.CreateAsync(org,branch,"A1","staff","Bearer fixture",default);
            var otherTable=await entries.CreateAsync(org,branch,"B1","staff","Bearer fixture",default);
            var a=(await entries.StartAsync(table.EntryToken,default))!;var b=(await entries.StartAsync(table.EntryToken,default))!;
            var main=(await entries.StartAsync(otherTable.EntryToken,default))!;
            Assert.NotEqual(a.Context.VisitorId,b.Context.VisitorId);
            var catalog=new CatalogRepo(sources["Catalog"]);catalog.AddForOrganizationBranch(org,branch,new(product,"Soup",107,"THB","kitchen"));
            var port=new JourneyPort(entries,new EntryRepo(restaurant),configuration,catalog);
            var auth=new QrAcceptancePersistenceTests.Auth();var qr=new PostgresQrRequestRepository(orders);var original=new PostgresOrderRepository(orders);
            var menu=new QrMenuService(port,port,TimeProvider.System);var requests=new QrRequestService(menu,qr,auth,original);
            var acceptance=new QrAcceptanceService(qr,port,port,port,auth,menu,TimeProvider.System);
            var quote=await requests.QuoteAsync(a.SessionToken,new([new(product,1)]),default);
            await Assert.ThrowsAsync<GuestRequestConflictException>(()=>requests.SubmitAsync(b.SessionToken,new(quote.QuoteId!.Value,quote.Fingerprint),Guid.NewGuid(),default));
            var proposal=await requests.SubmitAsync(a.SessionToken,new(quote.QuoteId!.Value,quote.Fingerprint),Guid.NewGuid(),default);
            Assert.Empty((await requests.MineAsync(b.SessionToken,default)).Items);Assert.Empty((await requests.MineAsync(main.SessionToken,default)).Items);
            catalog.AddForOrganizationBranch(org,branch,new(product,"Soup",214,"THB","kitchen"));
            var review=(await acceptance.AcceptAsync(org,branch,proposal.Id,new(1),"staff","Bearer fixture",Guid.NewGuid(),default))!;
            await Assert.ThrowsAsync<GuestRequestConflictException>(()=>acceptance.ConfirmAsync(b.SessionToken,proposal.Id,new(review.Version,review.ReviewQuote!.Fingerprint),Guid.NewGuid(),default));
            await acceptance.ConfirmAsync(a.SessionToken,proposal.Id,new(review.Version,review.ReviewQuote!.Fingerprint),Guid.NewGuid(),default);
            await entries.ChangeAsync(org,branch,table.Entry.Id,table.Entry.Version,true,"staff","Bearer fixture",default);
            await Assert.ThrowsAsync<GuestRequestConflictException>(()=>acceptance.AcceptAsync(org,branch,proposal.Id,new(3),"staff","Bearer fixture",Guid.NewGuid(),default));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>requests.MineAsync(a.SessionToken,default));
            var inventory=new StockRepo(sources["Inventory"]);inventory.SetStock(org,branch,product,10,new("fixture",Guid.NewGuid()));
            Assert.Equal(0L,await Count(orders,"orders"));Assert.Equal(0L,await Count(sources["Kitchen"],"kitchen_tickets"));
            // The unaffected table's actual live session proceeds through the original commercial snapshot.
            var mainQuote=await requests.QuoteAsync(main.SessionToken,new([new(product,1)]),default);
            var submissions=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>requests.SubmitAsync(main.SessionToken,new(mainQuote.QuoteId!.Value,mainQuote.Fingerprint),Guid.NewGuid(),default)));
            Guid id=submissions[0].Id;Assert.All(submissions,s=>Assert.Equal(id,s.Id));
            await Task.WhenAll(Enumerable.Range(0,8).Select(_=>acceptance.AcceptAsync(org,branch,id,new(1),"staff","Bearer fixture",Guid.NewGuid(),default)));
            Assert.Equal(10,inventory.GetStock(org,branch).Single().AvailableQuantity);Assert.Equal(0L,await Count(sources["Kitchen"],"kitchen_tickets"));
            var recovery=new OrderWorkflowRecoveryService(original,new QrAcceptancePersistenceTests.StockPort(inventory),new QrAcceptancePersistenceTests.KitchenPort(new KitchenRepo(sources["Kitchen"])),new QrAcceptancePersistenceTests.NoPayment(),new QrAcceptancePersistenceTests.RecoveryClock());
            await Assert.ThrowsAsync<HttpRequestException>(()=>recovery.RecoverNextAsync(TimeSpan.FromMinutes(2),TimeSpan.Zero,default));
            Assert.True(await recovery.RecoverNextAsync(TimeSpan.FromMinutes(2),TimeSpan.Zero,default));
            await Assert.ThrowsAsync<HttpRequestException>(()=>recovery.RecoverNextAsync(TimeSpan.FromMinutes(2),TimeSpan.Zero,default));
            Assert.True(await recovery.RecoverNextAsync(TimeSpan.FromMinutes(2),TimeSpan.Zero,default));
            Guid store=Guid.NewGuid(),terminal=Guid.NewGuid(),shift=Guid.NewGuid();
            await Sql(pos,"INSERT INTO stores(id,restaurant_id,branch_id,code,name,operational_status,created_at_utc,created_by,updated_at_utc,updated_by) VALUES($1,$2,$3,'journey','Journey','active',now(),'fixture',now(),'fixture')",store,restaurantId,branch);
            await Sql(pos,"INSERT INTO terminals(id,restaurant_id,store_id,code,device_type,registration_status,registered_at_utc,created_at_utc,updated_at_utc) VALUES($1,$2,$3,'journey','pos','active',now(),now(),now())",terminal,restaurantId,store);
            await new POS::NexaConnect.Services.POS.Infrastructure.Persistence.PostgresShiftStore(pos).CreateAsync(POS::NexaConnect.Services.POS.Domain.Shifts.Shift.Open(shift,store,terminal,"cashier","JOURNEY",Guid.NewGuid(),DateTimeOffset.UtcNow.AddMinutes(-1)),default);
            await new POS::NexaConnect.Services.POS.Infrastructure.Persistence.PostgresCashSessionStore(pos).OpenAsync(shift,store,"THB",0,default);
            var admission=new QrAcceptancePersistenceTests.Admission(pos,org,restaurantId,branch);
            var collection=new QrCollectionService(qr,original,new PostgresQrCollectionRepository(orders),auth,admission,TimeProvider.System);
            var collect=new QrCollectionCommand(restaurantId,terminal,store,shift,Guid.NewGuid());
            await collection.CollectAsync(org,branch,id,collect,"cashier","Bearer fixture",Guid.NewGuid(),default);
            var tender=new ConfirmManualTenderCommand(org,branch,id,terminal,collect.SettlementKey,"cash",mainQuote.TotalAmount,"THB",false,null,"cashier",Guid.NewGuid(),Guid.NewGuid());
            await new ManualTenderApplicationService(original,admission).ConfirmAsync(tender,default);
            Assert.True((await new ManualTenderApplicationService(new PostgresOrderRepository(orders),admission).ConfirmAsync(tender,default))!.Replayed);
            Assert.Equal(0L,await Count(pos,"pos_order_settlements"));Assert.Equal(0L,await Count(sources["Reporting"],"sales_facts"));
            await PublishAndProject(orders,pos,sources["Reporting"],broker,id);
            var mine=Assert.Single((await requests.MineAsync(main.SessionToken,default)).Items);var receipt=(await original.GetReceiptAsync(org,branch,id,default))!;
            Assert.Equal("paid",mine.CollectionStatus);Assert.Equal(receipt.ReceiptNumber,mine.Receipt!.ReceiptNumber);Assert.Equal(receipt.TotalAmount,mine.Receipt.TotalAmount);
            Assert.Equal(1L,await Count(orders,"orders"));Assert.Equal(1L,await Count(sources["Kitchen"],"kitchen_tickets"));Assert.Equal(9,inventory.GetStock(org,branch).Single().AvailableQuantity);
            await entries.EndAsync(main.SessionToken,default);await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>requests.MineAsync(main.SessionToken,default));
        }
        finally
        {
            foreach(var source in sources.Values)await source.DisposeAsync();
            foreach(string schema in schemas)await Sql(admin,$"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE");
        }
    }
    private static async Task PublishAndProject(NpgsqlDataSource orders,NpgsqlDataSource pos,NpgsqlDataSource reporting,Uri broker,Guid id)
    {
        string exchange="qr.journey."+Guid.NewGuid().ToString("N");
        var store=new PostgresOutboxStore(orders);var financial=(await store.ClaimBatchAsync(100,default)).Where(m=>m.AggregateId==id&&m.EventType is "order.manual-tender-settled.v1" or "order.sale-completed.v1").ToArray();Assert.Equal(2,financial.Length);
        await using(var unavailable=new RabbitMqOutboxTransport(Options.Create(new OutboxOptions{Exchange=exchange}),ct=>new ConnectionFactory{Uri=new Uri("amqp://fixture:unavailable@127.0.0.1:1"),RequestedConnectionTimeout=TimeSpan.FromSeconds(1),AutomaticRecoveryEnabled=false}.CreateConnectionAsync(ct)))
        {
            foreach(var message in financial){await Assert.ThrowsAnyAsync<Exception>(()=>unavailable.PublishAsync(message,default));await store.MarkFailedAsync(message.Id,"connection_unavailable",default);}
        }
        await using(var check=orders.CreateCommand("SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1 AND event_type IN('order.manual-tender-settled.v1','order.sale-completed.v1') AND published_at_utc IS NULL AND last_error_category='connection_unavailable'"))
        {check.Parameters.AddWithValue(id);Assert.Equal(2L,await check.ExecuteScalarAsync());}
        // Cross the owning outbox's real 30-second lease; no retry-clock override is used here.
        await Task.Delay(TimeSpan.FromSeconds(31));
        var recovered=(await new PostgresOutboxStore(orders).ClaimBatchAsync(100,default)).Where(m=>financial.Any(f=>f.Id==m.Id)).ToArray();Assert.Equal(2,recovered.Length);
        await using var connection=await new ConnectionFactory{Uri=broker}.CreateConnectionAsync();await using var channel=await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(exchange,ExchangeType.Topic,true,false);
        var queue=(await channel.QueueDeclareAsync("",false,true,true)).QueueName;await channel.QueueBindAsync(queue,exchange,"order.#");
        try
        {
            await using var transport=new RabbitMqOutboxTransport(connection,Options.Create(new OutboxOptions{Exchange=exchange}));
            foreach(var message in recovered){await transport.PublishAsync(message,default);await transport.PublishAsync(message,default);await store.MarkPublishedAsync(message.Id,default);}
            var cash=new POS::NexaConnect.Services.POS.Infrastructure.Persistence.PostgresOrderSettlementProjectionStore(pos);
            var sales=new REPORTING::NexaConnect.Services.Reporting.Application.SaleFinancialReporting(new REPORTING::NexaConnect.Services.Reporting.Infrastructure.Persistence.PostgresSaleFinancialFactRepository(reporting));
            for(int n=0;n<4;n++)
            {
                var delivery=await channel.BasicGetAsync(queue,false);Assert.NotNull(delivery);
                if(delivery.RoutingKey=="order.manual-tender-settled.v1")await cash.ProjectAsync(JsonSerializer.Deserialize<OrderManualTenderSettledV1>(delivery.Body.Span)!,default);
                else {Assert.Equal("order.sale-completed.v1",delivery.RoutingKey);await sales.ProjectAsync(JsonSerializer.Deserialize<OrderSaleCompletedV1>(delivery.Body.Span)!,default);}
                await channel.BasicAckAsync(delivery.DeliveryTag,false);
            }
            Assert.Equal(1L,await Count(pos,"pos_order_settlements"));Assert.Equal(1L,await Count(pos,"cash_movements"));Assert.Equal(1L,await Count(reporting,"sales_facts"));Assert.Equal(1L,await Count(reporting,"payment_facts"));
        }
        finally{await channel.ExchangeDeleteAsync(exchange,false);}
    }
    private static async Task Sql(NpgsqlDataSource source,string sql,params object[] values){await using var c=source.CreateCommand(sql);foreach(var value in values)c.Parameters.AddWithValue(value);await c.ExecuteNonQueryAsync();}
    private static async Task<long> Count(NpgsqlDataSource source,string table){Assert.Contains(table,new[]{"orders","kitchen_tickets","pos_order_settlements","cash_movements","sales_facts","payment_facts"});await using var c=source.CreateCommand($"SELECT count(*) FROM \"{table}\"");return (long)(await c.ExecuteScalarAsync())!;}
    private sealed class EntryAuth:RESTAURANT::NexaConnect.Services.Restaurant.Application.Branches.IBranchCustomerAuthorizer
    {public Task<bool> IsGrantedAsync(Guid org,Guid? restaurant,Guid? branch,string permission,string bearer,CancellationToken ct)=>Task.FromResult(true);}
    private sealed class JourneyPort(EntryService entries,EntryRepo repository,ConfigRepo configuration,CatalogRepo catalog):IQrMenuPort,IQrAcceptancePort,IBranchPricingPort
    {
        public Task<QrGuestContext?> ContextAsync(string token,CancellationToken ct)=>entries.ContextAsync(token,ct);
        public async Task<QrAcceptanceContext?> ContextAsync(GuestRequestScope scope,CancellationToken ct)
        {var context=await repository.VisitorAsync(scope.OrganizationId,scope.BranchId,scope.VisitorId,scope.EntryId,DateTimeOffset.UtcNow,ct);var policy=await configuration.GetAsync(scope.OrganizationId,scope.BranchId,ct,true);return context is null?null:new(context,policy?.DineInEnabled==true);}
        public Task<IReadOnlyList<CatalogMenuItem>> MenuAsync(Guid org,Guid branch,CancellationToken ct)=>Task.FromResult<IReadOnlyList<CatalogMenuItem>>(catalog.GetGuestMenu(org,branch).Select(i=>new CatalogMenuItem(i.ProductId,i.Name,i.UnitPrice,i.Currency,i.Available,i.PreparationStation)).ToArray());
        public async Task<PricingPolicy> GetAsync(Guid org,Guid restaurant,Guid branch,CancellationToken ct)
        {var value=await configuration.GetAsync(org,branch,ct,true)??throw new HttpRequestException();Assert.Equal(restaurant,value.RestaurantId);return new(value.ConcurrencyVersion,value.TaxPercent,value.TaxInclusive,value.ServiceChargePercent);}
    }
}
