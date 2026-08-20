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
builder.Services.AddNexaConnectApiAuthentication(builder.Configuration);
builder.Services.AddNexaConnectDataProtection(builder.Configuration, builder.Environment, "order");
var usePostgres = builder.Configuration.GetValue<string>("Persistence:Provider")?.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase) == true;
if (usePostgres)
{
    var connectionString = builder.Configuration.GetConnectionString("Order") ?? throw new InvalidOperationException("ConnectionStrings:Order is required for PostgreSQL persistence.");
    builder.Services.AddSingleton(new NpgsqlDataSourceBuilder(connectionString).Build());
    builder.Services.AddSingleton<PostgresOrderRepository>();
    builder.Services.AddSingleton<IOrderRepository>(services => services.GetRequiredService<PostgresOrderRepository>());
    builder.Services.AddSingleton<IOrderApplicationService, PostgresOrderApplicationService>();
}
else
{
    builder.Services.AddSingleton<InMemoryOrderApplicationService>();
    builder.Services.AddSingleton<IOrderApplicationService>(services => services.GetRequiredService<InMemoryOrderApplicationService>());
    builder.Services.AddSingleton<IOrderRepository>(services => services.GetRequiredService<InMemoryOrderApplicationService>());
}
builder.Services.AddSingleton<InMemoryIntegrationEventPublisher>();
builder.Services.AddSingleton<IIntegrationEventPublisher>(services =>
    services.GetRequiredService<InMemoryIntegrationEventPublisher>());
builder.Services.AddScoped<PlaceOrderWorkflow>();
builder.Services.AddScoped<PaymentReconciliationApplicationService>();
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
if (builder.Configuration.GetValue<bool>("Workflow:UseHttpAdapters"))
{
    builder.Services.AddTransient<OutboundTokenHandler>();
    builder.Services.AddTransient<RetryingHttpMessageHandler>();
    builder.Services.AddSingleton<IOutboundAccessTokenProvider, KeycloakClientCredentialsTokenProvider>();
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
        .AddNexaConnectCorrelationPropagation().AddHttpMessageHandler<OutboundTokenHandler>().AddHttpMessageHandler<RetryingHttpMessageHandler>();
}

var app = builder.Build();
app.UseNexaConnectRequestLogging();

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
