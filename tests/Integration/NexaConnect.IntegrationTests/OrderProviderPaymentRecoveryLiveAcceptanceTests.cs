extern alias MIGRATIONS;
extern alias ORDER;
extern alias PAYMENT;

using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NexaConnect.Contracts.IntegrationEvents;
using Npgsql;
using RabbitMQ.Client;
using MigrationApplication = MIGRATIONS::MigrationApplication;
using OrderAggregate = ORDER::NexaConnect.Services.Order.Domain.OrderAggregate;
using OrderLine = ORDER::NexaConnect.Services.Order.Domain.OrderLine;
using PostgresOrderRepository = ORDER::NexaConnect.Services.Order.Infrastructure.Persistence.PostgresOrderRepository;
using PlaceOrderRequest = ORDER::NexaConnect.Services.Order.Controllers.PlaceOrderRequest;
using PlaceOrderRequestLine = ORDER::NexaConnect.Services.Order.Controllers.PlaceOrderRequestLine;
using PaymentIntent = PAYMENT::NexaConnect.Services.Payment.Application.Intents.PaymentIntent;
using PaymentMutationContext = PAYMENT::NexaConnect.Services.Payment.Application.Intents.PaymentMutationContext;
using CreatePaymentIntent = PAYMENT::NexaConnect.Services.Payment.Application.Intents.CreatePaymentIntent;
using PaymentAuthorizationService = PAYMENT::NexaConnect.Services.Payment.Application.Intents.PaymentAuthorizationService;
using PostgresPaymentIntents = PAYMENT::NexaConnect.Services.Payment.Infrastructure.PostgresPaymentIntents;
using IPaymentProvider = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.IPaymentProvider;
using HttpPaymentProvider = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.HttpPaymentProvider;
using OmisePaymentProvider = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.OmisePaymentProvider;
using PaymentProviderOptions = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.PaymentProviderOptions;
using ProviderAuthorizationResult = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.ProviderAuthorizationResult;
using ProviderAuthorizationStatus = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.ProviderAuthorizationStatus;
using ProviderCaptureResult = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.ProviderCaptureResult;
using ProviderVoidResult = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.ProviderVoidResult;

namespace NexaConnect.IntegrationTests;

[CollectionDefinition("Order provider payment recovery live acceptance", DisableParallelization = true)]
public sealed class OrderProviderPaymentRecoveryLiveAcceptanceCollection;

