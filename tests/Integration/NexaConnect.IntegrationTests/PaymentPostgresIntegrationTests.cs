extern alias PAYMENT;

using PaymentCreateIntent = PAYMENT::NexaConnect.Services.Payment.Application.Intents.CreatePaymentIntent;
using PaymentIdempotencyConflict = PAYMENT::NexaConnect.Services.Payment.Application.Intents.PaymentIdempotencyConflictException;
using PaymentMutationContext = PAYMENT::NexaConnect.Services.Payment.Application.Intents.PaymentMutationContext;
using PaymentRepository = PAYMENT::NexaConnect.Services.Payment.Infrastructure.PostgresPaymentIntents;
using ProviderAuthorizationOutcome = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.ProviderAuthorizationOutcome;
using ProviderCaptureOutcome = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.ProviderCaptureOutcome;
using ProviderCaptureResult = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.ProviderCaptureResult;
using PaymentProvider = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.IPaymentProvider;
using PaymentCaptureRecoveryService = PAYMENT::NexaConnect.Services.Payment.Application.Intents.PaymentCaptureRecoveryService;
using PaymentIntent = PAYMENT::NexaConnect.Services.Payment.Application.Intents.PaymentIntent;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using NexaConnect.Infrastructure.Messaging;
using Npgsql;
using RabbitMQ.Client;
using System.Text.Json;

namespace NexaConnect.IntegrationTests;

public sealed class PaymentPostgresIntegrationTests : IAsyncLifetime
{
    private readonly string? configuredConnectionString=Environment.GetEnvironmentVariable("NEXACONNECT_PAYMENT_INTEGRATION_DB");
    private NpgsqlDataSource? dataSource;
    private string? schema;

    [PaymentDatabaseFact]
    public async Task Intent_audit_and_outbox_are_atomic_idempotent_and_tenant_scoped()
    {
        Guid organizationId=Guid.NewGuid(), restaurantId=Guid.NewGuid(), branchId=Guid.NewGuid(), orderId=Guid.NewGuid(), correlationId=Guid.NewGuid();
        var repository=new PaymentRepository(dataSource!);
        var command=new PaymentCreateIntent(restaurantId,branchId,orderId,"payment-1",12.50m,"usd","cash");
        var context=new PaymentMutationContext("payment-test",correlationId);

        var first=repository.Create(organizationId,command,context);
        var replay=repository.Create(organizationId,command,context);

        Assert.Equal(first.Id,replay.Id);
        Assert.Null(repository.Get(Guid.NewGuid(),first.Id));
        Assert.Throws<PaymentIdempotencyConflict>(()=>repository.Create(organizationId,command with{Amount=13m},context));
        await using NpgsqlConnection connection=await dataSource!.OpenConnectionAsync();
        Assert.Equal(1L,await ScalarAsync(connection,"SELECT count(*) FROM payment_intents WHERE organization_id=$1 AND id=$2",organizationId,first.Id));
        Assert.Equal(1L,await ScalarAsync(connection,"SELECT count(*) FROM payment_audit_records WHERE organization_id=$1 AND payment_intent_id=$2",organizationId,first.Id));
        Assert.Equal(2L,await ScalarAsync(connection,"SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1 AND correlation_id=$2",first.Id,correlationId.ToString("D")));
        await using var mutate=new NpgsqlCommand("UPDATE payment_audit_records SET action='tampered' WHERE payment_intent_id=$1",connection);mutate.Parameters.AddWithValue(first.Id);
        await Assert.ThrowsAsync<PostgresException>(()=>mutate.ExecuteNonQueryAsync());
    }

    [PaymentDatabaseFact]
    public async Task Concurrent_retries_return_one_intent_and_publish_once()
    {
        Guid organizationId=Guid.NewGuid();
        var command=new PaymentCreateIntent(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"concurrent-payment",20m,"USD","card");
        var repository=new PaymentRepository(dataSource!);
        Task<PAYMENT::NexaConnect.Services.Payment.Application.Intents.PaymentIntent>[] attempts=[
            Task.Run(()=>repository.Create(organizationId,command,new("payment-test",Guid.NewGuid()))),
            Task.Run(()=>repository.Create(organizationId,command,new("payment-test",Guid.NewGuid())))];

        var results=await Task.WhenAll(attempts);
        Assert.Equal(results[0].Id,results[1].Id);
        await using NpgsqlConnection connection=await dataSource!.OpenConnectionAsync();
        Assert.Equal(1L,await ScalarAsync(connection,"SELECT count(*) FROM payment_intents WHERE organization_id=$1",organizationId));
        Assert.Equal(1L,await ScalarAsync(connection,"SELECT count(*) FROM payment_audit_records WHERE organization_id=$1",organizationId));
        Assert.Equal(2L,await ScalarAsync(connection,"SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1",results[0].Id));
    }

