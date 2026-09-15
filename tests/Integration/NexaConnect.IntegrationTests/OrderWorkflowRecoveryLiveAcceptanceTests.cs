extern alias MIGRATIONS;
extern alias ORDER;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NexaConnect.Contracts.IntegrationEvents;
using MigrationApplication = MIGRATIONS::MigrationApplication;
using ConfirmManualTenderCommand = ORDER::NexaConnect.Services.Order.Application.ManualTenders.ConfirmManualTenderCommand;
using ManualTenderApplicationService = ORDER::NexaConnect.Services.Order.Application.ManualTenders.ManualTenderApplicationService;
using OrderAggregate = ORDER::NexaConnect.Services.Order.Domain.OrderAggregate;
using OrderLine = ORDER::NexaConnect.Services.Order.Domain.OrderLine;
using OrderStatus = ORDER::NexaConnect.Services.Order.Domain.OrderStatus;
using PostgresOrderRepository = ORDER::NexaConnect.Services.Order.Infrastructure.Persistence.PostgresOrderRepository;

namespace NexaConnect.IntegrationTests;

[CollectionDefinition("Order workflow recovery live acceptance", DisableParallelization = true)]
public sealed class OrderWorkflowRecoveryLiveAcceptanceCollection;

[Collection("Order workflow recovery live acceptance")]
public sealed class OrderWorkflowRecoveryLiveAcceptanceTests
{
    [OrderWorkflowRecoveryLiveFact]
    public async Task Real_host_recovers_two_process_interruptions_and_dependency_outage_without_duplicates()
    {
        string connectionString = Environment.GetEnvironmentVariable("NEXACONNECT_ORDER_INTEGRATION_DB")!;
        string rabbitUri = Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_INTEGRATION_URI")!;
        string hostDll = Environment.GetEnvironmentVariable("NEXACONNECT_ORDER_RECOVERY_HOST_DLL")!;
        string evidencePath = Environment.GetEnvironmentVariable("NEXACONNECT_ORDER_RECOVERY_EVIDENCE_PATH")!;
        string scriptsRoot = Path.Combine(FindRepositoryRoot(), "src", "Tools", "NexaConnect.DataMigration", "Scripts");
        string? previousOrderDatabase = Environment.GetEnvironmentVariable("NEXACONNECT_ORDER_DB");
        try
        {
            Environment.SetEnvironmentVariable("NEXACONNECT_ORDER_DB", connectionString);
            Assert.Equal(0, await MigrationApplication.RunAsync([
                "--service", "Order", "--scripts-root", scriptsRoot, "--target", "6",
                "--application-version", "0.15.0", "--confirm"]));
        }
        finally
        {
            Environment.SetEnvironmentVariable("NEXACONNECT_ORDER_DB", previousOrderDatabase);
        }

        await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);
        var repository = new PostgresOrderRepository(dataSource);
        await using var fixture = await DependencyFixture.StartAsync();

        OrderAggregate submitted = await SeedAsync(repository, dataSource, OrderStatus.Submitted);
        fixture.ArmInventoryHold(submitted.Id);
        Process interrupted = await StartOrderAsync(hostDll, connectionString, rabbitUri, fixture.BaseAddress);
        try
        {
            await fixture.WaitForInventoryAsync(submitted.Id, TimeSpan.FromSeconds(20));
        }
        finally { await StopAndDisposeOrderAsync(interrupted); }
        Assert.Equal("submitted", await ReadStatusAsync(dataSource, submitted.Id));
        Process recovered = await StartOrderAsync(hostDll, connectionString, rabbitUri, fixture.BaseAddress);
        try
        {
            await WaitForStatusAsync(dataSource, submitted.Id, "inventory_reserved", TimeSpan.FromSeconds(20));
            await MakeRecoveryDueAsync(dataSource, submitted.Id);
            await WaitForStatusAsync(dataSource, submitted.Id, "kitchen_accepted", TimeSpan.FromSeconds(20));
        }
        finally { await StopAndDisposeOrderAsync(recovered); }
        Assert.Equal(2, fixture.InventoryAttempts(submitted.Id));
        Assert.Equal(1, fixture.KitchenAttempts(submitted.Id));
        Assert.All(fixture.Correlations(submitted.Id), value => Assert.Equal(submitted.WorkflowCorrelationId!.Value.ToString("D"), value));
        Assert.Equal(1, await EventCountAsync(dataSource, submitted.Id, nameof(InventoryReservedV1)));
        Assert.Equal(1, await EventCountAsync(dataSource, submitted.Id, nameof(KitchenTicketCreatedV1)));