[Collection("Order provider payment recovery live acceptance")]
public sealed class OrderProviderPaymentRecoveryLiveAcceptanceTests
{
    private const string PaymentEvidenceQueue = "nexaconnect.acceptance.provider-recovery.payment-evidence";
    private static string FixtureOrderKey(string scenario) => $"provider-recovery-{scenario}";
    private static bool IsOmise() => Environment.GetEnvironmentVariable("NEXACONNECT_ORDER_PROVIDER_RECOVERY_ADAPTER") == "Omise";
    private static string[] Scenarios() => IsOmise()
        && Environment.GetEnvironmentVariable("NEXACONNECT_ORDER_PROVIDER_RECOVERY_CARD_HANDOFF") != "1"
        ? ["authorization_response", "capture_response", "void_response", "void_paid_protection"]
        : ["intent_created", "authorization_response", "capture_response", "void_response", "void_paid_protection"];
    private static string? CardToken(string scenario) => IsOmise()
        ? Required("NEXACONNECT_OMISE_" + scenario.ToUpperInvariant() + "_TEST_TOKEN") : null;
    private static void RemoveCardCredentials(ProcessStartInfo start)
    {
        foreach (string name in start.Environment.Keys.Where(name => name.StartsWith("NEXACONNECT_OMISE_", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(name);
        start.Environment.Remove("PaymentProvider__OmiseSecretKey");
        foreach (string name in start.Environment.Keys.Where(name => name.StartsWith("OmiseWebhooks", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(name);
        start.Environment["OmiseWebhooks__Enabled"] = "false";
        // Payment receives its secret through its explicit service configuration, below.
    }
    [OrderProviderRecoveryLiveFact("initialize")]
    public async Task Initialize_provider_payment_process_interruption_acceptance()
    {
        string scriptsRoot = Path.Combine(FindRepositoryRoot(), "src", "Tools", "NexaConnect.DataMigration", "Scripts");
        await RunMigrationAsync("Order", OrderConnection(), "NEXACONNECT_ORDER_DB", scriptsRoot);
        await RunMigrationAsync("Payment", PaymentConnection(), "NEXACONNECT_PAYMENT_DB", scriptsRoot);
        await using NpgsqlDataSource payment = NpgsqlDataSource.Create(PaymentConnection());
        await using var command = payment.CreateCommand("""
            CREATE TABLE provider_recovery_scenarios(
              scenario text PRIMARY KEY,
              organization_id uuid NOT NULL,
              restaurant_id uuid NOT NULL,
              branch_id uuid NOT NULL,
              order_id uuid NOT NULL UNIQUE,
              payment_intent_id uuid NOT NULL UNIQUE,
              correlation_id uuid NOT NULL);
            CREATE TABLE provider_recovery_calls(
              scenario text NOT NULL,
              operation text NOT NULL,
              call_count integer NOT NULL CHECK(call_count > 0),
              PRIMARY KEY(scenario,operation));
            """);
        await command.ExecuteNonQueryAsync();
        var factory = new ConnectionFactory { Uri = new Uri(RabbitConnection()) };
        await using IConnection connection = await factory.CreateConnectionAsync();
        await using IChannel channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync("nexaconnect.events", ExchangeType.Topic, durable: true);
        await channel.QueueDeclareAsync(PaymentEvidenceQueue, durable: true, exclusive: false, autoDelete: false);
        await channel.QueueBindAsync(PaymentEvidenceQueue, "nexaconnect.events", "payment.#");
    }

    [OrderProviderRecoveryLiveFact("arm")]
    public async Task Arm_provider_payment_process_interruption()
    {
        string scenario = Scenario();
        Assert.Contains(scenario, Scenarios());
        await using NpgsqlDataSource orderSource = NpgsqlDataSource.Create(OrderConnection());
        await using NpgsqlDataSource paymentSource = NpgsqlDataSource.Create(PaymentConnection());
        var orderRepository = new PostgresOrderRepository(orderSource);
        var paymentRepository = NewPaymentRepository(paymentSource);
        ScenarioState state = await SeedAsync(scenario, orderRepository, orderSource, paymentRepository, paymentSource);

        if (scenario == "authorization_response")
        {
            using var provider = NewProvider(paymentSource, scenario);
            var lease = paymentRepository.BeginAuthorization(state.OrganizationId, state.PaymentIntentId,
                new PaymentMutationContext("provider-recovery-arm", state.CorrelationId));
            Assert.True(lease.Acquired);
            ProviderAuthorizationResult result = await provider.AuthorizeAsync(lease.Intent, CardToken(scenario), default);
            Assert.True(result.Succeeded, "Authorization was not confirmed; inspect test-account charges before a new run.");
        }
        else if (scenario == "capture_response" || scenario.StartsWith("void_", StringComparison.Ordinal))
        {
            using var provider = NewProvider(paymentSource, scenario);
            var authorization = new PaymentAuthorizationService(paymentRepository, provider);
            PaymentIntent authorized = Assert.IsType<PaymentIntent>(await authorization.AuthorizeAsync(
                state.OrganizationId, state.PaymentIntentId,
                new PaymentMutationContext("provider-recovery-arm", state.CorrelationId), CardToken(scenario), default));
            Assert.Equal("authorized", authorized.Status);
            if (scenario.StartsWith("void_", StringComparison.Ordinal))
            {
                var voidLease = paymentRepository.BeginVoid(state.OrganizationId, state.PaymentIntentId,
                    new PaymentMutationContext("provider-recovery-arm", state.CorrelationId));
                Assert.True(voidLease.Acquired);
                ProviderVoidResult voidResult = await provider.VoidAsync(voidLease.Intent, default);
                Assert.Equal(PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.ProviderVoidOutcome.Voided, voidResult.Outcome);
            }
            else
            {
                var lease = paymentRepository.BeginCapture(state.OrganizationId, state.PaymentIntentId,
                    new PaymentMutationContext("provider-recovery-arm", state.CorrelationId));
                Assert.True(lease.Acquired);
                ProviderCaptureResult result = await provider.CaptureAsync(lease.Intent, default);
                Assert.Equal(PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.ProviderCaptureOutcome.Captured,
                    result.Outcome);
            }
        }

        await WriteMarkerAsync(MarkerPath(), scenario);
        await Task.Delay(Timeout.InfiniteTimeSpan);
    }

    [OrderProviderRecoveryLiveFact("hosted_recover")]
    public async Task Recover_provider_payment_through_hosted_workers_and_outbox()
    {
        string scenario = Scenario();
        await using NpgsqlDataSource orderSource = NpgsqlDataSource.Create(OrderConnection());
        await using NpgsqlDataSource paymentSource = NpgsqlDataSource.Create(PaymentConnection());
        ScenarioState state = await ReadScenarioAsync(paymentSource, scenario);
        await using var fixture = await HostedDependencyFixture.StartAsync();
        int paymentPort = ReserveLoopbackPort();
        Uri paymentBaseAddress = new($"http://127.0.0.1:{paymentPort}/");
        using Process order = await StartOrderAsync(fixture.BaseAddress, paymentBaseAddress);
        Process? payment = null;
        try
        {
            await WritePhaseMarkerAsync("order_ready", scenario, order.Id, null);
            await WaitForControlPhaseAsync("broker_stopped", TimeSpan.FromSeconds(45));
            payment = await StartPaymentAsync(fixture.BaseAddress, paymentPort);
            if (IsOmise() && scenario == "intent_created")
                await SubmitRecoveredCardTokenAsync(fixture.BaseAddress, orderSource, paymentSource, state, order);
            await WaitForHostedRecoveryOutboxAsync(paymentSource, state.PaymentIntentId, scenario,
                TimeSpan.FromSeconds(IsOmise() ? 150 : 45));
            await WritePhaseMarkerAsync("outbox_persisted", scenario, order.Id, payment.Id);
            await WaitForControlPhaseAsync("broker_restarted", TimeSpan.FromSeconds(45));
            await payment.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            payment.Dispose();
            payment = await StartPaymentAsync(fixture.BaseAddress, paymentPort);
            await WaitForOrderStatusAsync(orderSource, state.OrderId, scenario == "void_response" ? "cancelled" : "completed", TimeSpan.FromSeconds(75));
            await WaitForOutboxPublishedAsync(paymentSource, state.PaymentIntentId, TerminalEventType(scenario),
                TimeSpan.FromSeconds(75));
            await VerifyPaymentEvidenceDeliveryAsync(paymentSource, state.PaymentIntentId, scenario);
            if (scenario != "intent_created")
            {
                Guid eventId = await ReconciliationEventIdAsync(paymentSource, state.PaymentIntentId, scenario)
                    ?? throw new InvalidOperationException("Persisted reconciliation event is missing.");
                await WaitForInboxCompletedAsync(orderSource, eventId, TimeSpan.FromSeconds(15));
                if (scenario.StartsWith("void_", StringComparison.Ordinal))
                {
                    await ReplayWithInboxBarrierAsync(orderSource, paymentSource, eventId, state.PaymentIntentId, scenario);
                    Assert.Equal(scenario == "void_response" ? 1 : 0, fixture.ReleaseAttempts);
                    Assert.Equal(scenario == "void_response" ? 1 : 0, fixture.CancelAttempts);
                }
            }
        }
        finally
        {
            if (payment is not null)
            {
                await StopProcessAsync(payment);
                payment.Dispose();
            }
            await StopProcessAsync(order);
        }
    }

    [OrderProviderRecoveryLiveFact("verify")]
    public async Task Verify_provider_payment_process_interruption_matrix()
    {
        await using NpgsqlDataSource order = NpgsqlDataSource.Create(OrderConnection());
        await using NpgsqlDataSource payment = NpgsqlDataSource.Create(PaymentConnection());
        var evidence = new List<object>();
        foreach (string scenario in Scenarios())
        {
            ScenarioState state = await ReadScenarioAsync(payment, scenario);
            if (scenario.StartsWith("void_", StringComparison.Ordinal))
            {
                Assert.Equal(1L, await ScalarAsync(order, "SELECT count(*) FROM orders WHERE id=$1 AND status=$2 AND payment_intent_id=$3", state.OrderId, scenario == "void_response" ? "cancelled" : "completed", state.PaymentIntentId));
                Assert.Equal(1L, await ScalarAsync(payment, "SELECT count(*) FROM payment_intents WHERE id=$1 AND status='voided'", state.PaymentIntentId));
                Assert.Equal(1L, await ScalarAsync(payment, "SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1 AND event_type='payment.void-started.v1'", state.PaymentIntentId));
                Assert.Equal(0L, await ScalarAsync(payment, "SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1 AND event_type='payment.capture-started.v1'", state.PaymentIntentId));
                Assert.Equal(1, await CallCountAsync(payment, scenario, "void_command"));
                Assert.Equal(1L, await ScalarAsync(payment, "SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1 AND event_type='payment.authorization-started.v1'", state.PaymentIntentId));
                Assert.Equal(1L, await ScalarAsync(payment, "SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1 AND event_type=$2 AND published_at_utc IS NOT NULL", state.PaymentIntentId, TerminalEventType(scenario)));
                Assert.Equal(scenario == "void_response" ? 1L : 0L, await ScalarAsync(order, "SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1 AND event_type=$2", state.OrderId, nameof(PaymentFailedV1)));
                Guid id = (await ReconciliationEventIdAsync(payment, state.PaymentIntentId, scenario))!.Value;
                await WaitForInboxCompletedAsync(order, id, TimeSpan.FromSeconds(15));
                evidence.Add(new { scenario, finalOrderStatus = scenario == "void_response" ? "PaymentFailed" : "Paid", finalPaymentStatus = "voided", paidProtectionFixture = scenario == "void_paid_protection", transactionalOutboxPublished = true, persistentPaymentEvidenceDelivered = true, orderInboxCompleted = true, duplicateDeliveryVerified = true });
                continue;
            }
            Assert.Equal(1L, await ScalarAsync(order, "SELECT count(*) FROM orders WHERE id=$1 AND status='completed' AND payment_intent_id=$2", state.OrderId, state.PaymentIntentId));
            Assert.Equal(1L, await ScalarAsync(payment, "SELECT count(*) FROM payment_intents WHERE id=$1 AND order_id=$2 AND status='captured'", state.PaymentIntentId, state.OrderId));
            Assert.Equal(1L, await ScalarAsync(order, "SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1 AND event_type=$2", state.OrderId, nameof(PaymentCompletedV1)));
            long authorizationCommandStarts = await ScalarAsync(payment,
                "SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1 AND event_type='payment.authorization-started.v1'",
                state.PaymentIntentId);
            long captureCommandStarts = await ScalarAsync(payment,
                "SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1 AND event_type='payment.capture-started.v1'",
                state.PaymentIntentId);
            Assert.Equal(1L, authorizationCommandStarts);
            Assert.Equal(1L, captureCommandStarts);
            int armAuthorizationCommands = await CallCountAsync(payment, scenario, "authorize_command");
            int armCaptureCommands = await CallCountAsync(payment, scenario, "capture_command");
            Assert.Equal(scenario == "intent_created" ? 0 : 1, armAuthorizationCommands);
            Assert.Equal(scenario == "capture_response" ? 1 : 0, armCaptureCommands);
            Assert.Equal(1L, await ScalarAsync(payment,
                "SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1 AND event_type=$2 AND published_at_utc IS NOT NULL",
                state.PaymentIntentId, TerminalEventType(scenario)));
            Guid? reconciliationEventId = await ReconciliationEventIdAsync(payment, state.PaymentIntentId, scenario);
            if (reconciliationEventId is not null)
                Assert.Equal(1L, await ScalarAsync(order,
                    "SELECT count(*) FROM inbox_messages WHERE message_id=$1 AND consumer_name='order.payment-reconciled.v1' AND status='completed'",
                    reconciliationEventId.Value));
            evidence.Add(new
            {
                scenario,
                orderCount = 1,
                paymentIntentCount = 1,
                authorizationCommandStarts,
                captureCommandStarts,
                armAuthorizationCommands,
                armCaptureCommands,
                hostedPaymentRecovery = scenario != "intent_created",
                transactionalOutboxPublished = true,
                persistentPaymentEvidenceDelivered = true,
                orderInboxCompleted = reconciliationEventId is not null,
                finalOrderStatus = "Paid",
                finalPaymentStatus = "captured"
            });
        }

        await File.WriteAllTextAsync(EvidencePath(), JsonSerializer.Serialize(new
        {
            completedAtUtc = DateTimeOffset.UtcNow,
            provider = IsOmise() ? "Omise" : "GenericHttp",
            testMode = true,
            intentCreatedBeforeAuthorizationVerified = Scenarios().Contains("intent_created"),
            scenarios = evidence,
            stableOrderAndPaymentIdentityVerified = true,
            duplicateDurableCommandStartsDetected = false,
            providerReferencesRetained = false,
            secretsRetained = false,
            rawServiceLogsRetained = false
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task<ScenarioState> SeedAsync(string scenario, PostgresOrderRepository orderRepository,
        NpgsqlDataSource orderSource, PostgresPaymentIntents paymentRepository, NpgsqlDataSource paymentSource)
    {
        Guid organization = Guid.NewGuid(), restaurant = Guid.NewGuid(), branch = Guid.NewGuid(), orderId = Guid.NewGuid();
        Guid correlation = Guid.NewGuid();
        decimal amount = decimal.Parse(Required("NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_AMOUNT"),
            System.Globalization.CultureInfo.InvariantCulture);
        string currency = Required("NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_CURRENCY").Trim().ToUpperInvariant();
        var payment = paymentRepository.Create(organization,
            new CreatePaymentIntent(restaurant, branch, orderId, $"order:{orderId:D}", amount, currency, "card"),
            new PaymentMutationContext("provider-recovery-arm", correlation));
        var order = OrderAggregate.Create(orderId, organization, branch,
            [new OrderLine(Guid.NewGuid(), "Provider recovery acceptance", amount, 1, "kitchen")], currency, restaurant,
            idempotencyKey: FixtureOrderKey(scenario), workflowPaymentMethod: IsOmise() && scenario == "intent_created" ? "card_omise_test" : "card",
            workflowCorrelationId: correlation);
        order.Submit(); order.MarkInventoryReserved(); order.MarkKitchenAccepted();
        if (scenario != "intent_created") order.MarkPaymentPending(payment.Id);
        // Defensive fixture: a paid Order must ignore a delayed pre-capture void event.
        if (scenario == "void_paid_protection") order.MarkPaid();
        await orderRepository.SaveAsync(order, default);
        await using var due = orderSource.CreateCommand(
            "UPDATE orders SET workflow_recovery_next_attempt_at_utc=now()-interval '1 second' WHERE id=$1");
        due.Parameters.AddWithValue(orderId);
        await due.ExecuteNonQueryAsync();
        await using var insert = paymentSource.CreateCommand("INSERT INTO provider_recovery_scenarios(scenario,organization_id,restaurant_id,branch_id,order_id,payment_intent_id,correlation_id) VALUES($1,$2,$3,$4,$5,$6,$7)");
        insert.Parameters.AddWithValue(scenario); insert.Parameters.AddWithValue(organization); insert.Parameters.AddWithValue(restaurant);
        insert.Parameters.AddWithValue(branch); insert.Parameters.AddWithValue(orderId); insert.Parameters.AddWithValue(payment.Id); insert.Parameters.AddWithValue(correlation);
        await insert.ExecuteNonQueryAsync();
        return new(scenario, organization, restaurant, branch, orderId, payment.Id, correlation);
    }

    private static PostgresPaymentIntents NewPaymentRepository(NpgsqlDataSource source) =>
        new(source, Options.Create(new PaymentProviderOptions
        {
            LeaseDuration = IsOmise() ? TimeSpan.FromSeconds(70) : TimeSpan.FromMilliseconds(50), MaximumAuthorizationAttempts = 3,
            MaximumCaptureRecoveryAttempts = 3, RecoveryInterval = TimeSpan.FromMilliseconds(50)
        }));

    private static CountingProvider NewProvider(NpgsqlDataSource source, string scenario)
    {
        if (IsOmise())
        {
            var omiseClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { BaseAddress = new Uri("https://api.omise.co/"), Timeout = TimeSpan.FromSeconds(15) };
            return new CountingProvider(new OmisePaymentProvider(omiseClient, Options.Create(new PaymentProviderOptions
            { OmiseSecretKey = Required("NEXACONNECT_OMISE_TEST_SECRET_KEY") }),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<OmisePaymentProvider>.Instance), source, scenario, omiseClient);
        }
        var settings = ProviderSettings();
        var handler = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.ProviderAcceptanceTls.CreateHandler(
            Environment.GetEnvironmentVariable("NEXACONNECT_PAYMENT_PROVIDER_SIMULATOR_CERT_SHA256"),
            settings.Value.BaseUrl, Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT") == "Testing");
        var client = new HttpClient(handler) { BaseAddress = new Uri(settings.Value.BaseUrl), Timeout = settings.Value.RequestTimeout };
        return new CountingProvider(new HttpPaymentProvider(client, settings), source, scenario, client);
    }

    private static IOptions<PaymentProviderOptions> ProviderSettings() => Options.Create(new PaymentProviderOptions
    {
        Adapter = "GenericHttp", BaseUrl = Required("NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_URL"),
        ApiKey = Required("NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_API_KEY"),
        AuthorizationPath = Optional("NEXACONNECT_PAYMENT_PROVIDER_AUTHORIZATION_PATH", "v1/authorizations"),
        AuthorizationStatusPath = Optional("NEXACONNECT_PAYMENT_PROVIDER_AUTHORIZATION_STATUS_PATH", "v1/authorizations"),
        CapturePath = Optional("NEXACONNECT_PAYMENT_PROVIDER_CAPTURE_PATH", "v1/captures"),
        CaptureStatusPath = Optional("NEXACONNECT_PAYMENT_PROVIDER_CAPTURE_STATUS_PATH", "v1/captures"),
        VoidPath = Optional("NEXACONNECT_PAYMENT_PROVIDER_VOID_PATH", "v1/voids"),
        VoidStatusPath = Optional("NEXACONNECT_PAYMENT_PROVIDER_VOID_STATUS_PATH", "v1/voids"),
        RequestTimeout = TimeSpan.FromSeconds(30), LeaseDuration = TimeSpan.FromMilliseconds(50)
    });

    private static async Task RunMigrationAsync(string service, string connection, string variable, string scriptsRoot)
    {
        string? previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, connection);
            Assert.Equal(0, await MigrationApplication.RunAsync([
                "--service", service, "--scripts-root", scriptsRoot, "--target", "7",
                "--application-version", "0.16.0", "--confirm"]));
        }
        finally { Environment.SetEnvironmentVariable(variable, previous); }
    }

    private static async Task<Process> StartOrderAsync(Uri fixture, Uri paymentBaseAddress)
    {
        string hostDll = Required("NEXACONNECT_ORDER_PROVIDER_RECOVERY_HOST_DLL");
        int port = ReserveLoopbackPort();
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(hostDll)!
        };
        start.ArgumentList.Add(hostDll);
        RemoveCardCredentials(start);
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        start.Environment["DOTNET_ENVIRONMENT"] = "Testing";
        start.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        start.Environment["Persistence__Provider"] = "PostgreSQL";
        start.Environment["ConnectionStrings__Order"] = OrderConnection();
        start.Environment["Workflow__UseHttpAdapters"] = "true";
        start.Environment["CardCheckout__EnableOmiseTestCheckout"] = IsOmise() ? "true" : "false";
        start.Environment["WorkflowRecovery__Enabled"] = "true";
        start.Environment["WorkflowRecovery__PollInterval"] = "00:00:00.100";
        start.Environment["WorkflowRecovery__LeaseDuration"] = "00:00:02";
        start.Environment["WorkflowRecovery__RetryDelay"] = "00:00:00.200";
        start.Environment["PaymentReconciliationConsumer__Enabled"] = "true";
        start.Environment["PaymentReconciliationConsumer__ConnectionString"] = RabbitConnection();
        start.Environment["PaymentReconciliationConsumer__Queue"] = $"nexaconnect.order.provider-recovery.{Scenario()}";
        start.Environment["Outbox__Enabled"] = "false";
        start.Environment["Outbox__ConnectionString"] = RabbitConnection();
        start.Environment["Authentication__Authority"] = fixture.ToString();
        start.Environment["Authentication__Audience"] = "nexaconnect-api";
        start.Environment["Authentication__RequireHttpsMetadata"] = "false";
        start.Environment["Authentication__TokenEndpoint"] = new Uri(fixture, "token").ToString();
        start.Environment["Authentication__ClientId"] = "nexaconnect-order-service";
        start.Environment["Authentication__ClientSecret"] = "acceptance-only";
        foreach (string name in new[] { "Authorization", "PlatformDirectory", "Restaurant", "Catalog", "Inventory", "Kitchen" })
            start.Environment[$"Services__{name}"] = fixture.ToString();
        start.Environment["Services__Payment"] = paymentBaseAddress.ToString();
        Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the isolated Order host.");
        try
        {
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            await WaitForPortAsync(process, port, "Order", TimeSpan.FromSeconds(20));
            await WaitForQueueAsync($"nexaconnect.order.provider-recovery.{Scenario()}", TimeSpan.FromSeconds(20));
            return process;
        }
        catch
        {
            await StopProcessAsync(process);
            process.Dispose();
            throw;
        }
    }

    private static async Task<Process> StartPaymentAsync(Uri fixture, int port)
    {
        string hostDll = Required("NEXACONNECT_ORDER_PROVIDER_RECOVERY_PAYMENT_HOST_DLL");
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(hostDll)!
        };
        start.ArgumentList.Add(hostDll);
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        start.Environment["DOTNET_ENVIRONMENT"] = "Testing";
        start.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        start.Environment["Persistence__Provider"] = "PostgreSQL";
        start.Environment["ConnectionStrings__Payment"] = PaymentConnection();
        start.Environment["PaymentProvider__Adapter"] = "GenericHttp";
        start.Environment["PaymentProvider__BaseUrl"] = Required("NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_URL");
        start.Environment["PaymentProvider__ApiKey"] = Required("NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_API_KEY");
        start.Environment["PaymentProvider__SimulatorCertificateSha256"] =
            Environment.GetEnvironmentVariable("NEXACONNECT_PAYMENT_PROVIDER_SIMULATOR_CERT_SHA256") ?? "";
        start.Environment["PaymentProvider__AuthorizationPath"] = Optional(
            "NEXACONNECT_PAYMENT_PROVIDER_AUTHORIZATION_PATH", "v1/authorizations");
        start.Environment["PaymentProvider__AuthorizationStatusPath"] = Optional(
            "NEXACONNECT_PAYMENT_PROVIDER_AUTHORIZATION_STATUS_PATH", "v1/authorizations");
        start.Environment["PaymentProvider__CapturePath"] = Optional(
            "NEXACONNECT_PAYMENT_PROVIDER_CAPTURE_PATH", "v1/captures");
        start.Environment["PaymentProvider__CaptureStatusPath"] = Optional(
            "NEXACONNECT_PAYMENT_PROVIDER_CAPTURE_STATUS_PATH", "v1/captures");
        start.Environment["PaymentProvider__VoidPath"] = Optional(
            "NEXACONNECT_PAYMENT_PROVIDER_VOID_PATH", "v1/voids");
        start.Environment["PaymentProvider__VoidStatusPath"] = Optional(
            "NEXACONNECT_PAYMENT_PROVIDER_VOID_STATUS_PATH", "v1/voids");
        start.Environment["PaymentProvider__LeaseDuration"] = "00:00:00.050";
        if (IsOmise())
        {
            start.Environment["PaymentProvider__Adapter"] = "Omise";
            start.Environment["PaymentProvider__OmiseSecretKey"] = Required("NEXACONNECT_OMISE_TEST_SECRET_KEY");
            start.Environment["PaymentProvider__SimulatorCertificateSha256"] = "";
            start.Environment["PaymentProvider__RequestTimeout"] = "00:00:15";
            start.Environment["PaymentProvider__LeaseDuration"] = "00:01:10";
        }
        start.Environment["PaymentProvider__RecoveryInterval"] = "00:00:00.100";
        start.Environment["PaymentProvider__CaptureRecoveryEnabled"] = "true";
        start.Environment["PaymentProvider__VoidRecoveryEnabled"] = "true";
        start.Environment["Outbox__Enabled"] = "true";
        start.Environment["Outbox__ConnectionString"] = RabbitConnection();
        start.Environment["Outbox__PollInterval"] = "00:00:00.100";
        start.Environment["Authentication__Authority"] = fixture.ToString();
        start.Environment["Authentication__Audience"] = "nexaconnect-api";
        start.Environment["Authentication__RequireHttpsMetadata"] = "false";
        start.Environment["Authentication__TokenEndpoint"] = new Uri(fixture, "token").ToString();
        start.Environment["Authentication__ClientId"] = "nexaconnect-payment-service";
        start.Environment["Authentication__ClientSecret"] = "acceptance-only";
        foreach (string name in new[] { "Authorization", "PlatformDirectory", "Restaurant", "Order" })
            start.Environment[$"Services__{name}"] = fixture.ToString();
        string? omiseSecret = IsOmise() ? start.Environment["PaymentProvider__OmiseSecretKey"] : null;
        RemoveCardCredentials(start);
        if (omiseSecret is not null) start.Environment["PaymentProvider__OmiseSecretKey"] = omiseSecret;
        Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the isolated Payment host.");
        try
        {
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            await WaitForPortAsync(process, port, "Payment", TimeSpan.FromSeconds(20));
            return process;
        }
        catch
        {
            await StopProcessAsync(process);
            process.Dispose();
            throw;
        }
    }

    private static async Task WaitForQueueAsync(string queue, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var factory = new ConnectionFactory { Uri = new Uri(RabbitConnection()) };
                await using IConnection connection = await factory.CreateConnectionAsync();
                await using IChannel channel = await connection.CreateChannelAsync();
                await channel.QueueDeclarePassiveAsync(queue);
                return;
            }
            catch { await Task.Delay(100); }
        }
        throw new TimeoutException("Order reconciliation queue was not declared.");
    }

    private static async Task<ScenarioState> ReadScenarioAsync(NpgsqlDataSource source, string scenario)
    {
        await using var command = source.CreateCommand("SELECT organization_id,restaurant_id,branch_id,order_id,payment_intent_id,correlation_id FROM provider_recovery_scenarios WHERE scenario=$1");
        command.Parameters.AddWithValue(scenario);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new(scenario, reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3), reader.GetGuid(4), reader.GetGuid(5));
    }

    private static async Task WaitForOrderStatusAsync(NpgsqlDataSource source, Guid orderId, string expected, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var command = source.CreateCommand("SELECT status FROM orders WHERE id=$1");
            command.Parameters.AddWithValue(orderId);
            if (string.Equals(Convert.ToString(await command.ExecuteScalarAsync()), expected, StringComparison.Ordinal)) return;
            await Task.Delay(100);
        }
        throw new TimeoutException($"Provider recovery Order did not reach {expected}.");
    }

    private static async Task<long> ScalarAsync(NpgsqlDataSource source, string sql, params object[] values)
    {
        await using var command = source.CreateCommand(sql);
        foreach (object value in values) command.Parameters.AddWithValue(value);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CallCountAsync(NpgsqlDataSource source, string scenario, string operation) =>
        Convert.ToInt32(await ScalarAsync(source,
            "SELECT COALESCE((SELECT call_count FROM provider_recovery_calls WHERE scenario=$1 AND operation=$2),0)",
            scenario, operation));

    private static async Task<Guid?> ReconciliationEventIdAsync(
        NpgsqlDataSource source, Guid paymentIntentId, string scenario)
    {
        if (scenario == "intent_created") return null;
        await using var command = source.CreateCommand(
            "SELECT id FROM outbox_messages WHERE aggregate_id=$1 AND event_type=$2 ORDER BY occurred_at_utc DESC LIMIT 1");
        command.Parameters.AddWithValue(paymentIntentId);
        command.Parameters.AddWithValue(TerminalEventType(scenario));
        object? value = await command.ExecuteScalarAsync();
        return value is Guid eventId ? eventId : null;
    }

    private static string TerminalEventType(string scenario) => scenario switch
    {
        "intent_created" => "payment.captured.v1",
        "authorization_response" => "payment.authorization-reconciled.v1",
        "capture_response" => "payment.capture-reconciled.v1",
        "void_response" or "void_paid_protection" => "payment.void-reconciled.v1",
        _ => throw new InvalidOperationException("Unsupported provider recovery scenario.")
    };

    private static async Task WaitForHostedRecoveryOutboxAsync(
        NpgsqlDataSource source, Guid paymentIntentId, string scenario, TimeSpan timeout)
    {
        string expectedStatus = scenario.StartsWith("void_", StringComparison.Ordinal) ? "voided" : scenario == "authorization_response" ? "authorized" : "captured";
        string eventType = TerminalEventType(scenario);
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var command = source.CreateCommand("""
                SELECT count(*)
                FROM payment_intents intent
                JOIN outbox_messages message ON message.aggregate_id=intent.id
                WHERE intent.id=$1 AND intent.status=$2 AND message.event_type=$3
                  AND message.published_at_utc IS NULL AND message.last_error_category IS NOT NULL
                """);
            command.Parameters.AddWithValue(paymentIntentId);
            command.Parameters.AddWithValue(expectedStatus);
            command.Parameters.AddWithValue(eventType);
            if (Convert.ToInt64(await command.ExecuteScalarAsync()) == 1) return;
            await Task.Delay(100);
        }
        throw new TimeoutException($"Hosted Payment did not persist the unpublished {eventType} outbox message.");
    }

    private static async Task WaitForOutboxPublishedAsync(
        NpgsqlDataSource source, Guid paymentIntentId, string eventType, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var command = source.CreateCommand(
                "SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1 AND event_type=$2 AND published_at_utc IS NOT NULL");
            command.Parameters.AddWithValue(paymentIntentId);
            command.Parameters.AddWithValue(eventType);
            if (Convert.ToInt64(await command.ExecuteScalarAsync()) == 1) return;
            await Task.Delay(100);
        }
        throw new TimeoutException($"Hosted Payment did not publish {eventType} after restart.");
    }

    private static async Task VerifyPaymentEvidenceDeliveryAsync(NpgsqlDataSource source, Guid intentId, string scenario)
    {
        await using var command = source.CreateCommand(
            "SELECT id FROM outbox_messages WHERE aggregate_id=$1 AND event_type=$2 AND published_at_utc IS NOT NULL");
        command.Parameters.AddWithValue(intentId);
        command.Parameters.AddWithValue(TerminalEventType(scenario));
        Guid expected = Assert.IsType<Guid>(await command.ExecuteScalarAsync());
        var factory = new ConnectionFactory { Uri = new Uri(RabbitConnection()) };
        await using IConnection connection = await factory.CreateConnectionAsync();
        await using IChannel channel = await connection.CreateChannelAsync();
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            BasicGetResult? delivery = await channel.BasicGetAsync(PaymentEvidenceQueue, autoAck: false);
            if (delivery is null) { await Task.Delay(100); continue; }
            using JsonDocument document = JsonDocument.Parse(delivery.Body);
            bool matches = document.RootElement.GetProperty("EventId").GetGuid() == expected;
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false);
            if (!matches) continue;
            Assert.Equal(TerminalEventType(scenario), delivery.RoutingKey);
            Assert.True(delivery.BasicProperties.Persistent);
            return;
        }
        throw new TimeoutException("Payment terminal event was not delivered persistently to the evidence queue.");
    }

