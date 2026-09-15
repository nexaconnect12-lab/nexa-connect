extern alias MIGRATIONS;
extern alias ORDER;

using MigrationApplication = MIGRATIONS::MigrationApplication;
using OrderAggregate = ORDER::NexaConnect.Services.Order.Domain.OrderAggregate;
using OrderLine = ORDER::NexaConnect.Services.Order.Domain.OrderLine;
using PostgresOrderRepository = ORDER::NexaConnect.Services.Order.Infrastructure.Persistence.PostgresOrderRepository;
using ManualTenderApplicationService = ORDER::NexaConnect.Services.Order.Application.ManualTenders.ManualTenderApplicationService;
using ConfirmManualTenderCommand = ORDER::NexaConnect.Services.Order.Application.ManualTenders.ConfirmManualTenderCommand;
using NexaConnect.Contracts.IntegrationEvents;
using Npgsql;

namespace NexaConnect.IntegrationTests;

[Collection("Order migration runner acceptance")]
public sealed class OrderMigrationRunnerAcceptanceTests
{
    [OrderMigrationAcceptanceFact]
    public async Task Empty_database_runs_0_to_7_to_6_to_7_and_guards_recoverable_workflows()
    {
        string adminConnectionString=Environment.GetEnvironmentVariable("NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB")!;
        string databaseName=$"nexaconnect_order_clean_it_{Guid.NewGuid():N}";ValidateDatabaseName(databaseName);
        var adminBuilder=new NpgsqlConnectionStringBuilder(adminConnectionString){Database="postgres"};
        await using NpgsqlDataSource admin=NpgsqlDataSource.Create(adminBuilder.ConnectionString);
        await CreateDatabaseAsync(admin,databaseName);
        string? previous=Environment.GetEnvironmentVariable("NEXACONNECT_ORDER_DB");
        try
        {
            var databaseBuilder=new NpgsqlConnectionStringBuilder(adminConnectionString){Database=databaseName};
            Environment.SetEnvironmentVariable("NEXACONNECT_ORDER_DB",databaseBuilder.ConnectionString);
            string scriptsRoot=Path.Combine(FindRepositoryRoot(),"src","Tools","NexaConnect.DataMigration","Scripts");
            Assert.Equal(0,await RunAsync(scriptsRoot,7));
            await using NpgsqlDataSource dataSource=NpgsqlDataSource.Create(databaseBuilder.ConnectionString);
            await AssertVersion7Async(dataSource);
            Assert.Equal(0,await RunAsync(scriptsRoot,6));
            await AssertVersion6Async(dataSource);
            Assert.Equal(0,await RunAsync(scriptsRoot,7));
            await AssertVersion7Async(dataSource);
            await AssertPersistedOwnershipAndOutboxAsync(dataSource);
            await AssertRecoveryClaimFencesForegroundProgressAsync(dataSource);
            await AssertKitchenAcceptedProviderPaymentCanBeClaimedAsync(dataSource);
            await AssertKitchenAcceptedManualTenderCanSettleAsync(dataSource);
            await SeedRecoverableOrderAsync(dataSource);
            Assert.NotEqual(0,await RunAsync(scriptsRoot,5,true));
            await AssertVersion7Async(dataSource);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NEXACONNECT_ORDER_DB",previous);
            await DropDatabaseAsync(admin,databaseName);
        }
    }

    private static Task<int> RunAsync(string root,int target,bool destructive=false)
    {
        var args=new List<string>{"--service","Order","--scripts-root",root,"--target",target.ToString(),"--application-version","0.16.0","--confirm"};
        if(destructive)args.AddRange(["--allow-destructive","--backup-verified"]);
        return MigrationApplication.RunAsync(args.ToArray());
    }

