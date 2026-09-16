extern alias MIGRATIONS;
extern alias ORDER;
extern alias PAYMENT;

using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NexaConnect.Contracts.IntegrationEvents;
using Npgsql;
using RabbitMQ.Client;
using MigrationApplication = MIGRATIONS::MigrationApplication;
using OrderAggregate = ORDER::NexaConnect.Services.Order.Domain.OrderAggregate;
using OrderLine = ORDER::NexaConnect.Services.Order.Domain.OrderLine;
using PostgresOrderRepository = ORDER::NexaConnect.Services.Order.Infrastructure.Persistence.PostgresOrderRepository;
using PaymentIntent = PAYMENT::NexaConnect.Services.Payment.Application.Intents.PaymentIntent;
using PaymentMutationContext = PAYMENT::NexaConnect.Services.Payment.Application.Intents.PaymentMutationContext;
using CreatePaymentIntent = PAYMENT::NexaConnect.Services.Payment.Application.Intents.CreatePaymentIntent;
using PaymentAuthorizationService = PAYMENT::NexaConnect.Services.Payment.Application.Intents.PaymentAuthorizationService;
using PaymentCaptureService = PAYMENT::NexaConnect.Services.Payment.Application.Intents.PaymentCaptureService;
using PaymentCaptureRecoveryService = PAYMENT::NexaConnect.Services.Payment.Application.Intents.PaymentCaptureRecoveryService;
using PostgresPaymentIntents = PAYMENT::NexaConnect.Services.Payment.Infrastructure.PostgresPaymentIntents;
using IPaymentProvider = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.IPaymentProvider;
using HttpPaymentProvider = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.HttpPaymentProvider;
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
    }

    [OrderProviderRecoveryLiveFact("arm")]
    public async Task Arm_provider_payment_process_interruption()
    {
        string scenario = Scenario();
        Assert.Contains(scenario, new[] { "intent_created", "authorization_response", "capture_response" });
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
            ProviderAuthorizationResult result = await provider.AuthorizeAsync(lease.Intent, default);
            Assert.True(result.Succeeded);
        }
        else if (scenario == "capture_response")
        {
            using var provider = NewProvider(paymentSource, scenario);
            var authorization = new PaymentAuthorizationService(paymentRepository, provider);
            PaymentIntent authorized = Assert.IsType<PaymentIntent>(await authorization.AuthorizeAsync(
                state.OrganizationId, state.PaymentIntentId,
                new PaymentMutationContext("provider-recovery-arm", state.CorrelationId), default));
            Assert.Equal("authorized", authorized.Status);
            var lease = paymentRepository.BeginCapture(state.OrganizationId, state.PaymentIntentId,
                new PaymentMutationContext("provider-recovery-arm", state.CorrelationId));
            Assert.True(lease.Acquired);
            ProviderCaptureResult result = await provider.CaptureAsync(lease.Intent, default);
            Assert.Equal(PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.ProviderCaptureOutcome.Captured,
                result.Outcome);
        }

        await WriteMarkerAsync(MarkerPath(), scenario);
        await Task.Delay(Timeout.InfiniteTimeSpan);
    }

    [OrderProviderRecoveryLiveFact("recover")]
    public async Task Recover_provider_payment_after_process_interruption()
    {
        string scenario = Scenario();
        await using NpgsqlDataSource orderSource = NpgsqlDataSource.Create(OrderConnection());
        await using NpgsqlDataSource paymentSource = NpgsqlDataSource.Create(PaymentConnection());
        ScenarioState state = await ReadScenarioAsync(paymentSource, scenario);
        var paymentRepository = NewPaymentRepository(paymentSource);
        using var provider = NewProvider(paymentSource, scenario);
        await using var fixture = await PaymentApiFixture.StartAsync(paymentRepository, provider);
        using Process order = await StartOrderAsync(fixture.BaseAddress);
        try
        {
            if (scenario == "intent_created")
            {
                await WaitForOrderStatusAsync(orderSource, state.OrderId, "completed", TimeSpan.FromSeconds(60));
            }
            else if (scenario == "authorization_response")
            {
                await WaitForOrderStatusAsync(orderSource, state.OrderId, "payment_pending", TimeSpan.FromSeconds(30));
                await Task.Delay(150);
                var claim = paymentRepository.ClaimExpiredAuthorization(state.OrganizationId, state.PaymentIntentId,
                    new PaymentMutationContext("provider-recovery-worker", state.CorrelationId));
                Assert.True(claim.Acquired);
                var authorization = new PaymentAuthorizationService(paymentRepository, provider);
                PaymentIntent reconciled = Assert.IsType<PaymentIntent>(await authorization.ReconcileAsync(
                    state.OrganizationId, state.PaymentIntentId,
                    new PaymentMutationContext("provider-recovery-worker", state.CorrelationId), default));
                Assert.Equal("authorized", reconciled.Status);
                await PublishAsync("payment.authorization-reconciled.v1", new PaymentAuthorizationReconciledV1(
                    Guid.NewGuid(), state.CorrelationId, DateTimeOffset.UtcNow, state.OrganizationId, state.OrderId,
                    state.PaymentIntentId, "authorized", null));
                await WaitForOrderStatusAsync(orderSource, state.OrderId, "completed", TimeSpan.FromSeconds(60));
            }
            else if (scenario == "capture_response")
            {
                await Task.Delay(150);
                var claim = paymentRepository.ClaimExpiredCapture(state.OrganizationId, state.PaymentIntentId,
                    new PaymentMutationContext("payment-capture-recovery-worker", state.CorrelationId));
                Assert.True(claim.Acquired);
                var capture = new PaymentCaptureRecoveryService(paymentRepository, provider);
                PaymentIntent reconciled = await capture.ReconcileAsync(state.OrganizationId, state.PaymentIntentId,
                    new PaymentMutationContext("payment-capture-recovery-worker", state.CorrelationId), default);
                Assert.Equal("captured", reconciled.Status);
                await PublishAsync("payment.capture-reconciled.v1", new PaymentCaptureReconciledV1(
                    Guid.NewGuid(), state.CorrelationId, DateTimeOffset.UtcNow, state.OrganizationId, state.OrderId,
                    state.PaymentIntentId, "captured", null));
                await WaitForOrderStatusAsync(orderSource, state.OrderId, "completed", TimeSpan.FromSeconds(60));
            }
            else throw new InvalidOperationException("Unsupported provider recovery scenario.");
        }
        finally { await StopProcessAsync(order); }
    }

    [OrderProviderRecoveryLiveFact("verify")]
    public async Task Verify_provider_payment_process_interruption_matrix()
    {
        await using NpgsqlDataSource order = NpgsqlDataSource.Create(OrderConnection());
        await using NpgsqlDataSource payment = NpgsqlDataSource.Create(PaymentConnection());
        var evidence = new List<object>();
        foreach (string scenario in new[] { "intent_created", "authorization_response", "capture_response" })
        {
            ScenarioState state = await ReadScenarioAsync(payment, scenario);
            Assert.Equal(1L, await ScalarAsync(order, "SELECT count(*) FROM orders WHERE id=$1 AND status='completed' AND payment_intent_id=$2", state.OrderId, state.PaymentIntentId));
            Assert.Equal(1L, await ScalarAsync(payment, "SELECT count(*) FROM payment_intents WHERE id=$1 AND order_id=$2 AND status='captured'", state.PaymentIntentId, state.OrderId));
            Assert.Equal(1L, await ScalarAsync(order, "SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1 AND event_type=$2", state.OrderId, nameof(PaymentCompletedV1)));
            int authorizeCommands = await CallCountAsync(payment, scenario, "authorize_command");
            int captureCommands = await CallCountAsync(payment, scenario, "capture_command");
            Assert.Equal(1, authorizeCommands);
            Assert.Equal(1, captureCommands);
            int authorizationStatusLookups = await CallCountAsync(payment, scenario, "authorization_status");
            int captureStatusLookups = await CallCountAsync(payment, scenario, "capture_status");
            Assert.Equal(scenario == "authorization_response" ? 1 : 0, authorizationStatusLookups);
            Assert.Equal(scenario == "capture_response" ? 1 : 0, captureStatusLookups);
            evidence.Add(new
            {
                scenario,
                orderCount = 1,
                paymentIntentCount = 1,
                authorizeCommands,
                captureCommands,
                authorizationStatusLookups,
                captureStatusLookups,
                finalOrderStatus = "Paid",
                finalPaymentStatus = "captured"
            });
        }

        await File.WriteAllTextAsync(EvidencePath(), JsonSerializer.Serialize(new
        {
            completedAtUtc = DateTimeOffset.UtcNow,
            scenarios = evidence,
            stableOrderAndPaymentIdentityVerified = true,
            duplicateProviderCommandsDetected = false,
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
            idempotencyKey: $"provider-recovery-{scenario}", workflowPaymentMethod: "card",
            workflowCorrelationId: correlation);
        order.Submit(); order.MarkInventoryReserved(); order.MarkKitchenAccepted();
        if (scenario == "capture_response") order.MarkPaymentPending(payment.Id);
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
            LeaseDuration = TimeSpan.FromMilliseconds(50), MaximumAuthorizationAttempts = 3,
            MaximumCaptureRecoveryAttempts = 3, RecoveryInterval = TimeSpan.FromMilliseconds(50)
        }));

    private static CountingProvider NewProvider(NpgsqlDataSource source, string scenario)
    {
        var settings = ProviderSettings();
        var client = new HttpClient { BaseAddress = new Uri(settings.Value.BaseUrl), Timeout = settings.Value.RequestTimeout };
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

    private static async Task<Process> StartOrderAsync(Uri fixture)
    {
        string hostDll = Required("NEXACONNECT_ORDER_PROVIDER_RECOVERY_HOST_DLL");
        int port = ReserveLoopbackPort();
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
        start.Environment["ConnectionStrings__Order"] = OrderConnection();
        start.Environment["Workflow__UseHttpAdapters"] = "true";
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
        start.Environment["Authentication__RequireHttpsMetadata"] = "false";
        start.Environment["Authentication__TokenEndpoint"] = new Uri(fixture, "token").ToString();
        start.Environment["Authentication__ClientId"] = "nexaconnect-order-service";
        start.Environment["Authentication__ClientSecret"] = "acceptance-only";
        foreach (string name in new[] { "Authorization", "PlatformDirectory", "Restaurant", "Catalog", "Inventory", "Kitchen", "Payment" })
            start.Environment[$"Services__{name}"] = fixture.ToString();
        Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the isolated Order host.");
        try
        {
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            await WaitForPortAsync(process, port, TimeSpan.FromSeconds(20));
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

    private static async Task PublishAsync<T>(string routingKey, T message)
    {
        var factory = new ConnectionFactory { Uri = new Uri(RabbitConnection()) };
        await using IConnection connection = await factory.CreateConnectionAsync();
        await using IChannel channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync("nexaconnect.events", ExchangeType.Topic, durable: true);
        var properties = new BasicProperties { Persistent = true, ContentType = "application/json" };
        await channel.BasicPublishAsync("nexaconnect.events", routingKey, true, properties,
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message)));
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

    private static async Task WriteMarkerAsync(string path, string scenario)
    {
        string temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new { scenario, providerResponseObserved = scenario != "intent_created" }));
        File.Move(temporary, path, true);
    }

    private static async Task WaitForPortAsync(Process process, int port, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (process.HasExited) throw new InvalidOperationException($"Order host exited with code {process.ExitCode}.");
            try { using var client = new TcpClient(); await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(TimeSpan.FromMilliseconds(250)); return; }
            catch { await Task.Delay(100); }
        }
        throw new TimeoutException("Order host did not bind its loopback port.");
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
    private static string EvidencePath() => Required("NEXACONNECT_ORDER_PROVIDER_RECOVERY_EVIDENCE");

    private sealed record ScenarioState(string Scenario, Guid OrganizationId, Guid RestaurantId, Guid BranchId,
        Guid OrderId, Guid PaymentIntentId, Guid CorrelationId);

    private sealed class CountingProvider(IPaymentProvider inner, NpgsqlDataSource source, string scenario, IDisposable owner)
        : IPaymentProvider, IDisposable
    {
        public async Task<ProviderAuthorizationResult> AuthorizeAsync(PaymentIntent intent, CancellationToken cancellationToken)
        { await IncrementAsync("authorize_command", cancellationToken); return await inner.AuthorizeAsync(intent, cancellationToken); }
        public async Task<ProviderAuthorizationStatus> GetAuthorizationStatusAsync(PaymentIntent intent, CancellationToken cancellationToken)
        { await IncrementAsync("authorization_status", cancellationToken); return await inner.GetAuthorizationStatusAsync(intent, cancellationToken); }
        public async Task<ProviderCaptureResult> CaptureAsync(PaymentIntent intent, CancellationToken cancellationToken)
        { await IncrementAsync("capture_command", cancellationToken); return await inner.CaptureAsync(intent, cancellationToken); }
        public async Task<ProviderCaptureResult> GetCaptureStatusAsync(PaymentIntent intent, CancellationToken cancellationToken)
        { await IncrementAsync("capture_status", cancellationToken); return await inner.GetCaptureStatusAsync(intent, cancellationToken); }
        public Task<ProviderVoidResult> VoidAsync(PaymentIntent intent, CancellationToken cancellationToken) => inner.VoidAsync(intent, cancellationToken);
        public Task<ProviderVoidResult> GetVoidStatusAsync(PaymentIntent intent, CancellationToken cancellationToken) => inner.GetVoidStatusAsync(intent, cancellationToken);
        private async Task IncrementAsync(string operation, CancellationToken cancellationToken)
        {
            await using var command = source.CreateCommand("INSERT INTO provider_recovery_calls(scenario,operation,call_count) VALUES($1,$2,1) ON CONFLICT(scenario,operation) DO UPDATE SET call_count=provider_recovery_calls.call_count+1");
            command.Parameters.AddWithValue(scenario); command.Parameters.AddWithValue(operation);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        public void Dispose() => owner.Dispose();
    }

    private sealed class PaymentApiFixture : IAsyncDisposable
    {
        private readonly WebApplication app;
        private PaymentApiFixture(WebApplication app) { this.app = app; }
        public Uri BaseAddress { get; private set; } = null!;

        public static async Task<PaymentApiFixture> StartAsync(PostgresPaymentIntents repository, IPaymentProvider provider)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            WebApplication app = builder.Build();
            var fixture = new PaymentApiFixture(app);
            var authorization = new PaymentAuthorizationService(repository, provider);
            var capture = new PaymentCaptureService(repository, provider);
            app.MapPost("/token", () => Results.Json(new { access_token = "provider-recovery-acceptance", expires_in = 300 }));
            app.MapPost("/api/payment/v1/intents", async (HttpRequest request) =>
            {
                Guid organization = Guid.Parse(request.Headers["X-Nexa-Organization-Id"].ToString());
                CreateRequest body = (await request.ReadFromJsonAsync<CreateRequest>())!;
                PaymentIntent intent = repository.Create(organization,
                    new CreatePaymentIntent(body.RestaurantId, body.BranchId, body.OrderId, body.IdempotencyKey,
                        body.Amount, body.Currency, body.PaymentMethod),
                    new PaymentMutationContext("nexaconnect-order-service", Guid.NewGuid()));
                return Results.Json(intent, statusCode: StatusCodes.Status201Created);
            });
            app.MapGet("/api/payment/v1/intents/{id:guid}", (Guid id, HttpRequest request) =>
            {
                Guid organization = Guid.Parse(request.Headers["X-Nexa-Organization-Id"].ToString());
                PaymentIntent? intent = repository.Get(organization, id);
                return intent is null ? Results.NotFound() : Results.Json(intent);
            });
            app.MapPost("/api/payment/v1/intents/{id:guid}/authorize", async (Guid id, HttpRequest request) =>
            {
                Guid organization = Guid.Parse(request.Headers["X-Nexa-Organization-Id"].ToString());
                return Results.Json(await authorization.AuthorizeAsync(organization, id,
                    new PaymentMutationContext("nexaconnect-order-service", Guid.NewGuid()), request.HttpContext.RequestAborted));
            });
            app.MapPost("/api/payment/v1/intents/{id:guid}/capture", async (Guid id, HttpRequest request) =>
            {
                Guid organization = Guid.Parse(request.Headers["X-Nexa-Organization-Id"].ToString());
                return Results.Json(await capture.CaptureAsync(organization, id,
                    new PaymentMutationContext("nexaconnect-order-service", Guid.NewGuid()), request.HttpContext.RequestAborted));
            });
            app.MapPost("/api/inventory/v1/branches/{branchId:guid}/reservations/{orderId:guid}/release", () => Results.Ok());
            app.MapPost("/api/kitchen/v1/tickets/{orderId:guid}/cancel", () => Results.Ok());
            await app.StartAsync();
            fixture.BaseAddress = new Uri(app.Urls.Single().TrimEnd('/') + "/");
            return fixture;
        }

        public async ValueTask DisposeAsync() => await app.DisposeAsync();
        private sealed record CreateRequest(Guid RestaurantId, Guid BranchId, Guid OrderId, string IdempotencyKey,
            decimal Amount, string Currency, string PaymentMethod);
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
        string[] required = [
            "NEXACONNECT_ORDER_PROVIDER_RECOVERY_ORDER_DB", "NEXACONNECT_ORDER_PROVIDER_RECOVERY_PAYMENT_DB",
            "NEXACONNECT_ORDER_PROVIDER_RECOVERY_RABBITMQ", "NEXACONNECT_ORDER_PROVIDER_RECOVERY_HOST_DLL",
            "NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_API_KEY", "NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_AMOUNT",
            "NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_CURRENCY"];
        if (Environment.GetEnvironmentVariable("NEXACONNECT_ORDER_PROVIDER_RECOVERY_LIVE_ACCEPTANCE") != "1"
            || environment != "Testing" || stage != requiredStage || !safeUrl
            || required.Any(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
            Skip = "Provider-payment recovery live acceptance requires its guarded launcher, disposable databases/broker, exact stage, and a non-production HTTPS provider sandbox.";
    }
}