    private static async Task ReplayWithInboxBarrierAsync(NpgsqlDataSource order, NpgsqlDataSource payment,
        Guid eventId, Guid intentId, string scenario)
    {
        await using NpgsqlConnection connection = await order.OpenConnectionAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        // A completed row makes INSERT ON CONFLICT and the conditional UPDATE no-ops;
        // a row lock therefore cannot prove duplicate delivery. Block the INSERT's
        // table lock instead, only in this disposable acceptance database.
        await using var hold = new NpgsqlCommand("LOCK TABLE inbox_messages IN SHARE MODE", connection, transaction);
        await hold.ExecuteNonQueryAsync();
        await using var replay = payment.CreateCommand("UPDATE outbox_messages SET published_at_utc=NULL, next_attempt_at_utc=now() WHERE id=$1");
        replay.Parameters.AddWithValue(eventId);
        Assert.Equal(1, await replay.ExecuteNonQueryAsync());
        await WaitForOutboxPublishedAsync(payment, intentId, TerminalEventType(scenario), TimeSpan.FromSeconds(15));
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        bool observed = false;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await ScalarAsync(order, "SELECT count(*) FROM pg_stat_activity WHERE $1=ANY(pg_blocking_pids(pid))", connection.ProcessID) > 0)
            { observed = true; break; }
            await Task.Delay(50);
        }
        Assert.True(observed, "Duplicate must reach the hosted Order inbox before assertions.");
        await transaction.CommitAsync();
        deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (await ScalarAsync(order, "SELECT count(*) FROM pg_stat_activity WHERE $1=ANY(pg_blocking_pids(pid))", connection.ProcessID) > 0)
        {
            if (DateTimeOffset.UtcNow >= deadline) throw new TimeoutException("Duplicate inbox claim did not finish.");
            await Task.Delay(50);
        }
        await WaitForInboxCompletedAsync(order, eventId, TimeSpan.FromSeconds(15));
    }

    private static async Task SubmitRecoveredCardTokenAsync(Uri fixture, NpgsqlDataSource orders,
        NpgsqlDataSource payments, ScenarioState state, Process orderHost)
    {
        await WaitForOrderStatusAsync(orders, state.OrderId, "payment_pending", TimeSpan.FromSeconds(30));
        Assert.Equal(1L, await ScalarAsync(payments, "SELECT count(*) FROM payment_intents WHERE id=$1 AND status='pending'", state.PaymentIntentId));
        Assert.Equal(0L, await ScalarAsync(payments, "SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1 AND event_type='payment.authorization-started.v1'", state.PaymentIntentId));
        OrderAggregate original = await new PostgresOrderRepository(orders).GetAsync(state.OrderId, default)
            ?? throw new InvalidOperationException("Recovered card Order is missing.");
        // GetAsync restores the aggregate, not the transport idempotency record.
        // Verify the fixture's original key still resolves to this exact durable Order.
        var keyedOrder = await new PostgresOrderRepository(orders).FindByIdempotencyKeyAsync(
            state.RestaurantId, FixtureOrderKey(state.Scenario), default);
        Assert.True(keyedOrder?.Id == state.OrderId, "Original checkout identity could not be verified; no token was submitted.");
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        using var tokenResponse = await client.PostAsync(new Uri(fixture, "token"), null);
        Assert.True(tokenResponse.IsSuccessStatusCode);
        using JsonDocument credentials = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var orderBase = new Uri(orderHost.StartInfo.Environment["ASPNETCORE_URLS"]!);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(orderBase, "/api/order/v1/workflows/place"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", credentials.RootElement.GetProperty("access_token").GetString());
        request.Headers.Add("X-Nexa-Organization-Id", state.OrganizationId.ToString("D"));
        request.Headers.Add("X-Nexa-Application-Code", "nexa_connect");
        request.Headers.Add("X-Correlation-ID", state.CorrelationId.ToString("D"));
        request.Content = System.Net.Http.Json.JsonContent.Create(BuildRecoveredCardRequest(original, state, CardToken("intent_created")));
        using var response = await client.SendAsync(request);
        Assert.True(response.IsSuccessStatusCode, $"Fresh-token handoff was not confirmed (HTTP {(int)response.StatusCode}). Inspect provider state before any new run.");
        await WaitForOrderStatusAsync(orders, state.OrderId, "completed", TimeSpan.FromSeconds(30));
    }

    private static PlaceOrderRequest BuildRecoveredCardRequest(OrderAggregate original, ScenarioState state, string? token) =>
        new(state.RestaurantId, state.OrganizationId, state.BranchId, original.Currency, "card_omise_test",
            FixtureOrderKey(state.Scenario), original.Lines.Select(line => new PlaceOrderRequestLine(line.ProductId, line.Quantity)).ToArray(),
            state.OrderId, state.CorrelationId, token);

    [Fact]
    public async Task Rehydrated_card_fixture_keeps_original_checkout_key_in_http_request()
    {
        var state = new ScenarioState("intent_created", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var original = OrderAggregate.Create(state.OrderId, state.OrganizationId, state.BranchId,
            [new OrderLine(Guid.NewGuid(), "Restored fixture", 50m, 1, "kitchen")], "THB", state.RestaurantId,
            workflowPaymentMethod: "card_omise_test");
        Assert.Null(original.IdempotencyKey); // Matches PostgreSQL aggregate rehydration.
        using var content = System.Net.Http.Json.JsonContent.Create(BuildRecoveredCardRequest(original, state, null));
        using JsonDocument body = JsonDocument.Parse(await content.ReadAsStringAsync());
        Assert.Equal("provider-recovery-intent_created", body.RootElement.GetProperty("idempotencyKey").GetString());
        Assert.Equal(state.OrderId, body.RootElement.GetProperty("orderId").GetGuid());
        Assert.Equal(original.Lines[0].ProductId, body.RootElement.GetProperty("lines")[0].GetProperty("productId").GetGuid());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("cardToken").ValueKind);
    }

    private static async Task WriteMarkerAsync(string path, string scenario)
    {
        string temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new { scenario, providerResponseObserved = scenario != "intent_created" }));
        File.Move(temporary, path, true);
    }

    private static async Task WaitForInboxCompletedAsync(NpgsqlDataSource source, Guid eventId, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await ScalarAsync(source,
                "SELECT count(*) FROM inbox_messages WHERE message_id=$1 AND consumer_name='order.payment-reconciled.v1' AND status='completed'",
                eventId) == 1) return;
            await Task.Delay(100);
        }
        throw new TimeoutException("Order did not complete the persisted reconciliation inbox message.");
    }

    private static async Task WritePhaseMarkerAsync(
        string phase, string scenario, int orderProcessId, int? paymentProcessId)
    {
        string path = MarkerPath();
        string temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new
        {
            phase,
            scenario,
            orderProcessId,
            paymentProcessId
        }));
        File.Move(temporary, path, true);
    }

    private static async Task WaitForControlPhaseAsync(string expectedPhase, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                if (File.Exists(ControlPath()))
                {
                    using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(ControlPath()));
                    if (document.RootElement.GetProperty("phase").GetString() == expectedPhase) return;
                }
            }
            catch (IOException) { }
            catch (JsonException) { }
            await Task.Delay(100);
        }
        throw new TimeoutException($"Hosted Payment recovery did not receive control phase {expectedPhase}.");
    }

    private static async Task WaitForPortAsync(Process process, int port, string hostName, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (process.HasExited) throw new InvalidOperationException($"{hostName} host exited with code {process.ExitCode}.");
            try { using var client = new TcpClient(); await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(TimeSpan.FromMilliseconds(250)); return; }
            catch { await Task.Delay(100); }
        }
        throw new TimeoutException($"{hostName} host did not bind its loopback port.");
    }

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port;
    }

    private static async Task StopProcessAsync(Process process)
    {
        if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NexaConnect.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"Missing provider recovery setting: {name}.");
    private static string Optional(string name, string fallback) =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)) ? fallback : Environment.GetEnvironmentVariable(name)!;
    private static string Scenario() => Required("NEXACONNECT_ORDER_PROVIDER_RECOVERY_SCENARIO");
    private static string OrderConnection() => Required("NEXACONNECT_ORDER_PROVIDER_RECOVERY_ORDER_DB");
    private static string PaymentConnection() => Required("NEXACONNECT_ORDER_PROVIDER_RECOVERY_PAYMENT_DB");
    private static string RabbitConnection() => Required("NEXACONNECT_ORDER_PROVIDER_RECOVERY_RABBITMQ");
    private static string MarkerPath() => Required("NEXACONNECT_ORDER_PROVIDER_RECOVERY_MARKER");
    private static string ControlPath() => Required("NEXACONNECT_ORDER_PROVIDER_RECOVERY_CONTROL");
    private static string EvidencePath() => Required("NEXACONNECT_ORDER_PROVIDER_RECOVERY_EVIDENCE");

    private sealed record ScenarioState(string Scenario, Guid OrganizationId, Guid RestaurantId, Guid BranchId,
        Guid OrderId, Guid PaymentIntentId, Guid CorrelationId);

    private sealed class CountingProvider(IPaymentProvider inner, NpgsqlDataSource source, string scenario, IDisposable owner)
        : IPaymentProvider, IDisposable
    {
        public void ValidateAuthorizationInput(string? token) => inner.ValidateAuthorizationInput(token);
        public async Task<ProviderAuthorizationResult> AuthorizeAsync(PaymentIntent intent, string? token, CancellationToken cancellationToken)
        { await IncrementAsync("authorize_command", cancellationToken); return await inner.AuthorizeAsync(intent, token, cancellationToken); }
        public async Task<ProviderAuthorizationResult> AuthorizeAsync(PaymentIntent intent, CancellationToken cancellationToken)
        { await IncrementAsync("authorize_command", cancellationToken); return await inner.AuthorizeAsync(intent, cancellationToken); }
        public async Task<ProviderAuthorizationStatus> GetAuthorizationStatusAsync(PaymentIntent intent, CancellationToken cancellationToken)
        { await IncrementAsync("authorization_status", cancellationToken); return await inner.GetAuthorizationStatusAsync(intent, cancellationToken); }
        public async Task<ProviderCaptureResult> CaptureAsync(PaymentIntent intent, CancellationToken cancellationToken)
        { await IncrementAsync("capture_command", cancellationToken); return await inner.CaptureAsync(intent, cancellationToken); }
        public async Task<ProviderCaptureResult> GetCaptureStatusAsync(PaymentIntent intent, CancellationToken cancellationToken)
        { await IncrementAsync("capture_status", cancellationToken); return await inner.GetCaptureStatusAsync(intent, cancellationToken); }
        public async Task<ProviderVoidResult> VoidAsync(PaymentIntent intent, CancellationToken cancellationToken)
        { await IncrementAsync("void_command", cancellationToken); return await inner.VoidAsync(intent, cancellationToken); }
        public Task<ProviderVoidResult> GetVoidStatusAsync(PaymentIntent intent, CancellationToken cancellationToken) => inner.GetVoidStatusAsync(intent, cancellationToken);
        private async Task IncrementAsync(string operation, CancellationToken cancellationToken)
        {
            await using var command = source.CreateCommand("INSERT INTO provider_recovery_calls(scenario,operation,call_count) VALUES($1,$2,1) ON CONFLICT(scenario,operation) DO UPDATE SET call_count=provider_recovery_calls.call_count+1");
            command.Parameters.AddWithValue(scenario); command.Parameters.AddWithValue(operation);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        public void Dispose() => owner.Dispose();
    }

    private sealed class HostedDependencyFixture : IAsyncDisposable
    {
        private readonly WebApplication app;
        private readonly RSA signingKey;
        private HostedDependencyFixture(WebApplication app, RSA signingKey)
        {
            this.app = app;
            this.signingKey = signingKey;
        }
        public Uri BaseAddress { get; private set; } = null!;
        public int ReleaseAttempts;
        public int CancelAttempts;

        public static async Task<HostedDependencyFixture> StartAsync()
        {
            RSA rsa = RSA.Create(2048);
            var securityKey = new RsaSecurityKey(rsa) { KeyId = Guid.NewGuid().ToString("N") };
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            WebApplication app = builder.Build();
            var fixture = new HostedDependencyFixture(app, rsa);
            string? issuer = null;
            app.MapGet("/.well-known/openid-configuration", () => Results.Json(new
            {
                issuer,
                jwks_uri = issuer + "/jwks",
                token_endpoint = issuer + "/token",
                id_token_signing_alg_values_supported = new[] { SecurityAlgorithms.RsaSha256 }
            }));
            app.MapGet("/jwks", () =>
            {
                RSAParameters parameters = rsa.ExportParameters(false);
                return Results.Json(new
                {
                    keys = new[]
                    {
                        new
                        {
                            kty = "RSA",
                            use = "sig",
                            kid = securityKey.KeyId,
                            alg = SecurityAlgorithms.RsaSha256,
                            n = Base64UrlEncoder.Encode(parameters.Modulus),
                            e = Base64UrlEncoder.Encode(parameters.Exponent)
                        }
                    }
                });
            });
            app.MapPost("/token", () =>
            {
                DateTime now = DateTime.UtcNow;
                var token = new JwtSecurityToken(
                    issuer,
                    "nexaconnect-api",
                    [
                        new Claim(JwtRegisteredClaimNames.Sub, "provider-recovery-order-workload"),
                        new Claim("azp", "nexaconnect-order-service")
                    ],
                    now.AddSeconds(-5),
                    now.AddMinutes(5),
                    new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256));
                return Results.Json(new
                {
                    access_token = new JwtSecurityTokenHandler().WriteToken(token),
                    token_type = "Bearer",
                    expires_in = 300
                });
            });
            app.MapPost("/api/inventory/v1/branches/{branchId:guid}/reservations/{orderId:guid}/release", () => { Interlocked.Increment(ref fixture.ReleaseAttempts); return Results.Ok(); });
            app.MapPost("/api/kitchen/v1/tickets/{orderId:guid}/cancel", () => { Interlocked.Increment(ref fixture.CancelAttempts); return Results.Ok(); });
            await app.StartAsync();
            fixture.BaseAddress = new Uri(app.Urls.Single().TrimEnd('/') + "/");
            issuer = fixture.BaseAddress.ToString().TrimEnd('/');
            return fixture;
        }

        public async ValueTask DisposeAsync()
        {
            await app.DisposeAsync();
            signingKey.Dispose();
        }
    }
}