    private static async Task AssertVersion2Async(NpgsqlDataSource source)
    {
        await using NpgsqlConnection connection=await source.OpenConnectionAsync();
        Assert.Equal("inbox_messages",Convert.ToString(await new NpgsqlCommand("SELECT to_regclass('public.inbox_messages')::text",connection).ExecuteScalarAsync()));
        Assert.Equal(2L,Convert.ToInt64(await new NpgsqlCommand("SELECT max(version) FROM nexaconnect_schema_migrations",connection).ExecuteScalarAsync()));
        string definition=Convert.ToString(await new NpgsqlCommand("SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname='ck_orders_status'",connection).ExecuteScalarAsync())!;
        Assert.Contains("payment_pending",definition,StringComparison.Ordinal);
    }

    private static async Task AssertVersion1Async(NpgsqlDataSource source)
    {
        await using NpgsqlConnection connection=await source.OpenConnectionAsync();
        object? inbox=await new NpgsqlCommand("SELECT to_regclass('public.inbox_messages')::text",connection).ExecuteScalarAsync();
        Assert.True(inbox is null or DBNull || string.IsNullOrEmpty(Convert.ToString(inbox)));
        Assert.Equal(1L,Convert.ToInt64(await new NpgsqlCommand("SELECT max(version) FROM nexaconnect_schema_migrations",connection).ExecuteScalarAsync()));
    }

    private static async Task AssertVersion3Async(NpgsqlDataSource source)
    {
        await using NpgsqlConnection connection=await source.OpenConnectionAsync();
        Assert.Equal(3L,Convert.ToInt64(await new NpgsqlCommand("SELECT max(version) FROM nexaconnect_schema_migrations",connection).ExecuteScalarAsync()));
        string definition=Convert.ToString(await new NpgsqlCommand("SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname='ck_orders_status'",connection).ExecuteScalarAsync())!;
        Assert.Contains("payment_review",definition,StringComparison.Ordinal);
        Assert.True(await ColumnExistsAsync(source,"orders","organization_id"));
        Assert.True(await ColumnExistsAsync(source,"orders","payment_intent_id"));
    }

    private static async Task AssertVersion4Async(NpgsqlDataSource source)
    {
        await using NpgsqlConnection connection=await source.OpenConnectionAsync();
        Assert.Equal(4L,Convert.ToInt64(await new NpgsqlCommand("SELECT max(version) FROM nexaconnect_schema_migrations",connection).ExecuteScalarAsync()));
        Assert.Equal("order_payment_reviews",Convert.ToString(await new NpgsqlCommand("SELECT to_regclass('public.order_payment_reviews')::text",connection).ExecuteScalarAsync()));
    }

    private static async Task AssertVersion5Async(NpgsqlDataSource source)
    {
        await using NpgsqlConnection connection=await source.OpenConnectionAsync();
        Assert.Equal(5L,Convert.ToInt64(await new NpgsqlCommand("SELECT max(version) FROM nexaconnect_schema_migrations",connection).ExecuteScalarAsync()));
        Assert.Equal("order_manual_tender_settlements",Convert.ToString(await new NpgsqlCommand("SELECT to_regclass('public.order_manual_tender_settlements')::text",connection).ExecuteScalarAsync()));
    }

    private static async Task AssertVersion6Async(NpgsqlDataSource source)
    {
        await using NpgsqlConnection connection=await source.OpenConnectionAsync();
        Assert.Equal(6L,Convert.ToInt64(await new NpgsqlCommand("SELECT max(version) FROM nexaconnect_schema_migrations",connection).ExecuteScalarAsync()));
        Assert.True(await ColumnExistsAsync(source,"orders","workflow_recovery_claim_id"));
        string definition=Convert.ToString(await new NpgsqlCommand("SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname='ck_orders_status'",connection).ExecuteScalarAsync())!;
        Assert.Contains("inventory_reserved",definition,StringComparison.Ordinal);
        Assert.Contains("kitchen_accepted",definition,StringComparison.Ordinal);
    }

    private static async Task AssertVersion7Async(NpgsqlDataSource source)
    {
        await using NpgsqlConnection connection=await source.OpenConnectionAsync();
        Assert.Equal(7L,Convert.ToInt64(await new NpgsqlCommand("SELECT max(version) FROM nexaconnect_schema_migrations",connection).ExecuteScalarAsync()));
        string predicate=Convert.ToString(await new NpgsqlCommand("SELECT pg_get_expr(indpred,indrelid) FROM pg_index WHERE indexrelid='ix_orders_workflow_recovery'::regclass",connection).ExecuteScalarAsync())!;
        Assert.Contains("kitchen_accepted",predicate,StringComparison.Ordinal);
        Assert.Contains("workflow_payment_method",predicate,StringComparison.Ordinal);
    }

