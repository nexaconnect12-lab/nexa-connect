using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Order.Application.Orders;
using NexaConnect.Services.Order.Application.Workflow;
using NexaConnect.Infrastructure.Messaging;
using NexaConnect.Services.Order.Infrastructure.Messaging;
using NexaConnect.Services.Order.Infrastructure.Clients;
using NexaConnect.Services.Order.Infrastructure.Persistence;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddNexaConnectApiAuthentication(builder.Configuration);
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
if (usePostgres)
{
    builder.Services.AddPostgresOutbox(builder.Configuration, "Order");
    builder.Services.AddSingleton<IIntegrationEventPublisher, PostgresIntegrationEventPublisher>();
}
if (builder.Configuration.GetValue<bool>("Workflow:UseHttpAdapters"))
{
    builder.Services.AddTransient<OutboundTokenHandler>();
    builder.Services.AddHttpClient<IMenuCatalogPort, HttpMenuCatalogPort>(client =>
        client.BaseAddress = new Uri(builder.Configuration["Services:Catalog"] ?? throw new InvalidOperationException("Services:Catalog is required.")))
        .AddHttpMessageHandler<OutboundTokenHandler>();
    builder.Services.AddHttpClient<IInventoryReservationPort, HttpInventoryReservationPort>(client =>
        client.BaseAddress = new Uri(builder.Configuration["Services:Inventory"] ?? throw new InvalidOperationException("Services:Inventory is required.")))
        .AddHttpMessageHandler<OutboundTokenHandler>();
    builder.Services.AddHttpClient<IKitchenPort, HttpKitchenPort>(client =>
        client.BaseAddress = new Uri(builder.Configuration["Services:Kitchen"] ?? throw new InvalidOperationException("Services:Kitchen is required.")))
        .AddHttpMessageHandler<OutboundTokenHandler>();
    builder.Services.AddHttpClient<IPaymentPort, HttpPaymentPort>(client =>
        client.BaseAddress = new Uri(builder.Configuration["Services:Payment"] ?? throw new InvalidOperationException("Services:Payment is required.")))
        .AddHttpMessageHandler<OutboundTokenHandler>();
}

var app = builder.Build();

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
