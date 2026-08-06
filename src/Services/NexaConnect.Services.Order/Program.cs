using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Order.Application.Orders;
using NexaConnect.Services.Order.Application.Workflow;
using NexaConnect.Infrastructure.Messaging;
using NexaConnect.Services.Order.Infrastructure.Messaging;
using NexaConnect.Services.Order.Infrastructure.Clients;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddNexaConnectApiAuthentication(builder.Configuration);
builder.Services.AddSingleton<InMemoryOrderApplicationService>();
builder.Services.AddSingleton<IOrderApplicationService>(services => services.GetRequiredService<InMemoryOrderApplicationService>());
builder.Services.AddSingleton<IOrderRepository>(services => services.GetRequiredService<InMemoryOrderApplicationService>());
builder.Services.AddSingleton<InMemoryIntegrationEventPublisher>();
builder.Services.AddSingleton<IIntegrationEventPublisher>(services =>
    services.GetRequiredService<InMemoryIntegrationEventPublisher>());
builder.Services.AddScoped<PlaceOrderWorkflow>();
if (builder.Configuration.GetValue<string>("Persistence:Provider")?.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase) == true)
{
    builder.Services.AddPostgresOutbox(builder.Configuration, "Order");
    builder.Services.AddSingleton<IIntegrationEventPublisher, PostgresIntegrationEventPublisher>();
}
if (builder.Configuration.GetValue<bool>("Workflow:UseHttpAdapters"))
{
    builder.Services.AddHttpClient<IMenuCatalogPort, HttpMenuCatalogPort>(client =>
        client.BaseAddress = new Uri(builder.Configuration["Services:Catalog"] ?? throw new InvalidOperationException("Services:Catalog is required.")));
    builder.Services.AddHttpClient<IInventoryReservationPort, HttpInventoryReservationPort>(client =>
        client.BaseAddress = new Uri(builder.Configuration["Services:Inventory"] ?? throw new InvalidOperationException("Services:Inventory is required.")));
    builder.Services.AddHttpClient<IKitchenPort, HttpKitchenPort>(client =>
        client.BaseAddress = new Uri(builder.Configuration["Services:Kitchen"] ?? throw new InvalidOperationException("Services:Kitchen is required.")));
    builder.Services.AddHttpClient<IPaymentPort, HttpPaymentPort>(client =>
        client.BaseAddress = new Uri(builder.Configuration["Services:Payment"] ?? throw new InvalidOperationException("Services:Payment is required.")));
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