        OrderAggregate inventoryReserved = await SeedAsync(repository, dataSource, OrderStatus.InventoryReserved);
        fixture.ArmKitchenHold(inventoryReserved.Id);
        interrupted = await StartOrderAsync(hostDll, connectionString, rabbitUri, fixture.BaseAddress);
        try
        {
            await fixture.WaitForKitchenAsync(inventoryReserved.Id, TimeSpan.FromSeconds(20));
        }
        finally { await StopAndDisposeOrderAsync(interrupted); }
        Assert.Equal("inventory_reserved", await ReadStatusAsync(dataSource, inventoryReserved.Id));
        recovered = await StartOrderAsync(hostDll, connectionString, rabbitUri, fixture.BaseAddress);
        try
        {
            await WaitForStatusAsync(dataSource, inventoryReserved.Id, "kitchen_accepted", TimeSpan.FromSeconds(20));
        }
        finally { await StopAndDisposeOrderAsync(recovered); }
        Assert.Equal(2, fixture.KitchenAttempts(inventoryReserved.Id));
        Assert.All(fixture.Correlations(inventoryReserved.Id), value => Assert.Equal(inventoryReserved.WorkflowCorrelationId!.Value.ToString("D"), value));
        Assert.Equal(1, await EventCountAsync(dataSource, inventoryReserved.Id, nameof(KitchenTicketCreatedV1)));

        OrderAggregate dependencyRetry = await SeedAsync(repository, dataSource, OrderStatus.Submitted);
        fixture.ArmInventoryFailures(dependencyRetry.Id, 3);
        recovered = await StartOrderAsync(hostDll, connectionString, rabbitUri, fixture.BaseAddress);
        try
        {
            await WaitForStatusAsync(dataSource, dependencyRetry.Id, "inventory_reserved", TimeSpan.FromSeconds(20));
            await MakeRecoveryDueAsync(dataSource, dependencyRetry.Id);
            await WaitForStatusAsync(dataSource, dependencyRetry.Id, "kitchen_accepted", TimeSpan.FromSeconds(20));
        }
        finally { await StopAndDisposeOrderAsync(recovered); }
        Assert.Equal(4, fixture.InventoryAttempts(dependencyRetry.Id));
        Assert.True(await RecoveryAttemptsAsync(dataSource, dependencyRetry.Id) >= 2);
        Assert.Equal(1, await EventCountAsync(dataSource, dependencyRetry.Id, nameof(InventoryReservedV1)));
        Assert.Equal(1, await EventCountAsync(dataSource, dependencyRetry.Id, nameof(KitchenTicketCreatedV1)));