    [PaymentDatabaseFact]
    public async Task Outbox_failure_rolls_back_intent_and_audit()
    {
        Guid organizationId=Guid.NewGuid(),orderId=Guid.NewGuid();
        await using(NpgsqlConnection connection=await dataSource!.OpenConnectionAsync())await new NpgsqlCommand("ALTER TABLE outbox_messages RENAME TO unavailable_outbox_messages",connection).ExecuteNonQueryAsync();
        try
        {
            var repository=new PaymentRepository(dataSource!);
            Assert.Throws<PostgresException>(()=>repository.Create(organizationId,new(Guid.NewGuid(),Guid.NewGuid(),orderId,"rollback",5m,"USD","cash"),new("payment-test",Guid.NewGuid())));
        }
        finally
        {
            await using NpgsqlConnection connection=await dataSource!.OpenConnectionAsync();await new NpgsqlCommand("ALTER TABLE unavailable_outbox_messages RENAME TO outbox_messages",connection).ExecuteNonQueryAsync();
        }
        await using NpgsqlConnection verify=await dataSource!.OpenConnectionAsync();
        Assert.Equal(0L,await ScalarAsync(verify,"SELECT count(*) FROM payment_intents WHERE organization_id=$1 AND order_id=$2",organizationId,orderId));
        Assert.Equal(0L,await ScalarAsync(verify,"SELECT count(*) FROM payment_audit_records WHERE organization_id=$1 AND order_id=$2",organizationId,orderId));
    }

    [PaymentDatabaseFact]
    public async Task Authorization_lease_and_result_are_concurrency_safe_and_transactional()
    {
        Guid organizationId=Guid.NewGuid(),correlationId=Guid.NewGuid();var repository=new PaymentRepository(dataSource!);
        var intent=repository.Create(organizationId,new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"authorization",30m,"USD","card"),new("order-service",correlationId));

        var first=repository.BeginAuthorization(organizationId,intent.Id,new("order-service",correlationId));
        var replay=repository.BeginAuthorization(organizationId,intent.Id,new("order-service",Guid.NewGuid()));
        Assert.True(first.Acquired);Assert.False(replay.Acquired);Assert.Equal("authorizing",replay.Intent.Status);
        var authorized=repository.CompleteAuthorization(organizationId,intent.Id,first.Intent.ConcurrencyVersion,true,"provider-auth-1",null,new("order-service",correlationId));
        Assert.Equal("authorized",authorized.Status);Assert.Equal("provider-auth-1",authorized.ProviderAuthorizationId);

