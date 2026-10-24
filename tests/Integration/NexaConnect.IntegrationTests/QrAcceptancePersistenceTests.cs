extern alias ORDER;
extern alias INVENTORY;
extern alias KITCHEN;
using Npgsql;
using NexaConnect.Contracts.RestaurantQr;
using ORDER::NexaConnect.Services.Order.Application.Orders;
using ORDER::NexaConnect.Services.Order.Application.Tenant;
using ORDER::NexaConnect.Services.Order.Application.Workflow;
using ORDER::NexaConnect.Services.Order.Domain;
using ORDER::NexaConnect.Services.Order.Domain.Qr;
using ORDER::NexaConnect.Services.Order.Infrastructure.Persistence;
using InventoryStore=INVENTORY::NexaConnect.Services.Inventory.Infrastructure.PostgresInventoryReservations;
using KitchenStore=KITCHEN::NexaConnect.Services.Kitchen.Infrastructure.PostgresKitchenTicketStore;
namespace NexaConnect.IntegrationTests;
public sealed class QrAcceptancePersistenceTests
{
    [ConfiguredEnvironmentFact("NEXACONNECT_QR_INTEGRATION_DB")]
    public async Task Acceptance_is_atomic_reviewed_single_order_and_recovers_committed_dependency_response_loss()
    {
        var settings=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NEXACONNECT_QR_INTEGRATION_DB"));Assert.Equal("127.0.0.1",settings.Host);Assert.Matches("^nexa_qr_it_[a-f0-9]{32}$",settings.Database!);
        string suffix=Guid.NewGuid().ToString("N");string[] schemas=["qr_accept_"+suffix,"qr_stock_"+suffix,"qr_kitchen_"+suffix];
        await using var admin=new NpgsqlConnection(settings.ConnectionString);await admin.OpenAsync();
        try
        {
            foreach(string schema in schemas){await using var command=new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"",admin);await command.ExecuteNonQueryAsync();}
            NpgsqlDataSource Source(string schema){var s=new NpgsqlConnectionStringBuilder(settings.ConnectionString){SearchPath=schema};return NpgsqlDataSource.Create(s.ConnectionString);}
            await using var orders=Source(schemas[0]);await using var stock=Source(schemas[1]);await using var kitchen=Source(schemas[2]);
            async Task Migrate(NpgsqlDataSource source,string component){string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../src/Tools/NexaConnect.DataMigration/Scripts",component));foreach(var dir in Directory.GetDirectories(root).Order())await Sql(source,await File.ReadAllTextAsync(Path.Combine(dir,"up.sql")));}
            await Migrate(orders,"Order");await Migrate(stock,"Inventory");await Migrate(kitchen,"Kitchen");
            var port=new Port();var auth=new Auth();var repo=new PostgresQrRequestRepository(orders);var policies=new Policies();var menu=new QrMenuService(port,policies,TimeProvider.System);
            var requests=new QrRequestService(menu,repo,auth);var acceptance=new QrAcceptanceService(repo,port,port,policies,auth,menu,TimeProvider.System);
            var inventory=new InventoryStore(stock);inventory.SetStock(port.Context.OrganizationId,port.Context.BranchId,port.Product,10,new("fixture",Guid.NewGuid()));
            var quote=await requests.QuoteAsync(Token,new([new(port.Product,1)]),default);var request=await requests.SubmitAsync(Token,new(quote.QuoteId!.Value,quote.Fingerprint),Guid.NewGuid(),default);
            auth.Allowed=false;await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>acceptance.AcceptAsync(port.Context.OrganizationId,port.Context.BranchId,request.Id,new(1),"operator","Bearer staff",Guid.NewGuid(),default));auth.Allowed=true;
            port.Price=214;var review=(await acceptance.AcceptAsync(port.Context.OrganizationId,port.Context.BranchId,request.Id,new(1),"operator","Bearer staff",Guid.NewGuid(),default))!;
            Assert.Equal("price_review",review.Status);Assert.Equal(235.4m,review.ReviewQuote!.TotalAmount);Assert.Equal(0,await Count(orders,"orders"));Assert.Equal(10,inventory.GetStock(port.Context.OrganizationId,port.Context.BranchId).Single().AvailableQuantity);
            var originalVisitor=port.Context.VisitorId;port.Context=port.Context with{VisitorId=Guid.NewGuid()};await Assert.ThrowsAsync<GuestRequestConflictException>(()=>acceptance.ConfirmAsync(Token,request.Id,new(review.Version,review.ReviewQuote.Fingerprint),Guid.NewGuid(),default));port.Context=port.Context with{VisitorId=originalVisitor};
            var confirmed=await acceptance.ConfirmAsync(Token,request.Id,new(review.Version,review.ReviewQuote.Fingerprint),Guid.NewGuid(),default);Assert.Equal("pending",confirmed.Status);Assert.Equal(235.4m,confirmed.Quote.TotalAmount);
            port.DineIn=false;await Assert.ThrowsAsync<GuestRequestConflictException>(()=>acceptance.AcceptAsync(port.Context.OrganizationId,port.Context.BranchId,request.Id,new(confirmed.Version),"operator","Bearer staff",Guid.NewGuid(),default));port.DineIn=true;
            var accepted=await Task.WhenAll(Enumerable.Range(0,12).Select(_=>acceptance.AcceptAsync(port.Context.OrganizationId,port.Context.BranchId,request.Id,new(confirmed.Version),"operator","Bearer staff",Guid.NewGuid(),default)));
            Assert.All(accepted,r=>Assert.Equal(request.Id,r!.OrderId));Assert.Equal(1,await Count(orders,"orders"));Assert.Equal(1,await Count(orders,"order_qr_acceptances"));Assert.Equal(0,await Count(kitchen,"kitchen_tickets"));
            var original=new PostgresOrderRepository(orders);var saved=await original.GetAsync(request.Id,default);Assert.Equal("qr",saved!.Channel);Assert.Equal("dine_in",saved.ServiceType);Assert.Equal("cash_manual",saved.WorkflowPaymentMethod);Assert.Equal(235.4m,saved.TotalAmount);Assert.Null(saved.PaymentIntentId);Assert.Null(saved.Receipt);
            await using(var eventCount=orders.CreateCommand("SELECT count(*) FROM outbox_messages WHERE event_type='OrderSubmittedV1'"))Assert.Equal(1L,await eventCount.ExecuteScalarAsync());
            // Each dependency commits before its simulated transport response is lost.
            var inventoryPort=new StockPort(inventory);var kitchenPort=new KitchenPort(new KitchenStore(kitchen));var recovery=new OrderWorkflowRecoveryService(original,inventoryPort,kitchenPort,new NoPayment(),new RecoveryClock());
            await Assert.ThrowsAsync<HttpRequestException>(()=>recovery.RecoverNextAsync(TimeSpan.FromMinutes(2),TimeSpan.Zero,default));
            Assert.True(await recovery.RecoverNextAsync(TimeSpan.FromMinutes(2),TimeSpan.Zero,default));
            await Assert.ThrowsAsync<HttpRequestException>(()=>recovery.RecoverNextAsync(TimeSpan.FromMinutes(2),TimeSpan.Zero,default));
            Assert.True(await recovery.RecoverNextAsync(TimeSpan.FromMinutes(2),TimeSpan.Zero,default));Assert.False(await recovery.RecoverNextAsync(TimeSpan.FromMinutes(2),TimeSpan.Zero,default));
            Assert.Equal(9,inventory.GetStock(port.Context.OrganizationId,port.Context.BranchId).Single().AvailableQuantity);Assert.Equal(1,await Count(kitchen,"kitchen_tickets"));Assert.Equal(1,await Count(stock,"inventory_reservation_lines"));
            Assert.Equal(OrderStatus.KitchenAccepted,(await original.GetAsync(request.Id,default))!.Status);
            var replay=(await acceptance.AcceptAsync(port.Context.OrganizationId,port.Context.BranchId,request.Id,new(1),"operator","Bearer staff",Guid.NewGuid(),default))!;Assert.Equal("kitchen_accepted",replay.OrderStatus);
            await Assert.ThrowsAsync<PostgresException>(()=>Sql(orders,"UPDATE order_qr_acceptances SET table_code='tampered'"));
            // A failed joined acceptance/outbox transaction leaves the request pending and no replacement Order.
            port.Context=port.Context with{VisitorId=Guid.NewGuid()};var q2=await requests.QuoteAsync(Token,new([new(port.Product,1)]),default);var r2=await requests.SubmitAsync(Token,new(q2.QuoteId!.Value,q2.Fingerprint),Guid.NewGuid(),default);
            await Sql(orders,"ALTER TABLE outbox_messages RENAME TO unavailable_outbox_messages");
            try{await Assert.ThrowsAsync<PostgresException>(()=>acceptance.AcceptAsync(port.Context.OrganizationId,port.Context.BranchId,r2.Id,new(1),"operator","Bearer staff",Guid.NewGuid(),default));}
            finally{await Sql(orders,"ALTER TABLE unavailable_outbox_messages RENAME TO outbox_messages");}
            Assert.Equal(1,await Count(orders,"orders"));Assert.Equal("pending",(await repo.GetAsync(port.Context.OrganizationId,port.Context.BranchId,r2.Id,default))!.Request.Status);
            await repo.RejectAsync(port.Context.OrganizationId,port.Context.BranchId,r2.Id,new(1,"closing"),"operator",Guid.NewGuid(),Guid.NewGuid(),default);
            await Assert.ThrowsAsync<GuestRequestConflictException>(()=>acceptance.AcceptAsync(port.Context.OrganizationId,port.Context.BranchId,r2.Id,new(1),"operator","Bearer staff",Guid.NewGuid(),default));Assert.Equal(1,await Count(orders,"orders"));
            // Restart reads preserve accepted provenance and cleanup never removes it.
            Assert.Equal(request.Id,(await new PostgresQrRequestRepository(orders).GetAsync(port.Context.OrganizationId,port.Context.BranchId,request.Id,default))!.OrderId);
            port.Context=port.Context with{VisitorId=Guid.NewGuid()};var q3=await requests.QuoteAsync(Token,new([new(port.Product,1)]),default);var r3=await requests.SubmitAsync(Token,new(q3.QuoteId!.Value,q3.Fingerprint),Guid.NewGuid(),default);
            await acceptance.AcceptAsync(port.Context.OrganizationId,port.Context.BranchId,r3.Id,new(1),"operator","Bearer staff",Guid.NewGuid(),default);
            inventory.SetStock(port.Context.OrganizationId,port.Context.BranchId,port.Product,0,new("fixture",Guid.NewGuid()));
            Assert.True(await recovery.RecoverNextAsync(TimeSpan.FromMinutes(2),TimeSpan.Zero,default));Assert.Equal(OrderStatus.Rejected,(await original.GetAsync(r3.Id,default))!.Status);Assert.Equal(1,await Count(kitchen,"kitchen_tickets"));
        }
        finally{foreach(string schema in schemas){await using var command=new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE",admin);await command.ExecuteNonQueryAsync();}}
    }
    private const string Token="SSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSS";
    private static async Task Sql(NpgsqlDataSource source,string sql){await using var command=source.CreateCommand(sql);await command.ExecuteNonQueryAsync();}
    private static async Task<long> Count(NpgsqlDataSource source,string table){Assert.Contains(table,new[]{"orders","order_qr_acceptances","kitchen_tickets","inventory_reservation_lines"});await using var c=source.CreateCommand($"SELECT count(*) FROM \"{table}\"");return (long)(await c.ExecuteScalarAsync())!;}
    private sealed class Port:IQrMenuPort,IQrAcceptancePort
    {
        public QrGuestContext Context=new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"R","B","A1","THB",DateTimeOffset.UtcNow.AddHours(1));public Guid Product=Guid.NewGuid();public decimal Price=107;public bool DineIn=true;
        public Task<QrGuestContext?> ContextAsync(string token,CancellationToken ct)=>Task.FromResult<QrGuestContext?>(Context);
        public Task<QrAcceptanceContext?> ContextAsync(GuestRequestScope scope,CancellationToken ct)=>Task.FromResult<QrAcceptanceContext?>(new(Context,DineIn));
        public Task<IReadOnlyList<CatalogMenuItem>> MenuAsync(Guid org,Guid branch,CancellationToken ct){Assert.Equal(Context.OrganizationId,org);Assert.Equal(Context.BranchId,branch);return Task.FromResult<IReadOnlyList<CatalogMenuItem>>([new(Product,"Soup",Price,"THB",true,"kitchen")]);}
    }
    private sealed class Policies:IBranchPricingPort{public Task<PricingPolicy> GetAsync(Guid org,Guid restaurant,Guid branch,CancellationToken ct)=>Task.FromResult(new PricingPolicy(1,7,true,10));}
    private sealed class Auth:IOrderTenantAuthorizer{public bool Allowed=true;public Task<bool> HasBranchAccessAsync(Guid org,Guid branch,string p,string b,CancellationToken ct)=>Task.FromResult(Allowed);public Task<Guid?> GetBranchDecisionAsync(Guid org,Guid branch,string p,string b,CancellationToken ct)=>Task.FromResult<Guid?>(Allowed?Guid.NewGuid():null);}
    private sealed class StockPort(InventoryStore store):IInventoryReservationPort
    {
        private bool lose=true;
        public Task<InventoryReservationResult> ReserveAsync(Guid org,Guid id,Guid branch,IReadOnlyCollection<OrderLine> lines,CancellationToken ct)
        {try{var result=store.Reserve(org,new(id,branch,lines.Select(l=>new INVENTORY::NexaConnect.Services.Inventory.Application.Reservations.ReservationLine(l.ProductId,l.Quantity)).ToArray()),new("fixture",id));if(lose){lose=false;throw new HttpRequestException("simulated response loss");}return Task.FromResult(new InventoryReservationResult(true,result.ReservationId,null));}catch(InvalidOperationException){return Task.FromResult(new InventoryReservationResult(false,null,"unavailable"));}}
    }
    private sealed class KitchenPort(KitchenStore store):IKitchenPort
    {
        private bool lose=true;
        public async Task<KitchenTicketResult> CreateTicketAsync(Guid org,Guid restaurant,Guid id,Guid branch,IReadOnlyCollection<OrderLine> lines,CancellationToken ct)
        {var result=await store.CreateAsync(org,new(restaurant,id,branch,lines.Select(l=>new KITCHEN::NexaConnect.Services.Kitchen.Application.KitchenTicketLine(l.ProductId,l.Name,l.Quantity,l.PreparationStation)).ToArray()),new("fixture",id),ct);if(lose){lose=false;throw new HttpRequestException("simulated response loss");}return new(result.TicketId);}
    }
    private sealed class NoPayment:IPaymentPort{public Task<PaymentResult> AuthorizeAsync(Guid org,Guid restaurant,Guid branch,Guid id,decimal amount,string currency,string method,CancellationToken ct)=>throw new InvalidOperationException("QR acceptance must stay unpaid.");}
    private sealed class RecoveryClock:TimeProvider{public override DateTimeOffset GetUtcNow()=>DateTimeOffset.UtcNow.AddMinutes(1);}
}
