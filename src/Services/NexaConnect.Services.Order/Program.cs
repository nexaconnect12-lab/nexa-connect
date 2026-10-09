using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Order.Application.Orders;
using NexaConnect.Services.Order.Application.Workflow;
using NexaConnect.Infrastructure.Messaging;
using NexaConnect.Services.Order.Infrastructure.Messaging;
using NexaConnect.Services.Order.Infrastructure.Clients;
using NexaConnect.Services.Order.Infrastructure.Persistence;
using Npgsql;
using NexaConnect.Infrastructure.Http;
using NexaConnect.Services.Order.Application.Tenant;
using NexaConnect.Services.Order.Infrastructure;
using NexaConnect.Infrastructure.Authorization;
using NexaConnect.Observability;
using NexaConnect.Services.Order.Application.PaymentReviews;
using NexaConnect.Services.Order.Application.ManualTenders;
using NexaConnect.Services.Order.Application.Cancellations;

var builder = WebApplication.CreateBuilder(args);
builder.AddNexaConnectObservability("nexaconnect-order");
NexaConnect.Infrastructure.Authentication.AuthenticationServiceCollectionExtensions.EnsureProductionHttps(builder.Configuration, builder.Environment);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient("keycloak-token");
builder.Services.AddHttpClient<OrderWorkloadTokenProvider>();
builder.Services.AddHttpClient("OrderPlatformDirectory", client => client.BaseAddress = new Uri(builder.Configuration["Services:PlatformDirectory"] ?? throw new InvalidOperationException("Services:PlatformDirectory is required."))).AddNexaConnectCorrelationPropagation();
builder.Services.AddHttpClient("OrderRestaurant", client => client.BaseAddress = new Uri(builder.Configuration["Services:Restaurant"] ?? throw new InvalidOperationException("Services:Restaurant is required."))).AddNexaConnectCorrelationPropagation();
builder.Services.AddHttpClient<ProductAuthorizationClient>(client => client.BaseAddress = new Uri(builder.Configuration["Services:Authorization"] ?? throw new InvalidOperationException("Services:Authorization is required."))).AddNexaConnectCorrelationPropagation();
builder.Services.AddScoped<IOrderTenantAuthorizer, HttpOrderTenantAuthorizer>();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddOptions<CardCheckoutOptions>().Bind(builder.Configuration.GetSection("CardCheckout"))
    .Validate(options => !options.EnableOmiseTestCheckout || builder.Environment.IsDevelopment()
        || builder.Environment.IsEnvironment("Testing"), "Omise test checkout requires Development or Testing.").ValidateOnStart();
builder.Services.AddNexaConnectApiAuthentication(builder.Configuration);
builder.Services.AddNexaConnectDataProtection(builder.Configuration, builder.Environment, "order");
var usePostgres = builder.Configuration.GetValue<string>("Persistence:Provider")?.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase) == true;
if (usePostgres)
{
    var connectionString = builder.Configuration.GetConnectionString("Order") ?? throw new InvalidOperationException("ConnectionStrings:Order is required for PostgreSQL persistence.");
    builder.Services.AddSingleton(new NpgsqlDataSourceBuilder(connectionString).Build());
    builder.Services.AddSingleton<PostgresOrderRepository>();
    builder.Services.AddSingleton<PostgresOrderCancellationRepository>();
    builder.Services.AddSingleton<IOrderCancellationRepository>(services => services.GetRequiredService<PostgresOrderCancellationRepository>());
    builder.Services.AddSingleton<IOrderRepository>(services => services.GetRequiredService<PostgresOrderRepository>());
    builder.Services.AddSingleton<IOrderWorkflowRecoveryRepository>(services => services.GetRequiredService<PostgresOrderRepository>());
    builder.Services.AddSingleton<IManualTenderRepository>(services => services.GetRequiredService<PostgresOrderRepository>());
    builder.Services.AddSingleton<IOrderApplicationService, PostgresOrderApplicationService>();
    builder.Services.Configure<OrderOperationalMetricsOptions>(builder.Configuration.GetSection("OperationalMetrics"));
    builder.Services.AddHostedService<PaymentReconciliationOperationalMetricsWorker>();
}
else
{
    builder.Services.AddSingleton<InMemoryOrderApplicationService>();
    builder.Services.AddSingleton<IOrderApplicationService>(services => services.GetRequiredService<InMemoryOrderApplicationService>());
    builder.Services.AddSingleton<IOrderRepository>(services => services.GetRequiredService<InMemoryOrderApplicationService>());
    builder.Services.AddSingleton<IOrderCancellationRepository, InMemoryOrderCancellationRepository>();
}
builder.Services.AddSingleton<InMemoryIntegrationEventPublisher>();
builder.Services.AddSingleton<IIntegrationEventPublisher>(services =>
    services.GetRequiredService<InMemoryIntegrationEventPublisher>());
