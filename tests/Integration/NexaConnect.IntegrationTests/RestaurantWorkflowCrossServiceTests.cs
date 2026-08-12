extern alias CATALOG;
extern alias INVENTORY;
extern alias ORDER;
extern alias PAYMENT;
extern alias KITCHEN;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using CatalogProgram = CATALOG::CatalogProgram;
using CatalogCreateMenuItem = CATALOG::NexaConnect.Services.Catalog.Application.Menu.CreateMenuItem;
using InventoryProgram = INVENTORY::InventoryProgram;
using InventoryStockItem = INVENTORY::NexaConnect.Services.Inventory.Application.Reservations.StockItem;
using OrderProgram = ORDER::OrderProgram;
using OrderMenuPort = ORDER::NexaConnect.Services.Order.Application.Workflow.IMenuCatalogPort;
using OrderInventoryPort = ORDER::NexaConnect.Services.Order.Application.Workflow.IInventoryReservationPort;
using OrderKitchenPort = ORDER::NexaConnect.Services.Order.Application.Workflow.IKitchenPort;
using OrderPaymentPort = ORDER::NexaConnect.Services.Order.Application.Workflow.IPaymentPort;
using OrderTenantAuthorizer = ORDER::NexaConnect.Services.Order.Application.Tenant.IOrderTenantAuthorizer;
using OrderMenuAdapter = ORDER::NexaConnect.Services.Order.Infrastructure.Clients.HttpMenuCatalogPort;
using OrderInventoryAdapter = ORDER::NexaConnect.Services.Order.Infrastructure.Clients.HttpInventoryReservationPort;
using OrderKitchenAdapter = ORDER::NexaConnect.Services.Order.Infrastructure.Clients.HttpKitchenPort;
using OrderPaymentAdapter = ORDER::NexaConnect.Services.Order.Infrastructure.Clients.HttpPaymentPort;
using PaymentProgram = PAYMENT::PaymentProgram;
using PaymentIntents = PAYMENT::NexaConnect.Services.Payment.Application.Intents.IPaymentIntents;
using PaymentIntent = PAYMENT::NexaConnect.Services.Payment.Application.Intents.PaymentIntent;
using CreatePaymentIntent = PAYMENT::NexaConnect.Services.Payment.Application.Intents.CreatePaymentIntent;
using KitchenProgram = KITCHEN::KitchenProgram;
using KitchenStore = KITCHEN::NexaConnect.Services.Kitchen.Application.IKitchenTicketStore;
using KitchenTicket = KITCHEN::NexaConnect.Services.Kitchen.Application.KitchenTicket;
using CreateKitchenTicket = KITCHEN::NexaConnect.Services.Kitchen.Application.CreateKitchenTicket;
using KitchenTicketStatus = KITCHEN::NexaConnect.Services.Kitchen.Application.KitchenTicketStatus;

namespace NexaConnect.IntegrationTests;

public sealed class RestaurantWorkflowCrossServiceTests : IClassFixture<RestaurantWorkflowServiceFixture>
{
    private readonly RestaurantWorkflowServiceFixture fixture;