        await using NpgsqlConnection connection=await dataSource!.OpenConnectionAsync();
        Assert.Equal(3L,await ScalarAsync(connection,"SELECT count(*) FROM payment_audit_records WHERE payment_intent_id=$1",intent.Id));
        Assert.Equal(6L,await ScalarAsync(connection,"SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1",intent.Id));
    }

    [PaymentDatabaseFact]
    public async Task Capture_is_authorized_only_concurrency_safe_and_transactional()
    {
        Guid organization=Guid.NewGuid(),correlation=Guid.NewGuid();var repository=new PaymentRepository(dataSource!);
        var intent=repository.Create(organization,new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"capture",40m,"USD","card"),new("order-service",correlation));
        var authorization=repository.BeginAuthorization(organization,intent.Id,new("order-service",correlation));
        var authorized=repository.CompleteAuthorization(organization,intent.Id,authorization.Intent.ConcurrencyVersion,ProviderAuthorizationOutcome.Authorized,"auth-capture",null,new("order-service",correlation));
        var capture=repository.BeginCapture(organization,intent.Id,new("order-service",correlation));var replay=repository.BeginCapture(organization,intent.Id,new("order-service",Guid.NewGuid()));
        Assert.True(capture.Acquired);Assert.False(replay.Acquired);Assert.Equal("capturing",replay.Intent.Status);
        var captured=repository.CompleteCapture(organization,intent.Id,capture.Intent.ConcurrencyVersion,ProviderCaptureOutcome.Captured,"capture-1",null,new("order-service",correlation));
        Assert.Equal("captured",captured.Status);Assert.Equal("capture-1",captured.ProviderCaptureId);
        await using NpgsqlConnection connection=await dataSource!.OpenConnectionAsync();
        Assert.Equal(5L,await ScalarAsync(connection,"SELECT count(*) FROM payment_audit_records WHERE payment_intent_id=$1",intent.Id));
        Assert.Equal(10L,await ScalarAsync(connection,"SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1",intent.Id));
    }

    [PaymentDatabaseFact]
    public async Task Unknown_capture_is_reclaimed_and_reconciled_atomically_after_worker_restart()
    {
        Guid organization=Guid.NewGuid(),correlation=Guid.NewGuid();
        var repository=new PaymentRepository(dataSource!,Options.Create(new PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.PaymentProviderOptions{LeaseDuration=TimeSpan.FromMilliseconds(1),MaximumCaptureRecoveryAttempts=3}));
        var intent=repository.Create(organization,new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"capture-recovery",45m,"USD","card"),new("order-service",correlation));
        var authorization=repository.BeginAuthorization(organization,intent.Id,new("order-service",correlation));
        repository.CompleteAuthorization(organization,intent.Id,authorization.Intent.ConcurrencyVersion,ProviderAuthorizationOutcome.Authorized,"auth-recovery",null,new("order-service",correlation));
        var capture=repository.BeginCapture(organization,intent.Id,new("order-service",correlation));
        var unknown=repository.CompleteCapture(organization,intent.Id,capture.Intent.ConcurrencyVersion,ProviderCaptureOutcome.Unknown,null,"provider_timeout",new("order-service",correlation));
        Assert.Equal("capture_unknown",unknown.Status);

        // A fresh repository represents a restarted worker. It recovers by status lookup identity,
        // never by issuing the capture command again.
        var restarted=new PaymentRepository(dataSource!,Options.Create(new PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.PaymentProviderOptions{LeaseDuration=TimeSpan.FromMinutes(1),MaximumCaptureRecoveryAttempts=3}));
        var claim=restarted.ClaimExpiredCapture(organization,intent.Id,new("payment-capture-recovery-worker",Guid.NewGuid()));
        Assert.True(claim.Acquired);
        var reconciled=restarted.ReconcileCapture(organization,intent.Id,claim.Intent.ConcurrencyVersion,ProviderCaptureOutcome.Captured,"capture-recovered",null,new("payment-capture-recovery-worker",correlation));
        Assert.Equal("captured",reconciled.Status);
        Assert.Equal(1,reconciled.CaptureAttemptCount);

        await using NpgsqlConnection connection=await dataSource!.OpenConnectionAsync();
        Assert.Equal(1L,await ScalarAsync(connection,"SELECT count(*) FROM payment_audit_records WHERE payment_intent_id=$1 AND action='payment.capture.reconciled'",intent.Id));
        Assert.Equal(1L,await ScalarAsync(connection,"SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1 AND event_type='payment.capture-reconciled.v1'",intent.Id));
        Assert.Equal(1L,await ScalarAsync(connection,"SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1 AND event_type='payment.audit.v1' AND payload->>'Action'='payment.capture.reconciled'",intent.Id));
    }

    [PaymentDatabaseFact]
    public async Task Provider_capture_response_survives_process_boundary_before_local_commit()
    {
        Guid organization=Guid.NewGuid(),correlation=Guid.NewGuid();
        var options=Options.Create(new PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.PaymentProviderOptions
            {LeaseDuration=TimeSpan.FromMilliseconds(1),MaximumCaptureRecoveryAttempts=3});
        var repository=new PaymentRepository(dataSource!,options);
        var intent=repository.Create(organization,new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"capture-crash-boundary",50m,"USD","card"),new("order-service",correlation));
        var authorization=repository.BeginAuthorization(organization,intent.Id,new("order-service",correlation));
        repository.CompleteAuthorization(organization,intent.Id,authorization.Intent.ConcurrencyVersion,ProviderAuthorizationOutcome.Authorized,"auth-crash-boundary",null,new("order-service",correlation));
        var capture=repository.BeginCapture(organization,intent.Id,new("order-service",correlation));
        var provider=new CapturedBeforeCommitProvider();

        ProviderCaptureResult providerResponse=await provider.CaptureAsync(capture.Intent,default);
        Assert.Equal(ProviderCaptureOutcome.Captured,providerResponse.Outcome);
        // Simulate abrupt process loss: deliberately discard the response without calling CompleteCapture.
        await Task.Delay(10);

        var restarted=new PaymentRepository(dataSource!,options);
        var claim=restarted.ClaimExpiredCapture(organization,intent.Id,new("payment-capture-recovery-worker",Guid.NewGuid()));
        Assert.True(claim.Acquired);
        var recovery=new PaymentCaptureRecoveryService(restarted,provider);
        PaymentIntent reconciled=await recovery.ReconcileAsync(organization,intent.Id,new("payment-capture-recovery-worker",correlation),default);

        Assert.Equal("captured",reconciled.Status);
        Assert.Equal("capture-before-crash",reconciled.ProviderCaptureId);
        Assert.Equal(1,provider.CaptureCalls);
        Assert.Equal(1,provider.StatusCalls);
        await using NpgsqlConnection connection=await dataSource!.OpenConnectionAsync();
        Assert.Equal(1L,await ScalarAsync(connection,"SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1 AND event_type='payment.capture-reconciled.v1'",intent.Id));
    }

    [PaymentProcessKillArmFact]
    public async Task Arm_real_http_capture_boundary_for_external_process_kill()
    {
        string markerPath=Environment.GetEnvironmentVariable("NEXACONNECT_PAYMENT_PROCESS_KILL_MARKER")!;
        Guid organization=Guid.NewGuid(),correlation=Guid.NewGuid();
        var options=Options.Create(new PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.PaymentProviderOptions
            {LeaseDuration=TimeSpan.FromMilliseconds(1),MaximumCaptureRecoveryAttempts=3,CapturePath="v1/captures"});
        var repository=new PaymentRepository(dataSource!,options);
        var intent=repository.Create(organization,new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"capture-process-kill",51m,"USD","card"),new("order-service",correlation));
        var authorization=repository.BeginAuthorization(organization,intent.Id,new("order-service",correlation));
        repository.CompleteAuthorization(organization,intent.Id,authorization.Intent.ConcurrencyVersion,ProviderAuthorizationOutcome.Authorized,"auth-process-kill",null,new("order-service",correlation));
        var capture=repository.BeginCapture(organization,intent.Id,new("order-service",correlation));

        await using WebApplication provider=await StartProviderAsync(intent.Id);
        using var client=new HttpClient{BaseAddress=new Uri(provider.Urls.Single())};
        var httpProvider=new PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.HttpPaymentProvider(client,options);
        ProviderCaptureResult result=await httpProvider.CaptureAsync(capture.Intent,default);
        Assert.Equal(ProviderCaptureOutcome.Captured,result.Outcome);

        string temporaryPath=markerPath+".tmp";
        await File.WriteAllTextAsync(temporaryPath,JsonSerializer.Serialize(new ProcessKillMarker(schema!,organization,intent.Id,correlation)));
        File.Move(temporaryPath,markerPath,overwrite:true);
        await Task.Delay(Timeout.InfiniteTimeSpan);
    }

    [PaymentProcessKillRecoveryFact]
    public async Task Recover_capture_after_external_process_kill()
    {
        string markerPath=Environment.GetEnvironmentVariable("NEXACONNECT_PAYMENT_PROCESS_KILL_MARKER")!;
        ProcessKillMarker marker=JsonSerializer.Deserialize<ProcessKillMarker>(await File.ReadAllTextAsync(markerPath))
            ?? throw new InvalidOperationException("The process-kill marker is invalid.");
        var builder=new NpgsqlConnectionStringBuilder(configuredConnectionString){SearchPath=marker.Schema};
        await using NpgsqlDataSource killedProcessDataSource=NpgsqlDataSource.Create(builder.ConnectionString);
        try
        {
            var options=Options.Create(new PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.PaymentProviderOptions
                {LeaseDuration=TimeSpan.FromMilliseconds(1),MaximumCaptureRecoveryAttempts=3,CaptureStatusPath="v1/captures"});
            var repository=new PaymentRepository(killedProcessDataSource,options);
            var claim=repository.ClaimExpiredCapture(marker.OrganizationId,marker.IntentId,new("payment-capture-recovery-worker",Guid.NewGuid()));
            Assert.True(claim.Acquired);
            await using WebApplication provider=await StartProviderAsync(marker.IntentId);
            using var client=new HttpClient{BaseAddress=new Uri(provider.Urls.Single())};
            var httpProvider=new PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.HttpPaymentProvider(client,options);
            var recovery=new PaymentCaptureRecoveryService(repository,httpProvider);
            PaymentIntent reconciled=await recovery.ReconcileAsync(marker.OrganizationId,marker.IntentId,new("payment-capture-recovery-worker",marker.CorrelationId),default);
            Assert.Equal("captured",reconciled.Status);
            Assert.Equal("capture-process-kill",reconciled.ProviderCaptureId);
        }
        finally
        {
            await using NpgsqlConnection cleanup=await killedProcessDataSource.OpenConnectionAsync();
            await new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{marker.Schema}\" CASCADE",cleanup).ExecuteNonQueryAsync();
            File.Delete(markerPath);
        }
    }

    [PaymentRabbitMqFact]
    public async Task Broker_outage_retains_rows_and_recovery_publishes_with_confirmations()
    {
        string rabbitMqUri=Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_INTEGRATION_URI")!;
        await Assert.ThrowsAnyAsync<Exception>(async()=>{var unavailable=new ConnectionFactory{Uri=new Uri("amqp://guest:guest@127.0.0.1:1"),RequestedConnectionTimeout=TimeSpan.FromSeconds(1)};await using IConnection ignored=await unavailable.CreateConnectionAsync();});
        Guid organizationId=Guid.NewGuid(),correlationId=Guid.NewGuid();
        var repository=new PaymentRepository(dataSource!);
        var intent=repository.Create(organizationId,new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"recovery",18m,"USD","wallet"),new("payment-recovery-test",correlationId));
        await using(NpgsqlConnection verify=await dataSource!.OpenConnectionAsync())Assert.Equal(2L,await ScalarAsync(verify,"SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1 AND published_at_utc IS NULL",intent.Id));

        string exchange=$"nexaconnect.payment.phase11.{Guid.NewGuid():N}",queue=$"nexaconnect.payment.phase11.{Guid.NewGuid():N}";
        var factory=new ConnectionFactory{Uri=new Uri(rabbitMqUri)};
        await using IConnection connection=await factory.CreateConnectionAsync();await using IChannel channel=await connection.CreateChannelAsync();
        try
        {
            await channel.ExchangeDeclareAsync(exchange,ExchangeType.Topic,durable:true,autoDelete:false);await channel.QueueDeclareAsync(queue,durable:false,exclusive:true,autoDelete:true);await channel.QueueBindAsync(queue,exchange,"payment.#");
            var transport=new RabbitMqOutboxTransport(connection,Options.Create(new OutboxOptions{Exchange=exchange}));var store=new PostgresOutboxStore(dataSource!);
            OutboxMessage[] pending=(await store.ClaimBatchAsync(10,CancellationToken.None)).Where(message=>message.AggregateId==intent.Id).ToArray();Assert.Equal(2,pending.Length);
            foreach(OutboxMessage message in pending){await transport.PublishAsync(message,CancellationToken.None);await store.MarkPublishedAsync(message.Id,CancellationToken.None);}
            var deliveries=new List<BasicGetResult>();for(int attempt=0;attempt<20&&deliveries.Count<2;attempt++){BasicGetResult? delivery=await channel.BasicGetAsync(queue,autoAck:true);if(delivery is not null)deliveries.Add(delivery);else await Task.Delay(100);}
            Assert.Equal(["payment.audit.v1","payment.intent-created.v1"],deliveries.Select(item=>item.RoutingKey).Order().ToArray());
            Assert.All(deliveries,delivery=>Assert.True(delivery.BasicProperties.Persistent));Assert.All(deliveries,delivery=>Assert.Equal(delivery.RoutingKey,delivery.BasicProperties.Type));Assert.All(deliveries,delivery=>Assert.Equal("application/json",delivery.BasicProperties.ContentType));
            Assert.All(deliveries,delivery=>{string payload=System.Text.Encoding.UTF8.GetString(delivery.Body.Span);Assert.Contains(correlationId.ToString("D"),payload,StringComparison.OrdinalIgnoreCase);Assert.Contains(organizationId.ToString("D"),payload,StringComparison.OrdinalIgnoreCase);});
            await using NpgsqlConnection published=await dataSource!.OpenConnectionAsync();Assert.Equal(2L,await ScalarAsync(published,"SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1 AND published_at_utc IS NOT NULL",intent.Id));
        }
        finally{await channel.ExchangeDeleteAsync(exchange,ifUnused:false);}
    }

    [PaymentRabbitMqFact]
    public async Task Established_publisher_connection_is_replaced_after_disconnect()
    {
        string rabbitMqUri=Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_INTEGRATION_URI")!;
        string exchange=$"nexaconnect.payment.phase12.{Guid.NewGuid():N}",queue=$"nexaconnect.payment.phase12.{Guid.NewGuid():N}";
        var factory=new ConnectionFactory{Uri=new Uri(rabbitMqUri),AutomaticRecoveryEnabled=true,TopologyRecoveryEnabled=true};
        await using IConnection control=await factory.CreateConnectionAsync();
        await using IChannel channel=await control.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(exchange,ExchangeType.Topic,durable:true,autoDelete:false);
        await channel.QueueDeclareAsync(queue,durable:false,exclusive:true,autoDelete:true);
        await channel.QueueBindAsync(queue,exchange,"payment.#");
        IConnection? established=null;int connectionCount=0;
        async Task<IConnection> CreateConnection(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref connectionCount);
            IConnection created=await factory.CreateConnectionAsync(cancellationToken);
            established=created;
            return created;
        }
        await using var transport=new RabbitMqOutboxTransport(
            Options.Create(new OutboxOptions{Exchange=exchange,ConnectionString=rabbitMqUri}),CreateConnection);
        try
        {
            var first=new OutboxMessage(Guid.NewGuid(),"payment.capture-reconciled.v1",1,"payment-intent",Guid.NewGuid(),"{}",Guid.NewGuid().ToString("D"),DateTimeOffset.UtcNow);
            await transport.PublishAsync(first,default);
            Assert.NotNull(established);
            await established!.CloseAsync();
            var second=first with{Id=Guid.NewGuid(),AggregateId=Guid.NewGuid()};
            await transport.PublishAsync(second,default);

            var deliveries=new List<BasicGetResult>();
            for(int attempt=0;attempt<20&&deliveries.Count<2;attempt++)
            {
                BasicGetResult? delivery=await channel.BasicGetAsync(queue,autoAck:true);
                if(delivery is null)await Task.Delay(100);else deliveries.Add(delivery);
            }
            Assert.Equal(2,deliveries.Count);
            Assert.Equal(2,connectionCount);
            Assert.All(deliveries,item=>Assert.True(item.BasicProperties.Persistent));
        }
        finally
        {
            await channel.ExchangeDeleteAsync(exchange,ifUnused:false);
        }
    }

    [PaymentBrokerRestartFact]
    public async Task Established_publisher_recovers_after_full_broker_container_restart()
    {
        string rabbitMqUri=Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_INTEGRATION_URI")!;
        string readyPath=Environment.GetEnvironmentVariable("NEXACONNECT_BROKER_RESTART_READY_MARKER")!;
        string continuePath=Environment.GetEnvironmentVariable("NEXACONNECT_BROKER_RESTART_CONTINUE_MARKER")!;
        string exchange=$"nexaconnect.payment.phase12.restart.{Guid.NewGuid():N}",queue=$"nexaconnect.payment.phase12.restart.{Guid.NewGuid():N}";
        var factory=new ConnectionFactory{Uri=new Uri(rabbitMqUri),AutomaticRecoveryEnabled=true,TopologyRecoveryEnabled=true};
        await using var transport=new RabbitMqOutboxTransport(Options.Create(new OutboxOptions{Exchange=exchange,ConnectionString=rabbitMqUri}));
        var first=new OutboxMessage(Guid.NewGuid(),"payment.capture-reconciled.v1",1,"payment-intent",Guid.NewGuid(),"{}",Guid.NewGuid().ToString("D"),DateTimeOffset.UtcNow);
        try
        {
            await using(IConnection setupConnection=await factory.CreateConnectionAsync())
            await using(IChannel setupChannel=await setupConnection.CreateChannelAsync())
            {
                await setupChannel.ExchangeDeclareAsync(exchange,ExchangeType.Topic,durable:true,autoDelete:false);
                await setupChannel.QueueDeclareAsync(queue,durable:true,exclusive:false,autoDelete:false);
                await setupChannel.QueueBindAsync(queue,exchange,"payment.#");
            }
            await transport.PublishAsync(first,default);
            await File.WriteAllTextAsync(readyPath,"ready");
            await WaitForFileAsync(continuePath,TimeSpan.FromSeconds(60));

            var second=first with{Id=Guid.NewGuid(),AggregateId=Guid.NewGuid()};
            Exception? lastFailure=null;
            for(int attempt=0;attempt<30;attempt++)
            {
                try{await transport.PublishAsync(second,default);lastFailure=null;break;}
                catch(Exception exception){lastFailure=exception;await Task.Delay(1000);}
            }
            Assert.Null(lastFailure);

            await using IConnection verifyConnection=await factory.CreateConnectionAsync();
            await using IChannel verifyChannel=await verifyConnection.CreateChannelAsync();
            var deliveries=new List<BasicGetResult>();
            for(int attempt=0;attempt<30&&deliveries.Count<2;attempt++)
            {
                BasicGetResult? delivery=await verifyChannel.BasicGetAsync(queue,autoAck:true);
                if(delivery is null)await Task.Delay(250);else deliveries.Add(delivery);
            }
            Assert.Equal(2,deliveries.Count);
            Assert.All(deliveries,item=>Assert.True(item.BasicProperties.Persistent));
        }
        finally
        {
            try
            {
                await using IConnection cleanupConnection=await factory.CreateConnectionAsync();
                await using IChannel cleanupChannel=await cleanupConnection.CreateChannelAsync();
                await cleanupChannel.QueueDeleteAsync(queue,ifUnused:false,ifEmpty:false);
                await cleanupChannel.ExchangeDeleteAsync(exchange,ifUnused:false);
            }
            catch
            {
                // The script retains failed-run diagnostics. Operators can remove the uniquely prefixed
                // durable resources after restoring the broker when immediate cleanup is impossible.
            }
            File.Delete(readyPath);
            File.Delete(continuePath);
        }
    }

    public async Task InitializeAsync(){if(string.IsNullOrWhiteSpace(configuredConnectionString)||!IsSafeEnvironment())return;schema=$"payment_phase11_it_{Guid.NewGuid():N}";var builder=new NpgsqlConnectionStringBuilder(configuredConnectionString){SearchPath=schema};dataSource=NpgsqlDataSource.Create(builder.ConnectionString);await using NpgsqlConnection connection=await dataSource.OpenConnectionAsync();await new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"",connection).ExecuteNonQueryAsync();await new NpgsqlCommand(SchemaSql,connection).ExecuteNonQueryAsync();}
    public async Task DisposeAsync(){if(dataSource is null||schema is null)return;await using NpgsqlConnection connection=await dataSource.OpenConnectionAsync();await new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE",connection).ExecuteNonQueryAsync();await dataSource.DisposeAsync();}
    private static bool IsSafeEnvironment(){string? environment=Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT")??Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")??Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");return environment is "Development" or "Test" or "Testing";}
    private static async Task<long> ScalarAsync(NpgsqlConnection connection,string sql,params object[] values){await using var command=new NpgsqlCommand(sql,connection);for(int i=0;i<values.Length;i++)command.Parameters.AddWithValue(values[i]);return(long)(await command.ExecuteScalarAsync()??0L);}

    private static async Task<WebApplication> StartProviderAsync(Guid intentId)
    {
        WebApplicationBuilder builder=WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        WebApplication app=builder.Build();
        app.MapPost("/v1/captures",()=>Results.Json(new{Succeeded=true,ProviderTransactionId="capture-process-kill",FailureReason=(string?)null}));
        app.MapGet("/v1/captures/{paymentIntentId:guid}",(Guid paymentIntentId)=>paymentIntentId==intentId
            ? Results.Json(new{Status="captured",ProviderTransactionId="capture-process-kill",FailureReason=(string?)null})
            : Results.NotFound());
        await app.StartAsync();
        return app;
    }

    private static async Task WaitForFileAsync(string path,TimeSpan timeout)
    {
        DateTimeOffset deadline=DateTimeOffset.UtcNow+timeout;
        while(DateTimeOffset.UtcNow<deadline){if(File.Exists(path))return;await Task.Delay(250);}
        throw new TimeoutException($"Timed out waiting for fault-rehearsal marker '{Path.GetFileName(path)}'.");
    }

    private sealed record ProcessKillMarker(string Schema,Guid OrganizationId,Guid IntentId,Guid CorrelationId);

    private const string SchemaSql="""
        CREATE TABLE payment_intents(id uuid PRIMARY KEY,organization_id uuid NOT NULL,restaurant_id uuid NOT NULL,branch_id uuid NOT NULL,order_id uuid NOT NULL,idempotency_key text NOT NULL,amount numeric(19,4) NOT NULL CHECK(amount>0),currency char(3) NOT NULL,payment_method text NOT NULL,status text NOT NULL CHECK(status IN ('pending','authorizing','unknown','requires_action','authorized','capturing','capture_unknown','captured','failed')),expires_at_utc timestamptz NULL,authorized_at_utc timestamptz NULL,captured_at_utc timestamptz NULL,failed_at_utc timestamptz NULL,created_at_utc timestamptz NOT NULL,updated_at_utc timestamptz NOT NULL,concurrency_version bigint NOT NULL DEFAULT 1,provider_authorization_id text NULL UNIQUE,failure_code text NULL,lease_owner text NULL,lease_expires_at_utc timestamptz NULL,authorization_attempt_count integer NOT NULL DEFAULT 0,last_reconciled_at_utc timestamptz NULL,provider_capture_id text NULL UNIQUE,capture_lease_owner text NULL,capture_lease_expires_at_utc timestamptz NULL,capture_attempt_count integer NOT NULL DEFAULT 0,capture_last_reconciled_at_utc timestamptz NULL,CONSTRAINT uq_payment_intents_organization_restaurant_idempotency UNIQUE(organization_id,restaurant_id,idempotency_key));
        CREATE TABLE payment_audit_records(id uuid PRIMARY KEY,organization_id uuid NOT NULL,restaurant_id uuid NOT NULL,branch_id uuid NOT NULL,order_id uuid NOT NULL,payment_intent_id uuid NOT NULL REFERENCES payment_intents(id),action text NOT NULL,actor_subject_id text NOT NULL CHECK(char_length(btrim(actor_subject_id)) BETWEEN 1 AND 200 AND actor_subject_id !~ '[[:cntrl:]]'),occurred_at_utc timestamptz NOT NULL);
        CREATE FUNCTION prevent_payment_audit_mutation() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'payment_audit_records is append-only'; END; $$;
        CREATE TRIGGER tr_payment_audit_records_append_only BEFORE UPDATE OR DELETE ON payment_audit_records FOR EACH ROW EXECUTE FUNCTION prevent_payment_audit_mutation();
        CREATE TABLE outbox_messages(id uuid PRIMARY KEY,event_type text NOT NULL,contract_version integer NOT NULL,aggregate_type text NOT NULL,aggregate_id uuid NOT NULL,payload jsonb NOT NULL,correlation_id text NULL,causation_id text NULL,occurred_at_utc timestamptz NOT NULL,published_at_utc timestamptz NULL,retry_count integer NOT NULL DEFAULT 0,next_attempt_at_utc timestamptz NULL,last_error_category text NULL);
        """;

    private sealed class CapturedBeforeCommitProvider : PaymentProvider
    {
        public int CaptureCalls{get;private set;}
        public int StatusCalls{get;private set;}
        public Task<PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.ProviderAuthorizationResult> AuthorizeAsync(PaymentIntent intent,CancellationToken cancellationToken)=>throw new NotSupportedException();
        public Task<ProviderCaptureResult> CaptureAsync(PaymentIntent intent,CancellationToken cancellationToken){CaptureCalls++;return Task.FromResult(new ProviderCaptureResult(ProviderCaptureOutcome.Captured,"capture-before-crash",null));}
        public Task<ProviderCaptureResult> GetCaptureStatusAsync(PaymentIntent intent,CancellationToken cancellationToken){StatusCalls++;return Task.FromResult(new ProviderCaptureResult(ProviderCaptureOutcome.Captured,"capture-before-crash",null));}
    }
}