builder.Services.AddScoped<PlaceOrderWorkflow>();
builder.Services.AddScoped<OrderPricingService>();
builder.Services.AddScoped<OrderReceiptService>();
builder.Services.AddSingleton<IOrderReceiptRepository>(services => (IOrderReceiptRepository)services.GetRequiredService<IOrderRepository>());
builder.Services.AddScoped<OrderWorkflowRecoveryService>();
builder.Services.AddScoped<PaymentReconciliationApplicationService>();
builder.Services.AddScoped<PaymentReviewApplicationService>();
builder.Services.AddScoped<ManualTenderApplicationService>();
builder.Services.AddScoped<OrderCancellationApplicationService>();
if (usePostgres)
{
    builder.Services.AddPostgresOutbox(builder.Configuration, "Order");
    builder.Services.AddSingleton<IIntegrationEventPublisher, PostgresIntegrationEventPublisher>();
}
if (builder.Configuration.GetValue<bool>("PaymentReconciliationConsumer:Enabled"))
{
    if (!usePostgres || !builder.Configuration.GetValue<bool>("Workflow:UseHttpAdapters"))
        throw new InvalidOperationException("Payment reconciliation consumption requires PostgreSQL Order persistence and HTTP workflow adapters.");
    builder.Services.AddPaymentReconciliationConsumer(builder.Configuration);
}
builder.Services.AddTransient<OutboundTokenHandler>();
builder.Services.AddSingleton<IOutboundAccessTokenProvider, KeycloakClientCredentialsTokenProvider>();
builder.Services.AddHttpClient<IBranchPricingPort, HttpBranchPricingPort>(client =>
    client.BaseAddress = new Uri(builder.Configuration["Services:Restaurant"] ?? throw new InvalidOperationException("Services:Restaurant is required.")))
    .AddNexaConnectCorrelationPropagation().AddHttpMessageHandler<OutboundTokenHandler>();
if (builder.Configuration.GetValue<bool>("Workflow:UseHttpAdapters"))
{
    builder.Services.AddTransient<RetryingHttpMessageHandler>();
    builder.Services.AddHttpClient("keycloak-token");
    builder.Services.AddHttpClient<IMenuCatalogPort, HttpMenuCatalogPort>(client =>
        client.BaseAddress = new Uri(builder.Configuration["Services:Catalog"] ?? throw new InvalidOperationException("Services:Catalog is required.")))
        .AddNexaConnectCorrelationPropagation().AddHttpMessageHandler<OutboundTokenHandler>().AddHttpMessageHandler<RetryingHttpMessageHandler>();
    builder.Services.AddHttpClient<IInventoryReservationPort, HttpInventoryReservationPort>(client =>
        client.BaseAddress = new Uri(builder.Configuration["Services:Inventory"] ?? throw new InvalidOperationException("Services:Inventory is required.")))
        .AddNexaConnectCorrelationPropagation().AddHttpMessageHandler<OutboundTokenHandler>().AddHttpMessageHandler<RetryingHttpMessageHandler>();
    builder.Services.AddHttpClient<IKitchenPort, HttpKitchenPort>(client =>
        client.BaseAddress = new Uri(builder.Configuration["Services:Kitchen"] ?? throw new InvalidOperationException("Services:Kitchen is required.")))
        .AddNexaConnectCorrelationPropagation().AddHttpMessageHandler<OutboundTokenHandler>().AddHttpMessageHandler<RetryingHttpMessageHandler>();
    builder.Services.AddHttpClient<IPaymentPort, HttpPaymentPort>(client =>
        client.BaseAddress = new Uri(builder.Configuration["Services:Payment"] ?? throw new InvalidOperationException("Services:Payment is required.")))
        .AddNexaConnectCorrelationPropagation().AddHttpMessageHandler<OutboundTokenHandler>();
}
if (builder.Configuration.GetValue<bool>("WorkflowRecovery:Enabled"))
{
    if (!usePostgres || !builder.Configuration.GetValue<bool>("Workflow:UseHttpAdapters"))
        throw new InvalidOperationException("Order workflow recovery requires PostgreSQL persistence and HTTP workflow adapters.");
    builder.Services.AddOptions<OrderWorkflowRecoveryOptions>()
        .Bind(builder.Configuration.GetSection("WorkflowRecovery"))
        .Validate(value => value.PollInterval > TimeSpan.Zero
            && value.LeaseDuration > TimeSpan.Zero
            && value.RetryDelay > TimeSpan.Zero,
            "Workflow recovery poll, lease, and retry durations must be positive.")
        .ValidateOnStart();
    builder.Services.AddHostedService<OrderWorkflowRecoveryWorker>();
}