    public RestaurantWorkflowCrossServiceTests(RestaurantWorkflowServiceFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task Catalog_order_inventory_kitchen_payment_complete_over_http()
    {
        fixture.Reset();
        using HttpClient catalog = fixture.Catalog.CreateClient();
        using HttpClient inventory = fixture.Inventory.CreateClient();
        using HttpClient order = fixture.Order.CreateClient();

        Guid productId = Guid.NewGuid();
        await EnsureSuccess(catalog.PostAsJsonAsync(
            $"/api/catalog/v1/branches/{RestaurantWorkflowServiceFixture.BranchId:D}/menu-items",
            new CatalogCreateMenuItem(productId, "E2E Burger", 12.50m, "USD", "grill")));
        await EnsureSuccess(inventory.PutAsJsonAsync(
            $"/api/inventory/v1/branches/{RestaurantWorkflowServiceFixture.BranchId:D}/stock/{productId:D}",
            new { quantity = 5m }));

        string idempotencyKey = $"e2e-{Guid.NewGuid():N}";
        HttpResponseMessage placed = await order.PostAsJsonAsync(
            "/api/order/v1/workflows/place",
            new
            {
                restaurantId = RestaurantWorkflowServiceFixture.RestaurantId,
                organizationId = RestaurantWorkflowServiceFixture.OrganizationId,
                branchId = RestaurantWorkflowServiceFixture.BranchId,
                currency = "USD",
                paymentMethod = "cash",
                idempotencyKey,
                lines = new[] { new { productId, quantity = 1 } }
            });

        Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
        WorkflowResult result = (await placed.Content.ReadFromJsonAsync<WorkflowResult>())!;
        Assert.Equal(4, result.Status);
        Assert.Equal(12.50m, result.TotalAmount);

        HttpResponseMessage orderRead = await order.GetAsync($"/api/order/v1/orders/{result.OrderId:D}");
        Assert.Equal(HttpStatusCode.OK, orderRead.StatusCode);
        OrderResponse persisted = (await orderRead.Content.ReadFromJsonAsync<OrderResponse>())!;
        Assert.Equal("Paid", persisted.Status);
        Assert.Equal(12.50m, persisted.TotalAmount);

        IReadOnlyCollection<InventoryStockItem> stock = (await inventory.GetFromJsonAsync<IReadOnlyCollection<InventoryStockItem>>(
            $"/api/inventory/v1/branches/{RestaurantWorkflowServiceFixture.BranchId:D}/stock"))!;
        Assert.Equal(4m, Assert.Single(stock).AvailableQuantity);
        Assert.True(fixture.Kitchen.WasCreated(result.OrderId));
        Assert.Equal(result.OrderId, fixture.Payments.Intents.LastIntent?.OrderId);
        Assert.Equal(1, fixture.Payments.Intents.CreateCount);

        // Durable idempotency replay is covered by OrderOutboxReplayPersistenceTests;
        // this test focuses on the cross-service HTTP path and downstream effects.
    }

    private static async Task EnsureSuccess(Task<HttpResponseMessage> responseTask)
    {
        using HttpResponseMessage response = await responseTask;
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private sealed record WorkflowResult(Guid OrderId, int Status, decimal TotalAmount, string Currency);
    private sealed record OrderResponse(Guid OrderId, Guid OrganizationId, Guid BranchId, string Status, decimal TotalAmount,
        string Currency, IReadOnlyCollection<object> Lines);
}

public sealed class RestaurantWorkflowServiceFixture : IDisposable
{
    public static readonly Guid OrganizationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid RestaurantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid BranchId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public CatalogFactory Catalog { get; } = new();
    public InventoryFactory Inventory { get; } = new();
    public KitchenFactory Kitchen { get; } = new();
    public PaymentFactory Payments { get; } = new();
    public OrderFactory Order { get; }

    public RestaurantWorkflowServiceFixture()
    {
        Order = new OrderFactory(Catalog, Inventory, Kitchen, Payments);
    }

    public void Reset()
    {
        Kitchen.Reset();
        Payments.Reset();
    }

    public void Dispose()
    {
        Order.Dispose();
        Payments.Dispose();
        Kitchen.Dispose();
        Inventory.Dispose();
        Catalog.Dispose();
    }
}

public sealed class CatalogFactory : WebApplicationFactory<CatalogProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => TestServiceConfiguration.Configure(builder, "catalog");
}

public sealed class InventoryFactory : WebApplicationFactory<InventoryProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        TestServiceConfiguration.Configure(builder, "inventory");
    }
}