    private static async Task SeedRecoverableOrderAsync(NpgsqlDataSource source)
    {
        var order=OrderAggregate.Create(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),
            [new OrderLine(Guid.NewGuid(),"Recovery item",20m,1,"kitchen")],"THB",Guid.NewGuid(),
            idempotencyKey:"recovery-acceptance",workflowPaymentMethod:"cash_manual",workflowCorrelationId:Guid.NewGuid());
        order.Submit();
        await new PostgresOrderRepository(source).SaveWithEventAsync(order,new OrderSubmittedV1(Guid.NewGuid(),Guid.NewGuid(),
            DateTimeOffset.UtcNow,order.Id,order.OrganizationId,order.BranchId,[],order.TotalAmount,order.Currency),default);
    }

    private static async Task AssertKitchenAcceptedManualTenderCanSettleAsync(NpgsqlDataSource source)
    {
        var order=OrderAggregate.Create(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),
            [new OrderLine(Guid.NewGuid(),"Recovered item",25m,1,"kitchen")],"THB",Guid.NewGuid(),
            workflowPaymentMethod:"cash_manual",workflowCorrelationId:Guid.NewGuid());
        order.Submit();order.MarkInventoryReserved();order.MarkKitchenAccepted();
        var repository=new PostgresOrderRepository(source);
        await repository.SaveAsync(order,default);
        var service=new ManualTenderApplicationService(repository);
        var command=new ConfirmManualTenderCommand(order.OrganizationId,order.BranchId,order.Id,Guid.NewGuid(),
            Guid.NewGuid(),"cash",order.TotalAmount,"THB",false,null,"migration-acceptance",Guid.NewGuid(),Guid.NewGuid());
        var result=Assert.IsType<ORDER::NexaConnect.Services.Order.Application.ManualTenders.ManualTenderResult>(
            await service.ConfirmAsync(command,default));
        Assert.Equal("Paid",result.Status);
        await using NpgsqlConnection connection=await source.OpenConnectionAsync();
        await using var status=new NpgsqlCommand("SELECT status FROM orders WHERE id=$1",connection);
        status.Parameters.AddWithValue(order.Id);
        Assert.Equal("completed",Convert.ToString(await status.ExecuteScalarAsync()));
    }

    private static async Task AssertKitchenAcceptedProviderPaymentCanBeClaimedAsync(NpgsqlDataSource source)
    {
        var order=OrderAggregate.Create(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),
            [new OrderLine(Guid.NewGuid(),"Provider recovery item",30m,1,"kitchen")],"THB",Guid.NewGuid(),
            workflowPaymentMethod:"card",workflowCorrelationId:Guid.NewGuid());
        order.Submit();order.MarkInventoryReserved();order.MarkKitchenAccepted();
        var repository=new PostgresOrderRepository(source);
        await repository.SaveAsync(order,default);
        await using(var prioritize=source.CreateCommand("UPDATE orders SET workflow_recovery_next_attempt_at_utc=now()-interval '1 day' WHERE id=$1"))
        {prioritize.Parameters.AddWithValue(order.Id);await prioritize.ExecuteNonQueryAsync();}
        var claim=Assert.IsType<ORDER::NexaConnect.Services.Order.Application.Workflow.ClaimedOrderWorkflow>(
            await repository.ClaimNextAsync(DateTimeOffset.UtcNow.AddMinutes(1),TimeSpan.FromMinutes(1),default));
        Assert.Equal(order.Id,claim.Order.Id);
        await repository.ReleaseAsync(claim,"acceptance_complete",DateTimeOffset.UtcNow.AddDays(1),default);
    }

    private static async Task AssertRecoveryClaimFencesForegroundProgressAsync(NpgsqlDataSource source)
    {
        var order=OrderAggregate.Create(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),
            [new OrderLine(Guid.NewGuid(),"Fenced item",15m,1,"kitchen")],"THB",Guid.NewGuid(),
            workflowPaymentMethod:"cash_manual",workflowCorrelationId:Guid.NewGuid());
        order.Submit();
        var repository=new PostgresOrderRepository(source);
        await repository.SaveAsync(order,default);
        OrderAggregate foreground=Assert.IsType<OrderAggregate>(await repository.GetAsync(order.Id,default));
        OrderAggregate staleSubmitted=Assert.IsType<OrderAggregate>(await repository.GetAsync(order.Id,default));
        DateTimeOffset claimTime=DateTimeOffset.UtcNow.AddMinutes(1);
        var claim=Assert.IsType<ORDER::NexaConnect.Services.Order.Application.Workflow.ClaimedOrderWorkflow>(
            await repository.ClaimNextAsync(claimTime,TimeSpan.FromMinutes(1),default));
        foreground.MarkInventoryReserved();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>repository.SaveAsync(foreground,default));
        claim.Order.MarkInventoryReserved();
        Assert.True(await repository.CommitAsync(claim,claim.Order,
            new InventoryReservedV1(Guid.NewGuid(),claim.Order.WorkflowCorrelationId!.Value,claimTime,
                claim.Order.Id,Guid.NewGuid()),claimTime,default));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>repository.SaveAsync(staleSubmitted,default));
        OrderAggregate stored=Assert.IsType<OrderAggregate>(await repository.GetAsync(order.Id,default));
        Assert.Equal(ORDER::NexaConnect.Services.Order.Domain.OrderStatus.InventoryReserved,stored.Status);
    }

    private static async Task<bool> ColumnExistsAsync(NpgsqlDataSource source,string table,string column)
    {
        await using var command=source.CreateCommand("SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name=$1 AND column_name=$2)");
        command.Parameters.AddWithValue(table);command.Parameters.AddWithValue(column);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task AssertPersistedOwnershipAndOutboxAsync(NpgsqlDataSource source)
    {
        Guid organizationId=Guid.NewGuid();Guid paymentIntentId=Guid.NewGuid();
        var order=OrderAggregate.Create(Guid.NewGuid(),organizationId,Guid.NewGuid(),
            [new OrderLine(Guid.NewGuid(),"Acceptance item",10m,1,"kitchen")],"USD",Guid.NewGuid());
        order.Submit();order.MarkInventoryReserved();order.MarkKitchenAccepted();order.MarkPaymentPending(paymentIntentId);order.MarkPaymentReview();
        var repository=new PostgresOrderRepository(source);
        var integrationEvent=new OrderPaymentReviewRequiredV1(Guid.NewGuid(),Guid.NewGuid(),DateTimeOffset.UtcNow,
            organizationId,order.Id,paymentIntentId,"acceptance_probe");
        await repository.SaveWithEventAsync(order,integrationEvent,default);
        OrderAggregate restored=Assert.IsType<OrderAggregate>(await repository.GetAsync(order.Id,default));
        Assert.Equal(organizationId,restored.OrganizationId);Assert.Equal(paymentIntentId,restored.PaymentIntentId);
        var review=Assert.IsType<ORDER::NexaConnect.Services.Order.Application.PaymentReviews.PaymentReviewCase>(await repository.GetReviewAsync(organizationId,order.Id,default));
        var claims=await Task.WhenAll(Enumerable.Range(0,2).Select(_=>repository.ClaimResolutionAsync(review,"confirm_void","acceptance-operator",DateTimeOffset.UtcNow,default)));
        Guid firstClaim=Assert.IsType<Guid>(Assert.Single(claims,value=>value.HasValue));
        await using(var expiryConnection=await source.OpenConnectionAsync()){await using var expire=new NpgsqlCommand("UPDATE order_payment_reviews SET resolution_locked_until_utc=now()-interval '1 second' WHERE order_id=$1",expiryConnection);expire.Parameters.AddWithValue(order.Id);await expire.ExecuteNonQueryAsync();}
        var expired=Assert.IsType<ORDER::NexaConnect.Services.Order.Application.PaymentReviews.PaymentReviewCase>(await repository.GetReviewAsync(organizationId,order.Id,default));
        Assert.Null(await repository.ClaimResolutionAsync(expired,"resume_payment","other-operator",DateTimeOffset.UtcNow,default));
        Guid claimId=Assert.IsType<Guid>(await repository.ClaimResolutionAsync(expired,"confirm_void","acceptance-operator",DateTimeOffset.UtcNow,default));
        Assert.NotEqual(firstClaim,claimId);restored.ResolvePaymentReviewAsVoided();
        var resolved=new OrderPaymentReviewResolvedV1(Guid.NewGuid(),Guid.NewGuid(),DateTimeOffset.UtcNow,organizationId,order.Id,paymentIntentId,"confirm_void",expired.ConcurrencyVersion+1,Guid.NewGuid());
        var audit=new PlatformAuditEventV1(Guid.NewGuid(),resolved.CorrelationId,resolved.OccurredAtUtc,"acceptance-operator",organizationId,"order.payment-review.resolved","order",order.Id.ToString("D"),"succeeded");
        Assert.False(await repository.ResolveAsync(restored,expired,"confirm_void","stale claimant", "acceptance-operator",firstClaim,resolved,audit,default));
        await repository.ReleaseResolutionAsync(expired,firstClaim,default);
        Assert.True(await repository.ResolveAsync(restored,expired,"confirm_void","acceptance", "acceptance-operator",claimId,resolved,audit,default));
        Assert.False(await repository.ResolveAsync(restored,expired,"confirm_void","duplicate", "acceptance-operator",claimId,resolved,audit,default));
        var entry=Assert.Single(await repository.ReadHistoryAsync(organizationId,order.Id,default));
        Assert.Equal("confirm_void",entry.Action);Assert.Equal("acceptance",entry.Reason);
        Assert.Equal("acceptance-operator",entry.ActorSubjectId);Assert.Equal(resolved.AuthorizationDecisionId,entry.AuthorizationDecisionId);
        Assert.Equal(3,entry.ConcurrencyVersion);
        Assert.Empty(await repository.ReadHistoryAsync(Guid.NewGuid(),order.Id,default));
        Assert.Empty(await repository.ReadHistoryAsync(organizationId,Guid.NewGuid(),default));
        await using NpgsqlConnection connection=await source.OpenConnectionAsync();
        await using(var history=new NpgsqlCommand("SELECT count(*) FROM order_payment_review_history WHERE order_id=$1 AND authorization_decision_id=$2 AND actor_subject_id='acceptance-operator'",connection))
        {history.Parameters.AddWithValue(order.Id);history.Parameters.AddWithValue(resolved.AuthorizationDecisionId);Assert.Equal(1L,Convert.ToInt64(await history.ExecuteScalarAsync()));}
        await using(var count=new NpgsqlCommand("SELECT count(*) FROM outbox_messages WHERE id=$1 AND event_type='order.payment-review-required.v1'",connection))
        {count.Parameters.AddWithValue(integrationEvent.EventId);Assert.Equal(1L,Convert.ToInt64(await count.ExecuteScalarAsync()));}
        await using var state=new NpgsqlCommand("SELECT status,concurrency_version FROM order_payment_reviews WHERE order_id=$1",connection);state.Parameters.AddWithValue(order.Id);
        await using var reader=await state.ExecuteReaderAsync();Assert.True(await reader.ReadAsync());Assert.Equal("resolved",reader.GetString(0));Assert.Equal(3,reader.GetInt64(1));
    }

    private static async Task SeedPendingOrderAsync(NpgsqlDataSource source)
    {
        await using NpgsqlConnection connection=await source.OpenConnectionAsync();
        await using var command=new NpgsqlCommand("INSERT INTO orders(id,restaurant_id,branch_id,order_number,currency,channel,service_type,subtotal_amount,total_amount,status,created_at_utc,created_by,updated_at_utc,updated_by) VALUES($1,$2,$3,$4,'USD','pos','takeaway',10,10,'payment_pending',now(),'acceptance',now(),'acceptance')",connection);
        command.Parameters.AddWithValue(Guid.NewGuid());command.Parameters.AddWithValue(Guid.NewGuid());command.Parameters.AddWithValue(Guid.NewGuid());command.Parameters.AddWithValue($"ACC-{Guid.NewGuid():N}"[..20]);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedReviewOrderAsync(NpgsqlDataSource source)
    {
        await using NpgsqlConnection connection=await source.OpenConnectionAsync();
        Guid orderId=Guid.NewGuid(),organizationId=Guid.NewGuid(),branchId=Guid.NewGuid(),intentId=Guid.NewGuid();
        await using var command=new NpgsqlCommand("INSERT INTO orders(id,organization_id,restaurant_id,branch_id,payment_intent_id,order_number,currency,channel,service_type,subtotal_amount,total_amount,status,created_at_utc,created_by,updated_at_utc,updated_by) VALUES($1,$2,$3,$4,$5,$6,'USD','pos','takeaway',10,10,'payment_review',now(),'acceptance',now(),'acceptance')",connection);
        command.Parameters.AddWithValue(orderId);command.Parameters.AddWithValue(organizationId);command.Parameters.AddWithValue(Guid.NewGuid());command.Parameters.AddWithValue(branchId);command.Parameters.AddWithValue(intentId);command.Parameters.AddWithValue($"REV-{Guid.NewGuid():N}"[..20]);
        await command.ExecuteNonQueryAsync();
        await using var review=new NpgsqlCommand("INSERT INTO order_payment_reviews(order_id,organization_id,branch_id,payment_intent_id,status,reason,concurrency_version,created_at_utc,updated_at_utc) VALUES($1,$2,$3,$4,'open','acceptance',1,now(),now())",connection);
        review.Parameters.AddWithValue(orderId);review.Parameters.AddWithValue(organizationId);review.Parameters.AddWithValue(branchId);review.Parameters.AddWithValue(intentId);await review.ExecuteNonQueryAsync();
    }

    private static async Task CreateDatabaseAsync(NpgsqlDataSource source,string name){await using NpgsqlConnection c=await source.OpenConnectionAsync();string quoted=new NpgsqlCommandBuilder().QuoteIdentifier(name);await new NpgsqlCommand($"CREATE DATABASE {quoted}",c).ExecuteNonQueryAsync();}
    private static async Task DropDatabaseAsync(NpgsqlDataSource source,string name){ValidateDatabaseName(name);await using NpgsqlConnection c=await source.OpenConnectionAsync();string quoted=new NpgsqlCommandBuilder().QuoteIdentifier(name);await new NpgsqlCommand($"DROP DATABASE IF EXISTS {quoted} WITH (FORCE)",c).ExecuteNonQueryAsync();}
    private static void ValidateDatabaseName(string name){if(!System.Text.RegularExpressions.Regex.IsMatch(name,"^nexaconnect_order_clean_it_[a-f0-9]{32}$"))throw new InvalidOperationException("Refusing to manage a database outside the Order acceptance boundary.");}
    private static string FindRepositoryRoot(){DirectoryInfo? d=new(AppContext.BaseDirectory);while(d is not null&&!File.Exists(Path.Combine(d.FullName,"NexaConnect.sln")))d=d.Parent;return d?.FullName??throw new DirectoryNotFoundException();}
}

[CollectionDefinition("Order migration runner acceptance",DisableParallelization=true)]
public sealed class OrderMigrationRunnerAcceptanceCollection;

public sealed class OrderMigrationAcceptanceFactAttribute : FactAttribute
{
    public OrderMigrationAcceptanceFactAttribute()
    {
        string? environment=Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT")??Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")??Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        string connection=Environment.GetEnvironmentVariable("NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB")??string.Empty;
        if(Environment.GetEnvironmentVariable("NEXACONNECT_ORDER_CLEAN_INSTALL_ACCEPTANCE")!="1"||environment is not ("Development" or "Test" or "Testing")||string.IsNullOrWhiteSpace(connection))
            Skip="Order clean-install acceptance requires its opt-in flag, administrator connection, and a safe environment.";
    }
}