public class PaymentDatabaseFactAttribute : FactAttribute
{
    public PaymentDatabaseFactAttribute()
    {
        string? environment=Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT")??Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")??Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NEXACONNECT_PAYMENT_INTEGRATION_DB"))||environment is not ("Development" or "Test" or "Testing"))
            Skip="NEXACONNECT_PAYMENT_INTEGRATION_DB and a Development/Test/Testing environment are required.";
    }
}

public class PaymentRabbitMqFactAttribute : PaymentDatabaseFactAttribute
{
    public PaymentRabbitMqFactAttribute()
    {
        string? uri=Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_INTEGRATION_URI");
        if(Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_ACCEPTANCE")!="1"||!Uri.TryCreate(uri,UriKind.Absolute,out _))
            Skip="NEXACONNECT_RABBITMQ_ACCEPTANCE=1 and NEXACONNECT_RABBITMQ_INTEGRATION_URI are required.";
    }
}

public sealed class PaymentProcessKillArmFactAttribute : PaymentDatabaseFactAttribute
{
    public PaymentProcessKillArmFactAttribute()
    {
        if(Environment.GetEnvironmentVariable("NEXACONNECT_PAYMENT_PROCESS_KILL_ACCEPTANCE")!="1"||
           Environment.GetEnvironmentVariable("NEXACONNECT_PAYMENT_PROCESS_KILL_STAGE")!="arm"||
           string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NEXACONNECT_PAYMENT_PROCESS_KILL_MARKER")))
            Skip="The explicitly gated Payment process-kill arm stage is required.";
    }
}

public sealed class PaymentProcessKillRecoveryFactAttribute : PaymentDatabaseFactAttribute
{
    public PaymentProcessKillRecoveryFactAttribute()
    {
        string? marker=Environment.GetEnvironmentVariable("NEXACONNECT_PAYMENT_PROCESS_KILL_MARKER");
        if(Environment.GetEnvironmentVariable("NEXACONNECT_PAYMENT_PROCESS_KILL_ACCEPTANCE")!="1"||
           Environment.GetEnvironmentVariable("NEXACONNECT_PAYMENT_PROCESS_KILL_STAGE")!="recover"||
           string.IsNullOrWhiteSpace(marker)||!File.Exists(marker))
            Skip="The explicitly gated Payment process-kill recovery stage and marker are required.";
    }
}

public sealed class PaymentBrokerRestartFactAttribute : PaymentRabbitMqFactAttribute
{
    public PaymentBrokerRestartFactAttribute()
    {
        if(Environment.GetEnvironmentVariable("NEXACONNECT_BROKER_RESTART_ACCEPTANCE")!="1"||
           string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NEXACONNECT_BROKER_RESTART_READY_MARKER"))||
           string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NEXACONNECT_BROKER_RESTART_CONTINUE_MARKER")))
            Skip="The explicitly gated broker-container restart stage and marker paths are required.";
    }
}