public sealed class OrderProviderRecoveryLiveFactAttribute : FactAttribute
{
    public OrderProviderRecoveryLiveFactAttribute(string requiredStage)
    {
        string? environment = Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT");
        string? stage = Environment.GetEnvironmentVariable("NEXACONNECT_ORDER_PROVIDER_RECOVERY_STAGE");
        string? url = Environment.GetEnvironmentVariable("NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_URL");
        bool safeUrl = Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
            && !uri.Host.Contains("prod", StringComparison.OrdinalIgnoreCase);
        string adapter = Environment.GetEnvironmentVariable("NEXACONNECT_ORDER_PROVIDER_RECOVERY_ADAPTER") ?? "GenericHttp";
        bool safeAdapter = adapter == "GenericHttp" || adapter == "Omise"
            && url == "https://api.omise.co/"
            && OmisePaymentProvider.IsTestSecret(Environment.GetEnvironmentVariable("NEXACONNECT_OMISE_TEST_SECRET_KEY"))
            && Environment.GetEnvironmentVariable("NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_CURRENCY") == "THB"
            && (requiredStage != "arm" || (Environment.GetEnvironmentVariable("NEXACONNECT_ORDER_PROVIDER_RECOVERY_SCENARIO") != "intent_created"
                || Environment.GetEnvironmentVariable("NEXACONNECT_ORDER_PROVIDER_RECOVERY_CARD_HANDOFF") == "1")
                && System.Text.RegularExpressions.Regex.IsMatch(Environment.GetEnvironmentVariable("NEXACONNECT_OMISE_" +
                    Environment.GetEnvironmentVariable("NEXACONNECT_ORDER_PROVIDER_RECOVERY_SCENARIO")?.ToUpperInvariant() + "_TEST_TOKEN") ?? "",
                    "^tokn_test_[a-z0-9]{10,64}$"));
        string[] required = [
            "NEXACONNECT_ORDER_PROVIDER_RECOVERY_ORDER_DB", "NEXACONNECT_ORDER_PROVIDER_RECOVERY_PAYMENT_DB",
            "NEXACONNECT_ORDER_PROVIDER_RECOVERY_RABBITMQ", "NEXACONNECT_ORDER_PROVIDER_RECOVERY_HOST_DLL",
            "NEXACONNECT_ORDER_PROVIDER_RECOVERY_PAYMENT_HOST_DLL", "NEXACONNECT_ORDER_PROVIDER_RECOVERY_CONTROL",
            "NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_API_KEY", "NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_AMOUNT",
            "NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_CURRENCY"];
        if (Environment.GetEnvironmentVariable("NEXACONNECT_ORDER_PROVIDER_RECOVERY_LIVE_ACCEPTANCE") != "1"
            || environment != "Testing" || stage != requiredStage || !safeUrl || !safeAdapter
            || required.Any(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
            Skip = "Provider-payment recovery live acceptance requires its guarded launcher, disposable databases/broker, exact stage, and a non-production HTTPS provider sandbox.";
    }
}
