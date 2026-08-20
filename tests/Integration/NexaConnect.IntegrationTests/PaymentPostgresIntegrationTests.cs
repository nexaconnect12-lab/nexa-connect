extern alias PAYMENT;

using PaymentCreateIntent = PAYMENT::NexaConnect.Services.Payment.Application.Intents.CreatePaymentIntent;
using PaymentIdempotencyConflict = PAYMENT::NexaConnect.Services.Payment.Application.Intents.PaymentIdempotencyConflictException;
using PaymentMutationContext = PAYMENT::NexaConnect.Services.Payment.Application.Intents.PaymentMutationContext;
using PaymentRepository = PAYMENT::NexaConnect.Services.Payment.Infrastructure.PostgresPaymentIntents;
using ProviderAuthorizationOutcome = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.ProviderAuthorizationOutcome;
using ProviderCaptureOutcome = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.ProviderCaptureOutcome;
using Microsoft.Extensions.Options;
using NexaConnect.Infrastructure.Messaging;
using Npgsql;
using RabbitMQ.Client;

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

    public async Task InitializeAsync(){if(string.IsNullOrWhiteSpace(configuredConnectionString)||!IsSafeEnvironment())return;schema=$"payment_phase11_it_{Guid.NewGuid():N}";var builder=new NpgsqlConnectionStringBuilder(configuredConnectionString){SearchPath=schema};dataSource=NpgsqlDataSource.Create(builder.ConnectionString);await using NpgsqlConnection connection=await dataSource.OpenConnectionAsync();await new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"",connection).ExecuteNonQueryAsync();await new NpgsqlCommand(SchemaSql,connection).ExecuteNonQueryAsync();}
    public async Task DisposeAsync(){if(dataSource is null||schema is null)return;await using NpgsqlConnection connection=await dataSource.OpenConnectionAsync();await new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE",connection).ExecuteNonQueryAsync();await dataSource.DisposeAsync();}
    private static bool IsSafeEnvironment(){string? environment=Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT")??Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")??Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");return environment is "Development" or "Test" or "Testing";}
    private static async Task<long> ScalarAsync(NpgsqlConnection connection,string sql,params object[] values){await using var command=new NpgsqlCommand(sql,connection);for(int i=0;i<values.Length;i++)command.Parameters.AddWithValue(values[i]);return(long)(await command.ExecuteScalarAsync()??0L);}

    private const string SchemaSql="""
        CREATE TABLE payment_intents(id uuid PRIMARY KEY,organization_id uuid NOT NULL,restaurant_id uuid NOT NULL,branch_id uuid NOT NULL,order_id uuid NOT NULL,idempotency_key text NOT NULL,amount numeric(19,4) NOT NULL CHECK(amount>0),currency char(3) NOT NULL,payment_method text NOT NULL,status text NOT NULL CHECK(status IN ('pending','authorizing','unknown','requires_action','authorized','capturing','capture_unknown','captured','failed')),expires_at_utc timestamptz NULL,authorized_at_utc timestamptz NULL,captured_at_utc timestamptz NULL,failed_at_utc timestamptz NULL,created_at_utc timestamptz NOT NULL,updated_at_utc timestamptz NOT NULL,concurrency_version bigint NOT NULL DEFAULT 1,provider_authorization_id text NULL UNIQUE,failure_code text NULL,lease_owner text NULL,lease_expires_at_utc timestamptz NULL,authorization_attempt_count integer NOT NULL DEFAULT 0,last_reconciled_at_utc timestamptz NULL,provider_capture_id text NULL UNIQUE,CONSTRAINT uq_payment_intents_organization_restaurant_idempotency UNIQUE(organization_id,restaurant_id,idempotency_key));
        CREATE TABLE payment_audit_records(id uuid PRIMARY KEY,organization_id uuid NOT NULL,restaurant_id uuid NOT NULL,branch_id uuid NOT NULL,order_id uuid NOT NULL,payment_intent_id uuid NOT NULL REFERENCES payment_intents(id),action text NOT NULL,actor_subject_id text NOT NULL CHECK(char_length(btrim(actor_subject_id)) BETWEEN 1 AND 200 AND actor_subject_id !~ '[[:cntrl:]]'),occurred_at_utc timestamptz NOT NULL);
        CREATE FUNCTION prevent_payment_audit_mutation() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'payment_audit_records is append-only'; END; $$;
        CREATE TRIGGER tr_payment_audit_records_append_only BEFORE UPDATE OR DELETE ON payment_audit_records FOR EACH ROW EXECUTE FUNCTION prevent_payment_audit_mutation();
        CREATE TABLE outbox_messages(id uuid PRIMARY KEY,event_type text NOT NULL,contract_version integer NOT NULL,aggregate_type text NOT NULL,aggregate_id uuid NOT NULL,payload jsonb NOT NULL,correlation_id text NULL,causation_id text NULL,occurred_at_utc timestamptz NOT NULL,published_at_utc timestamptz NULL,retry_count integer NOT NULL DEFAULT 0,next_attempt_at_utc timestamptz NULL,last_error_category text NULL);
        """;
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

public sealed class PaymentRabbitMqFactAttribute : PaymentDatabaseFactAttribute
{
    public PaymentRabbitMqFactAttribute()
    {
        string? uri=Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_INTEGRATION_URI");
        if(Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_ACCEPTANCE")!="1"||!Uri.TryCreate(uri,UriKind.Absolute,out _))
            Skip="NEXACONNECT_RABBITMQ_ACCEPTANCE=1 and NEXACONNECT_RABBITMQ_INTEGRATION_URI are required.";
    }
}