        OrderAggregate payable = Assert.IsType<OrderAggregate>(await repository.GetAsync(submitted.Id, default));
        var settlementService = new ManualTenderApplicationService(repository);
        var settlement = await settlementService.ConfirmAsync(new ConfirmManualTenderCommand(
            payable.OrganizationId, payable.BranchId, payable.Id, Guid.NewGuid(), Guid.NewGuid(), "cash",
            payable.TotalAmount, "THB", false, null, "order-recovery-acceptance", Guid.NewGuid(), Guid.NewGuid()), default);
        Assert.NotNull(settlement);
        Assert.Equal("Paid", settlement.Status);
        Assert.Equal("completed", await ReadStatusAsync(dataSource, submitted.Id));

        Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
        await File.WriteAllTextAsync(evidencePath, JsonSerializer.Serialize(new
        {
            completedAtUtc = DateTimeOffset.UtcNow,
            submittedInterruptionRecovered = true,
            inventoryReservedInterruptionRecovered = true,
            dependencyRetryRecovered = true,
            processInterruptions = 2,
            inventoryAttemptsAfterSubmittedInterruption = fixture.InventoryAttempts(submitted.Id),
            kitchenAttemptsAfterInventoryInterruption = fixture.KitchenAttempts(inventoryReserved.Id),
            dependencyAttempts = fixture.InventoryAttempts(dependencyRetry.Id),
            duplicateTransitions = 0,
            originalCorrelationPropagated = true,
            recoveredOrderSettled = true,
            secretsRetained = false,
            rawServiceLogsRetained = false
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task<OrderAggregate> SeedAsync(PostgresOrderRepository repository, NpgsqlDataSource source, OrderStatus status)
    {
        var order = OrderAggregate.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            [new OrderLine(Guid.NewGuid(), "Recovery acceptance item", 45m, 1, "kitchen")], "THB", Guid.NewGuid(),
            idempotencyKey: $"recovery-{Guid.NewGuid():N}", workflowPaymentMethod: "cash_manual",
            workflowCorrelationId: Guid.NewGuid());
        order.Submit();
        if (status == OrderStatus.InventoryReserved) order.MarkInventoryReserved();
        await repository.SaveAsync(order, default);
        await MakeRecoveryDueAsync(source, order.Id);
        return order;
    }

    private static async Task<Process> StartOrderAsync(string hostDll, string connectionString, string rabbitUri, Uri dependencies)
    {
        int port = ReserveLoopbackPort();
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(hostDll)!
        };
        start.ArgumentList.Add(hostDll);
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        start.Environment["DOTNET_ENVIRONMENT"] = "Testing";
        start.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        start.Environment["Persistence__Provider"] = "PostgreSQL";
        start.Environment["ConnectionStrings__Order"] = connectionString;
        start.Environment["Workflow__UseHttpAdapters"] = "true";
        start.Environment["WorkflowRecovery__Enabled"] = "true";
        start.Environment["WorkflowRecovery__PollInterval"] = "00:00:00.100";
        start.Environment["WorkflowRecovery__LeaseDuration"] = "00:00:02";
        start.Environment["WorkflowRecovery__RetryDelay"] = "00:00:00.200";
        start.Environment["OperationalMetrics__PollInterval"] = "00:00:00.250";
        start.Environment["Outbox__ConnectionString"] = rabbitUri;
        start.Environment["Outbox__PollInterval"] = "00:00:00.100";
        start.Environment["Authentication__Authority"] = dependencies.ToString();
        start.Environment["Authentication__Audience"] = "nexaconnect-api";
        start.Environment["Authentication__RequireHttpsMetadata"] = "false";
        start.Environment["Authentication__TokenEndpoint"] = new Uri(dependencies, "token").ToString();
        start.Environment["Authentication__ClientId"] = "order-recovery-acceptance";
        start.Environment["Authentication__ClientSecret"] = "acceptance-only";
        foreach (string name in new[] { "Authorization", "PlatformDirectory", "Restaurant", "Catalog", "Inventory", "Kitchen", "Payment" })
            start.Environment[$"Services__{name}"] = dependencies.ToString();
        Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the disposable Order process.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await WaitForPortAsync(process, port, TimeSpan.FromSeconds(20));
            return process;
        }
        catch
        {
            await StopOrderAsync(process);
            process.Dispose();
            throw;
        }
    }