public sealed class PaymentFactory : WebApplicationFactory<PaymentProgram>
{
    internal RecordingPaymentIntents Intents { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        TestServiceConfiguration.Configure(builder, "payment");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<PaymentIntents>();
            services.AddSingleton<PaymentIntents>(Intents);
        });
    }

    internal RecordingPaymentIntents LastIntentStore => Intents;
    internal void Reset() => Intents.Reset();
}

public sealed class OrderFactory : WebApplicationFactory<OrderProgram>
{
    private readonly CatalogFactory catalog;
    private readonly InventoryFactory inventory;
    private readonly KitchenFactory kitchen;
    private readonly PaymentFactory payment;

    public OrderFactory(CatalogFactory catalog, InventoryFactory inventory, KitchenFactory kitchen, PaymentFactory payment)
    {
        this.catalog = catalog;
        this.inventory = inventory;
        this.kitchen = kitchen;
        this.payment = payment;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        TestServiceConfiguration.Configure(builder, "order", new Dictionary<string, string?>
        {
            ["Workflow:UseHttpAdapters"] = "true",
            ["Workflow:RestaurantId"] = RestaurantWorkflowServiceFixture.RestaurantId.ToString(),
            ["Workflow:BranchId"] = RestaurantWorkflowServiceFixture.BranchId.ToString(),
            ["Services:Catalog"] = "http://catalog.test/",
            ["Services:Inventory"] = "http://inventory.test/",
            ["Services:Kitchen"] = "http://kitchen.test/",
            ["Services:Payment"] = "http://payment.test/",
            ["Authentication:OutboundToken"] = "integration-test-token"
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<OrderMenuPort>();
            services.RemoveAll<OrderInventoryPort>();
            services.RemoveAll<OrderKitchenPort>();
            services.RemoveAll<OrderPaymentPort>();
            services.RemoveAll<OrderTenantAuthorizer>();
            services.AddSingleton<OrderTenantAuthorizer, DenyOrderTenantAuthorizer>();
            services.AddHttpClient<OrderMenuPort, OrderMenuAdapter>(client => client.BaseAddress = new Uri("http://catalog.test/"))
                .ConfigurePrimaryHttpMessageHandler(_ => new ForwardingHandler(catalog.CreateClient));
            services.AddHttpClient<OrderInventoryPort, OrderInventoryAdapter>(client => client.BaseAddress = new Uri("http://inventory.test/"))
                .ConfigurePrimaryHttpMessageHandler(_ => new ForwardingHandler(inventory.CreateClient));
            services.AddHttpClient<OrderKitchenPort, OrderKitchenAdapter>(client => client.BaseAddress = new Uri("http://kitchen.test/"))
                .ConfigurePrimaryHttpMessageHandler(_ => new ForwardingHandler(kitchen.CreateClient));
            services.AddHttpClient<OrderPaymentPort, OrderPaymentAdapter>(client => client.BaseAddress = new Uri("http://payment.test/"))
                .ConfigurePrimaryHttpMessageHandler(_ => new ForwardingHandler(payment.CreateClient));
        });
    }
}

internal static class TestServiceConfiguration
{
    public static void Configure(IWebHostBuilder builder, string service, IReadOnlyDictionary<string, string?>? values = null)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["Authentication:Authority"] = "https://identity.tests/realms/nexa-test",
                ["Authentication:Audience"] = "nexaconnect-api",
                ["Authentication:RequireHttpsMetadata"] = "false",
                ["Persistence:Provider"] = "InMemory",
                ["ConnectionStrings:Catalog"] = "Host=localhost;Database=unused",
                ["ConnectionStrings:Inventory"] = "Host=localhost;Database=unused",
                ["ConnectionStrings:Payment"] = "Host=localhost;Database=unused",
                ["ConnectionStrings:Order"] = "Host=localhost;Database=unused"
            };
            if (values is not null)
                foreach (var entry in values) settings[entry.Key] = entry.Value;
            configuration.AddInMemoryCollection(settings);
        });
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthenticationHandler.Scheme;
                options.DefaultChallengeScheme = TestAuthenticationHandler.Scheme;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.Scheme, _ => { });
        });
    }
}

