extern alias ORDER;
extern alias INVENTORY;
extern alias KITCHEN;
extern alias POS;
extern alias REPORTING;
using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using ORDER::NexaConnect.Services.Order.Application.ManualTenders;
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
        string suffix=Guid.NewGuid().ToString("N");string[] schemas=["qr_accept_"+suffix,"qr_stock_"+suffix,"qr_kitchen_"+suffix,"qr_pos_"+suffix,"qr_report_"+suffix];
        await using var admin=new NpgsqlConnection(settings.ConnectionString);await admin.OpenAsync();
        try
        {
            foreach(string schema in schemas){await using var command=new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"",admin);await command.ExecuteNonQueryAsync();}
            NpgsqlDataSource Source(string schema){var s=new NpgsqlConnectionStringBuilder(settings.ConnectionString){SearchPath=schema};return NpgsqlDataSource.Create(s.ConnectionString);}
            await using var orders=Source(schemas[0]);await using var stock=Source(schemas[1]);await using var kitchen=Source(schemas[2]);
            await using var pos=Source(schemas[3]);await using var reporting=Source(schemas[4]);
            async Task Migrate(NpgsqlDataSource source,string component){string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../src/Tools/NexaConnect.DataMigration/Scripts",component));foreach(var dir in Directory.GetDirectories(root).Order())await Sql(source,await File.ReadAllTextAsync(Path.Combine(dir,"up.sql")));}
            await Migrate(orders,"Order");await Migrate(stock,"Inventory");await Migrate(kitchen,"Kitchen");
            await Migrate(pos,"POS");await Migrate(reporting,"Reporting");
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
            var unavailable=(await repo.GetAsync(port.Context.OrganizationId,port.Context.BranchId,r3.Id,default))!.View();
            Assert.Equal("unavailable",unavailable.CollectionStatus);Assert.Null(unavailable.CollectionReference);
            // QR5: real owning admission, immutable collecting custody and one financial handoff.
            var store=Guid.NewGuid();var terminal=Guid.NewGuid();var shift=Guid.NewGuid();var scope=port.Context;
            await Seed(pos,"INSERT INTO stores(id,restaurant_id,branch_id,code,name,operational_status,created_at_utc,created_by,updated_at_utc,updated_by) VALUES($1,$2,$3,'qr','QR','active',now(),'fixture',now(),'fixture')",store,scope.RestaurantId,scope.BranchId);
            await Seed(pos,"INSERT INTO terminals(id,restaurant_id,store_id,code,device_type,registration_status,registered_at_utc,created_at_utc,updated_at_utc) VALUES($1,$2,$3,'qr','pos','active',now(),now(),now())",terminal,scope.RestaurantId,store);
            var shiftAggregate=POS::NexaConnect.Services.POS.Domain.Shifts.Shift.Open(shift,store,terminal,"cashier","QR",Guid.NewGuid(),DateTimeOffset.UtcNow.AddMinutes(-1));
            await new POS::NexaConnect.Services.POS.Infrastructure.Persistence.PostgresShiftStore(pos).CreateAsync(shiftAggregate,default);
            await new POS::NexaConnect.Services.POS.Infrastructure.Persistence.PostgresCashSessionStore(pos).OpenAsync(shift,store,"THB",0,default);
            var admission=new Admission(pos,scope.OrganizationId,scope.RestaurantId,scope.BranchId);
            var claims=new PostgresQrCollectionRepository(orders);
            var collection=new QrCollectionService(repo,original,claims,auth,admission,TimeProvider.System);
            var collectionCommand=new QrCollectionCommand(scope.RestaurantId,terminal,store,shift,Guid.NewGuid());
            auth.Allowed=false;await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>collection.CollectAsync(scope.OrganizationId,scope.BranchId,request.Id,collectionCommand,"cashier","Bearer staff",Guid.NewGuid(),default));auth.Allowed=true;
            Assert.Null(await collection.CollectAsync(Guid.NewGuid(),scope.BranchId,request.Id,collectionCommand,"cashier","Bearer staff",Guid.NewGuid(),default));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>collection.CollectAsync(scope.OrganizationId,scope.BranchId,r3.Id,collectionCommand,"cashier","Bearer staff",Guid.NewGuid(),default));
            await Assert.ThrowsAsync<ManualTenderAdmissionConflictException>(()=>collection.CollectAsync(scope.OrganizationId,scope.BranchId,request.Id,collectionCommand with{ShiftId=Guid.NewGuid()},"cashier","Bearer staff",Guid.NewGuid(),default));
            await collection.CollectAsync(scope.OrganizationId,scope.BranchId,request.Id,collectionCommand,"cashier","Bearer staff",Guid.NewGuid(),default,preview:true);Assert.Null(await claims.BindingAsync(request.Id,default));
            var collected=await Task.WhenAll(Enumerable.Range(0,12).Select(_=>collection.CollectAsync(scope.OrganizationId,scope.BranchId,request.Id,collectionCommand,"cashier","Bearer staff",Guid.NewGuid(),default)));
            Assert.All(collected,c=>Assert.Equal(request.Id,c!.OrderId));
            Assert.Equal("collecting",(await repo.GetAsync(scope.OrganizationId,scope.BranchId,request.Id,default))!.CollectionStatus);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>collection.CollectAsync(scope.OrganizationId,scope.BranchId,request.Id,collectionCommand with{SettlementKey=Guid.NewGuid()},"cashier","Bearer staff",Guid.NewGuid(),default));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>collection.CollectAsync(scope.OrganizationId,scope.BranchId,request.Id,collectionCommand,"other","Bearer staff",Guid.NewGuid(),default));
            var tender=new ConfirmManualTenderCommand(scope.OrganizationId,scope.BranchId,request.Id,terminal,collectionCommand.SettlementKey,"cash",235.4m,"THB",false,null,"cashier",Guid.NewGuid(),Guid.NewGuid());
            var payments=new ManualTenderApplicationService(original,admission);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>payments.ConfirmAsync(tender with{OperatorSubjectId="other"},default));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>payments.ConfirmAsync(tender with{IdempotencyKey=Guid.NewGuid()},default));
            await Sql(pos,"UPDATE terminals SET registration_status='revoked',revoked_at_utc=now()");
            await Assert.ThrowsAsync<ManualTenderAdmissionConflictException>(()=>payments.ConfirmAsync(tender,default));await Sql(pos,"UPDATE terminals SET registration_status='active',revoked_at_utc=NULL");
            // Simulate commit followed by loss of the response; restart only replays this collectionCommand.
            var paidAttempts=await Task.WhenAll(Enumerable.Range(0,12).Select(_=>payments.ConfirmAsync(tender,default)));
            var paid=Assert.Single(paidAttempts,p=>!p!.Replayed)!;Assert.All(paidAttempts,p=>Assert.Equal(paid.SettlementId,p!.SettlementId));
            await Sql(pos,"UPDATE shifts SET status='closed',closed_at_utc=now()");
            var replayed=await new ManualTenderApplicationService(new PostgresOrderRepository(orders),admission).ConfirmAsync(tender,default);
            Assert.True(replayed!.Replayed);Assert.Equal(paid.SettlementId,replayed.SettlementId);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>payments.ConfirmAsync(tender with{OperatorSubjectId="other"},default));
            Assert.Equal("Paid",(await collection.CollectAsync(scope.OrganizationId,scope.BranchId,request.Id,collectionCommand,"cashier","Bearer staff",Guid.NewGuid(),default))!.Status);
            var receipt=(await original.GetReceiptAsync(scope.OrganizationId,scope.BranchId,request.Id,default))!;
            Assert.Equal(235.4m,receipt.TotalAmount);Assert.Equal("cash",receipt.Tender);
            await using var financial=orders.CreateCommand("SELECT payload::text FROM outbox_messages WHERE event_type='order.manual-tender-settled.v1'");
            var settled=JsonSerializer.Deserialize<OrderManualTenderSettledV1>((string)(await financial.ExecuteScalarAsync())!)!;
            var projection=new POS::NexaConnect.Services.POS.Infrastructure.Persistence.PostgresOrderSettlementProjectionStore(pos);
            Assert.Equal(POS::NexaConnect.Services.POS.Application.OrderSettlements.OrderSettlementProjectionStatus.Applied,await projection.ProjectAsync(settled,default));
            Assert.Equal(POS::NexaConnect.Services.POS.Application.OrderSettlements.OrderSettlementProjectionStatus.Replayed,await projection.ProjectAsync(settled,default));
            await using(var movement=pos.CreateCommand("SELECT count(*),sum(amount) FROM cash_movements WHERE movement_type='sale'"))
            {await using var reader=await movement.ExecuteReaderAsync();Assert.True(await reader.ReadAsync());Assert.Equal(1,reader.GetInt64(0));Assert.Equal(235.4m,reader.GetDecimal(1));}
            await using var saleQuery=orders.CreateCommand("SELECT payload::text FROM order_sale_publications WHERE order_id=$1");saleQuery.Parameters.AddWithValue(request.Id);
            var sale=JsonSerializer.Deserialize<OrderSaleCompletedV1>((string)(await saleQuery.ExecuteScalarAsync())!)!;
            var reports=new REPORTING::NexaConnect.Services.Reporting.Application.SaleFinancialReporting(new REPORTING::NexaConnect.Services.Reporting.Infrastructure.Persistence.PostgresSaleFinancialFactRepository(reporting));
            Assert.True(await reports.ProjectAsync(sale,default));Assert.False(await reports.ProjectAsync(sale,default));
            Assert.Equal(receipt.TotalAmount,sale.TotalAmount);Assert.Equal("qr",sale.Channel);
            port.Context=port.Context with{VisitorId=originalVisitor};
            var guest=(await new QrRequestService(menu,repo,auth,original).MineAsync(Token,default)).Items.Single();
            Assert.Equal("paid",guest.OrderStatus);Assert.Equal("paid",guest.CollectionStatus);Assert.Equal(receipt.ReceiptNumber,guest.Receipt!.ReceiptNumber);Assert.Equal(receipt.TotalAmount,guest.Receipt.TotalAmount);
            await Assert.ThrowsAsync<PostgresException>(()=>Sql(orders,"UPDATE order_qr_collections SET actor='other'"));
            Assert.Equal(1,await Count(kitchen,"kitchen_tickets"));Assert.Equal(1,await Count(stock,"inventory_reservation_lines"));
            // PromptPay uses a fresh open shift with no drawer and never creates another cash movement.
            var nextShift=POS::NexaConnect.Services.POS.Domain.Shifts.Shift.Open(Guid.NewGuid(),store,terminal,"cashier","QR-PROMPT",Guid.NewGuid(),DateTimeOffset.UtcNow);
            await new POS::NexaConnect.Services.POS.Infrastructure.Persistence.PostgresShiftStore(pos).CreateAsync(nextShift,default);
            inventory.SetStock(scope.OrganizationId,scope.BranchId,port.Product,10,new("fixture",Guid.NewGuid()));
            port.Context=port.Context with{VisitorId=Guid.NewGuid()};
            var promptQuote=await requests.QuoteAsync(Token,new([new(port.Product,1)]),default);
            var promptRequest=await requests.SubmitAsync(Token,new(promptQuote.QuoteId!.Value,promptQuote.Fingerprint),Guid.NewGuid(),default);
            await acceptance.AcceptAsync(scope.OrganizationId,scope.BranchId,promptRequest.Id,new(1),"operator","Bearer staff",Guid.NewGuid(),default);
            Assert.True(await recovery.RecoverNextAsync(TimeSpan.FromMinutes(2),TimeSpan.Zero,default));Assert.True(await recovery.RecoverNextAsync(TimeSpan.FromMinutes(2),TimeSpan.Zero,default));
            var promptCommand=collectionCommand with{ShiftId=nextShift.Id,SettlementKey=Guid.NewGuid()};
            await Assert.ThrowsAsync<InvalidOperationException>(()=>collection.CollectAsync(scope.OrganizationId,scope.BranchId,promptRequest.Id,promptCommand with{SettlementKey=collectionCommand.SettlementKey},"cashier","Bearer staff",Guid.NewGuid(),default));
            Assert.Null(await claims.BindingAsync(promptRequest.Id,default));
            await Sql(orders,"ALTER TABLE outbox_messages RENAME TO collection_unavailable_outbox");
            try { await Assert.ThrowsAsync<PostgresException>(()=>collection.CollectAsync(scope.OrganizationId,scope.BranchId,promptRequest.Id,promptCommand,"cashier","Bearer staff",Guid.NewGuid(),default)); }
            finally { await Sql(orders,"ALTER TABLE collection_unavailable_outbox RENAME TO outbox_messages"); }
            Assert.Null(await claims.BindingAsync(promptRequest.Id,default));
            await collection.CollectAsync(scope.OrganizationId,scope.BranchId,promptRequest.Id,promptCommand,"cashier","Bearer staff",Guid.NewGuid(),default);
            var promptTender=tender with{OrderId=promptRequest.Id,IdempotencyKey=promptCommand.SettlementKey,Method="promptpay_manual",ReceiptConfirmed=true,BankReference="fixture-confirmed"};
            var promptPaid=await payments.ConfirmAsync(promptTender,default);Assert.Equal("promptpay_manual",promptPaid!.Method);
            Assert.True((await payments.ConfirmAsync(promptTender,default))!.Replayed);
            await using var promptEventQuery=orders.CreateCommand("SELECT payload::text FROM outbox_messages WHERE event_type='order.manual-tender-settled.v1' AND aggregate_id=$1");promptEventQuery.Parameters.AddWithValue(promptRequest.Id);
            var promptEvent=JsonSerializer.Deserialize<OrderManualTenderSettledV1>((string)(await promptEventQuery.ExecuteScalarAsync())!)!;
            await projection.ProjectAsync(promptEvent,default);await projection.ProjectAsync(promptEvent,default);
            await using(var movementCount=pos.CreateCommand("SELECT count(*) FROM cash_movements WHERE movement_type='sale'"))Assert.Equal(1L,await movementCount.ExecuteScalarAsync());
            var collectionDown=await File.ReadAllTextAsync(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../src/Tools/NexaConnect.DataMigration/Scripts/Order/0022_qr_collection/down.sql")));
            await Assert.ThrowsAsync<PostgresException>(()=>Sql(orders,collectionDown));
        }
        finally{foreach(string schema in schemas){await using var command=new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE",admin);await command.ExecuteNonQueryAsync();}}
    }
    private const string Token="SSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSS";
    private static async Task Sql(NpgsqlDataSource source,string sql){await using var command=source.CreateCommand(sql);await command.ExecuteNonQueryAsync();}
    private static async Task Seed(NpgsqlDataSource source,string sql,params object[] values){await using var c=source.CreateCommand(sql);foreach(var v in values)c.Parameters.AddWithValue(v);await c.ExecuteNonQueryAsync();}
    internal sealed class Admission(NpgsqlDataSource source,Guid org,Guid restaurant,Guid branch):IManualTenderAdmission
    {
        public async Task<DateTimeOffset> AdmitAsync(ManualTenderAdmissionRequest r,CancellationToken ct)
        {
            var app=new POS::NexaConnect.Services.POS.Application.Terminals.ManualTenderAdmission(new POS::NexaConnect.Services.POS.Infrastructure.Persistence.PostgresManualTenderAdmissionStore(source),new Hierarchy(org,restaurant,branch),TimeProvider.System);
            return await app.AdmitAsync(new(r.OrganizationId,r.RestaurantId,r.BranchId,r.TerminalId,r.OperatorSubjectId,r.Method,r.Currency,r.ExpectedShiftId,r.ExpectedStoreId),ct)??throw new ManualTenderAdmissionConflictException();
        }
    }
    private sealed class Hierarchy(Guid org,Guid restaurant,Guid branch):POS::NexaConnect.Services.POS.Application.Shifts.IRestaurantScopeReader
    {public Task<POS::NexaConnect.Services.POS.Application.Shifts.RestaurantAuthorizationScope> GetAsync(Guid id,CancellationToken ct)=>Task.FromResult(new POS::NexaConnect.Services.POS.Application.Shifts.RestaurantAuthorizationScope(org,restaurant,branch));}
    private static async Task<long> Count(NpgsqlDataSource source,string table){Assert.Contains(table,new[]{"orders","order_qr_acceptances","kitchen_tickets","inventory_reservation_lines"});await using var c=source.CreateCommand($"SELECT count(*) FROM \"{table}\"");return (long)(await c.ExecuteScalarAsync())!;}
    private sealed class Port:IQrMenuPort,IQrAcceptancePort
    {
        public QrGuestContext Context=new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"R","B","A1","THB",DateTimeOffset.UtcNow.AddHours(1));public Guid Product=Guid.NewGuid();public decimal Price=107;public bool DineIn=true;
        public Task<QrGuestContext?> ContextAsync(string token,CancellationToken ct)=>Task.FromResult<QrGuestContext?>(Context);
        public Task<QrAcceptanceContext?> ContextAsync(GuestRequestScope scope,CancellationToken ct)=>Task.FromResult<QrAcceptanceContext?>(new(Context,DineIn));
        public Task<IReadOnlyList<CatalogMenuItem>> MenuAsync(Guid org,Guid branch,CancellationToken ct){Assert.Equal(Context.OrganizationId,org);Assert.Equal(Context.BranchId,branch);return Task.FromResult<IReadOnlyList<CatalogMenuItem>>([new(Product,"Soup",Price,"THB",true,"kitchen")]);}
    }
    private sealed class Policies:IBranchPricingPort{public Task<PricingPolicy> GetAsync(Guid org,Guid restaurant,Guid branch,CancellationToken ct)=>Task.FromResult(new PricingPolicy(1,7,true,10));}
    internal sealed class Auth:IOrderTenantAuthorizer{public bool Allowed=true;public Task<bool> HasBranchAccessAsync(Guid org,Guid branch,string p,string b,CancellationToken ct)=>Task.FromResult(Allowed);public Task<Guid?> GetBranchDecisionAsync(Guid org,Guid branch,string p,string b,CancellationToken ct)=>Task.FromResult<Guid?>(Allowed?Guid.NewGuid():null);}
    internal sealed class StockPort(InventoryStore store):IInventoryReservationPort
    {
        private bool lose=true;
        public Task<InventoryReservationResult> ReserveAsync(Guid org,Guid id,Guid branch,IReadOnlyCollection<OrderLine> lines,CancellationToken ct)
        {try{var result=store.Reserve(org,new(id,branch,lines.Select(l=>new INVENTORY::NexaConnect.Services.Inventory.Application.Reservations.ReservationLine(l.ProductId,l.Quantity)).ToArray()),new("fixture",id));if(lose){lose=false;throw new HttpRequestException("simulated response loss");}return Task.FromResult(new InventoryReservationResult(true,result.ReservationId,null));}catch(InvalidOperationException){return Task.FromResult(new InventoryReservationResult(false,null,"unavailable"));}}
    }
    internal sealed class KitchenPort(KitchenStore store):IKitchenPort
    {
        private bool lose=true;
        public async Task<KitchenTicketResult> CreateTicketAsync(Guid org,Guid restaurant,Guid id,Guid branch,IReadOnlyCollection<OrderLine> lines,CancellationToken ct)
        {var result=await store.CreateAsync(org,new(restaurant,id,branch,lines.Select(l=>new KITCHEN::NexaConnect.Services.Kitchen.Application.KitchenTicketLine(l.ProductId,l.Name,l.Quantity,l.PreparationStation)).ToArray()),new("fixture",id),ct);if(lose){lose=false;throw new HttpRequestException("simulated response loss");}return new(result.TicketId);}
    }
    internal sealed class NoPayment:IPaymentPort{public Task<PaymentResult> AuthorizeAsync(Guid org,Guid restaurant,Guid branch,Guid id,decimal amount,string currency,string method,CancellationToken ct)=>throw new InvalidOperationException("QR acceptance must stay unpaid.");}
    internal sealed class RecoveryClock:TimeProvider{public override DateTimeOffset GetUtcNow()=>DateTimeOffset.UtcNow.AddMinutes(1);}
}