    private static async Task StopOrderAsync(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static async Task StopAndDisposeOrderAsync(Process process)
    {
        try { await StopOrderAsync(process); }
        finally { process.Dispose(); }
    }

    private static async Task WaitForPortAsync(Process process, int port, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (process.HasExited) throw new InvalidOperationException($"Disposable Order process exited with code {process.ExitCode}.");
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(TimeSpan.FromMilliseconds(250));
                return;
            }
            catch { await Task.Delay(100); }
        }
        throw new TimeoutException("Disposable Order process did not bind its loopback port.");
    }

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task MakeRecoveryDueAsync(NpgsqlDataSource source, Guid orderId)
    {
        await using var command = source.CreateCommand("UPDATE orders SET workflow_recovery_next_attempt_at_utc=now()-interval '1 second' WHERE id=$1");
        command.Parameters.AddWithValue(orderId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task WaitForStatusAsync(NpgsqlDataSource source, Guid orderId, string expected, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await ReadStatusAsync(source, orderId) == expected) return;
            await Task.Delay(100);
        }
        throw new TimeoutException($"Order did not reach expected acceptance status {expected}.");
    }

    private static async Task<string?> ReadStatusAsync(NpgsqlDataSource source, Guid orderId)
    {
        await using var command = source.CreateCommand("SELECT status FROM orders WHERE id=$1");
        command.Parameters.AddWithValue(orderId);
        return Convert.ToString(await command.ExecuteScalarAsync());
    }

    private static async Task<long> EventCountAsync(NpgsqlDataSource source, Guid orderId, string eventType)
    {
        await using var command = source.CreateCommand("SELECT count(*) FROM outbox_messages WHERE aggregate_id=$1 AND event_type=$2");
        command.Parameters.AddWithValue(orderId);
        command.Parameters.AddWithValue(eventType);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<int> RecoveryAttemptsAsync(NpgsqlDataSource source, Guid orderId)
    {
        await using var command = source.CreateCommand("SELECT workflow_recovery_attempt_count FROM orders WHERE id=$1");
        command.Parameters.AddWithValue(orderId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "NexaConnect.sln"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed class DependencyFixture : IAsyncDisposable
    {
        private readonly WebApplication app;
        private readonly ConcurrentDictionary<Guid, Probe> probes = new();

        private DependencyFixture(WebApplication app) { this.app = app; }
        public Uri BaseAddress { get; private set; } = null!;

        public static async Task<DependencyFixture> StartAsync()
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            WebApplication app = builder.Build();
            var fixture = new DependencyFixture(app);
            app.MapPost("/token", () => Results.Json(new { access_token = "acceptance-token", expires_in = 300 }));
            app.MapPost("/api/inventory/v1/branches/{branchId:guid}/reservations", fixture.ReserveAsync);
            app.MapPost("/api/kitchen/v1/tickets", fixture.CreateTicketAsync);
            await app.StartAsync();
            fixture.BaseAddress = new Uri(app.Urls.Single().TrimEnd('/') + "/");
            return fixture;
        }

        public void ArmInventoryHold(Guid orderId) => probes[orderId] = new Probe(holdInventory: true, holdKitchen: false, inventoryFailures: 0);
        public void ArmKitchenHold(Guid orderId) => probes[orderId] = new Probe(holdInventory: false, holdKitchen: true, inventoryFailures: 0);
        public void ArmInventoryFailures(Guid orderId, int failures) => probes[orderId] = new Probe(false, false, failures);
        public int InventoryAttempts(Guid orderId) => probes[orderId].InventoryAttempts;
        public int KitchenAttempts(Guid orderId) => probes[orderId].KitchenAttempts;
        public IReadOnlyCollection<string?> Correlations(Guid orderId) => probes[orderId].Correlations.ToArray();
        public Task WaitForInventoryAsync(Guid orderId, TimeSpan timeout) => probes[orderId].InventoryObserved.Task.WaitAsync(timeout);
        public Task WaitForKitchenAsync(Guid orderId, TimeSpan timeout) => probes[orderId].KitchenObserved.Task.WaitAsync(timeout);

        private async Task<IResult> ReserveAsync(Guid branchId, HttpRequest request)
        {
            using JsonDocument body = await JsonDocument.ParseAsync(request.Body, cancellationToken: request.HttpContext.RequestAborted);
            Guid orderId = ReadGuid(body.RootElement, "orderId");
            Probe probe = probes[orderId];
            int attempt = Interlocked.Increment(ref probe.InventoryAttempts);
            probe.Correlations.Enqueue(request.Headers["X-Correlation-ID"].FirstOrDefault());
            if (probe.HoldInventory && attempt == 1)
            {
                probe.InventoryObserved.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, request.HttpContext.RequestAborted);
            }
            if (attempt <= probe.InventoryFailures) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            return Results.Json(new { probe.ReservationId, OrderId = orderId, BranchId = branchId, Lines = Array.Empty<object>() });
        }

        private async Task<IResult> CreateTicketAsync(HttpRequest request)
        {
            using JsonDocument body = await JsonDocument.ParseAsync(request.Body, cancellationToken: request.HttpContext.RequestAborted);
            Guid orderId = ReadGuid(body.RootElement, "orderId");
            Probe probe = probes[orderId];
            int attempt = Interlocked.Increment(ref probe.KitchenAttempts);
            probe.Correlations.Enqueue(request.Headers["X-Correlation-ID"].FirstOrDefault());
            if (probe.HoldKitchen && attempt == 1)
            {
                probe.KitchenObserved.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, request.HttpContext.RequestAborted);
            }
            return Results.Json(new { probe.TicketId });
        }

        private static Guid ReadGuid(JsonElement value, string name)
        {
            JsonProperty property = value.EnumerateObject().Single(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            return property.Value.GetGuid();
        }

        public async ValueTask DisposeAsync() => await app.DisposeAsync();

        private sealed class Probe(bool holdInventory, bool holdKitchen, int inventoryFailures)
        {
            public bool HoldInventory { get; } = holdInventory;
            public bool HoldKitchen { get; } = holdKitchen;
            public int InventoryFailures { get; } = inventoryFailures;
            public Guid ReservationId { get; } = Guid.NewGuid();
            public Guid TicketId { get; } = Guid.NewGuid();
            public int InventoryAttempts;
            public int KitchenAttempts;
            public ConcurrentQueue<string?> Correlations { get; } = new();
            public TaskCompletionSource<bool> InventoryObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<bool> KitchenObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}

public sealed class OrderWorkflowRecoveryLiveFactAttribute : FactAttribute
{
    public OrderWorkflowRecoveryLiveFactAttribute()
    {
        string? environment = Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        string connection = Environment.GetEnvironmentVariable("NEXACONNECT_ORDER_INTEGRATION_DB") ?? string.Empty;
        bool safeConnection = false;
        string rabbit = Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_INTEGRATION_URI") ?? string.Empty;
        bool safeRabbit = Uri.TryCreate(rabbit, UriKind.Absolute, out Uri? rabbitUri)
            && rabbitUri.Scheme == "amqp" && rabbitUri.Host is "127.0.0.1" or "localhost";
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connection);
            safeConnection = builder.Database == "order_recovery" && builder.Host is "127.0.0.1" or "localhost";
        }
        catch { }
        if (Environment.GetEnvironmentVariable("NEXACONNECT_ORDER_RECOVERY_LIVE_ACCEPTANCE") != "1"
            || environment is not ("Development" or "Test" or "Testing")
            || !safeConnection
            || !safeRabbit
            || !File.Exists(Environment.GetEnvironmentVariable("NEXACONNECT_ORDER_RECOVERY_HOST_DLL"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NEXACONNECT_ORDER_RECOVERY_EVIDENCE_PATH")))
            Skip = "Order recovery live acceptance requires its explicit opt-in, generated loopback database/broker, child host assembly, evidence path, and safe environment.";
    }
}