if (builder.Configuration.GetValue<string>("Persistence:Provider")?.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase) == true)
{
    builder.Services.AddScoped<NexaConnect.Services.Order.Application.Orders.IOrderDayReader, NexaConnect.Services.Order.Infrastructure.Persistence.PostgresOrderDayReader>();
}
builder.Services.AddScoped<NexaConnect.Services.Order.Application.Orders.OrderDayRead>();

builder.Services.AddScoped<NexaConnect.Services.Order.Application.Orders.OrderCutoffs>();
if (usePostgres) builder.Services.AddScoped<NexaConnect.Services.Order.Application.Orders.IOrderCutoffStore, NexaConnect.Services.Order.Infrastructure.Persistence.PostgresOrderCutoffStore>();
builder.Services.AddScoped<NexaConnect.Services.Order.Application.Orders.OrderDayFences>();
if (builder.Configuration["Persistence:Provider"]?.Equals("PostgreSQL",StringComparison.OrdinalIgnoreCase)==true) builder.Services.AddScoped<NexaConnect.Services.Order.Application.Orders.IOrderDayFenceStore,NexaConnect.Services.Order.Infrastructure.Persistence.PostgresOrderDayFenceStore>();
builder.Services.AddScoped<NexaConnect.Services.Order.Application.Orders.IOrderDayBarrierStore, NexaConnect.Services.Order.Infrastructure.Persistence.PostgresOrderDayBarrierStore>();
builder.Services.AddScoped<NexaConnect.Services.Order.Application.Orders.OrderDayBarriers>();
builder.Services.AddScoped<NexaConnect.Services.Order.Infrastructure.Messaging.LatePaymentReconciliation>();
builder.Services.AddScoped<NexaConnect.Services.Order.Application.Orders.IOrderLateWorkStore,NexaConnect.Services.Order.Infrastructure.Persistence.PostgresOrderLateWorkStore>();
builder.Services.AddScoped<NexaConnect.Services.Order.Application.Orders.OrderLateWork>();
var app = builder.Build();

app.UseNexaConnectRequestLogging();
app.Use(async (context,next)=>
{
    try { await next(); }
    catch(Npgsql.PostgresException e)when(e.SqlState=="PDS01"||e.SqlState=="P0001"&&e.MessageText=="financial_day_fenced")
    {
        var code=e.SqlState=="PDS01"?"financial_day_barrier":"financial_day_fenced";
        context.Response.StatusCode=409;context.Response.Headers.CacheControl="no-store";
        app.Logger.LogWarning("Financial source mutation rejected; code {Code}",code);
        await context.Response.WriteAsJsonAsync(new{code});
    }
});
app.Use(async (context,next) => { if(context.Request.Path.StartsWithSegments("/api/order/v1/customer/late-work")||context.Request.Path.StartsWithSegments("/api/order/v1/customer/day-cutoffs")||context.Request.Path.StartsWithSegments("/api/order/v1/internal/day-settlement-barriers")) context.Response.Headers.CacheControl="no-store"; await next(); });
app.Use(async (context, next) => { if (context.Request.Path.StartsWithSegments("/api/order/v1/customer/end-of-day", StringComparison.OrdinalIgnoreCase)) context.Response.Headers.CacheControl = "no-store"; await next(context); });

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public sealed class OrderProgram;