internal sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public new const string Scheme = "IntegrationTest";

    public TestAuthenticationHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder) : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new List<Claim> { new("sub", "integration-test-user") };
        if (!Request.Headers.TryGetValue("Authorization", out var authorization)
            || string.Equals(authorization.ToString(), "Bearer integration-test-token", StringComparison.Ordinal))
            claims.Add(new Claim("azp", "nexaconnect-order-service"));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme)), Scheme)));
    }
}

internal sealed class ForwardingHandler(Func<HttpClient> clientFactory) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpClient client = clientFactory();
        using var forwarded = new HttpRequestMessage(request.Method,
            new Uri(client.BaseAddress!, request.RequestUri!.PathAndQuery));
        foreach (var header in request.Headers)
            forwarded.Headers.TryAddWithoutValidation(header.Key, header.Value);
        if (request.Content is not null)
        {
            forwarded.Content = new StreamContent(await request.Content.ReadAsStreamAsync(cancellationToken));
            foreach (var header in request.Content.Headers)
                forwarded.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        return await client.SendAsync(forwarded, cancellationToken);
    }
}

public sealed class KitchenFactory : WebApplicationFactory<KitchenProgram>
{
    internal RecordingKitchenStore Store { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        TestServiceConfiguration.Configure(builder, "kitchen");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<KitchenStore>();
            services.AddSingleton<KitchenStore>(Store);
        });
    }

    public bool WasCreated(Guid orderId) => Store.WasCreated(orderId);
    public int CreatedCount => Store.CreatedCount;
    public void Reset() => Store.Reset();
}

internal sealed class RecordingKitchenStore : KitchenStore
{
    private readonly ConcurrentDictionary<Guid, KitchenTicket> tickets = new();

    public Task<KitchenTicket> CreateAsync(CreateKitchenTicket command, CancellationToken cancellationToken)
    {
        KitchenTicket ticket = new(Guid.NewGuid(), command.OrderId, command.BranchId,
            KitchenTicketStatus.Queued, DateTimeOffset.UtcNow, []);
        tickets[ticket.TicketId] = ticket;
        return Task.FromResult(ticket);
    }

    public Task<KitchenTicket?> GetAsync(Guid ticketId, CancellationToken cancellationToken) =>
        Task.FromResult(tickets.TryGetValue(ticketId, out KitchenTicket? ticket) ? ticket : null);

    public Task<bool> CancelAsync(Guid orderId, CancellationToken cancellationToken)
    {
        foreach ((Guid ticketId, KitchenTicket ticket) in tickets)
        {
            if (ticket.OrderId != orderId) continue;
            tickets[ticketId] = ticket with { Status = KitchenTicketStatus.Cancelled };
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public bool WasCreated(Guid orderId) => tickets.Values.Any(ticket => ticket.OrderId == orderId);
    public int CreatedCount => tickets.Count;
    public void Reset() => tickets.Clear();
}

internal sealed class RecordingPaymentIntents : PaymentIntents
{
    private readonly ConcurrentDictionary<Guid, PaymentIntent> intents = new();
    public PaymentIntent? LastIntent { get; private set; }
    public int CreateCount { get; private set; }

    public PaymentIntent Create(CreatePaymentIntent command)
    {
        LastIntent = new PaymentIntent(Guid.NewGuid(), command.RestaurantId, command.BranchId, command.OrderId,
            command.Amount, command.Currency.ToUpperInvariant(), command.PaymentMethod.ToLowerInvariant(), "pending", DateTimeOffset.UtcNow);
        intents[LastIntent.Id] = LastIntent;
        CreateCount++;
        return LastIntent;
    }

    public PaymentIntent? Get(Guid id) => intents.GetValueOrDefault(id);
    public void Reset()
    {
        intents.Clear();
        LastIntent = null;
        CreateCount = 0;
    }
}
