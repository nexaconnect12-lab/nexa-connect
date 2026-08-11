using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Infrastructure.Messaging;
using NexaConnect.Services.Inventory.Application.Reservations;
using NexaConnect.Services.Inventory.Infrastructure;
using NexaConnect.Services.Inventory.Application.Tenant;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
NexaConnect.Infrastructure.Authentication.AuthenticationServiceCollectionExtensions.EnsureProductionHttps(builder.Configuration, builder.Environment);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<IServiceWorkloadTokenProvider, ServiceWorkloadTokenProvider>();
builder.Services.AddHttpClient("InventoryPlatformDirectory", client => client.BaseAddress = new Uri(
    builder.Configuration["Services:PlatformDirectory"] ?? throw new InvalidOperationException("Services:PlatformDirectory is required.")));
builder.Services.AddHttpClient("InventoryRestaurant", client => client.BaseAddress = new Uri(
    builder.Configuration["Services:Restaurant"] ?? throw new InvalidOperationException("Services:Restaurant is required.")));
builder.Services.AddScoped<IInventoryTenantAuthorizer, HttpInventoryTenantAuthorizer>();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddNexaConnectApiAuthentication(builder.Configuration);
builder.Services.AddNexaConnectDataProtection(builder.Configuration, builder.Environment, "inventory");
if (builder.Configuration.GetValue<string>("Persistence:Provider")?.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase) == true)
{
    var dataSource = new NpgsqlDataSourceBuilder(builder.Configuration.GetConnectionString("Inventory") ?? throw new InvalidOperationException("ConnectionStrings:Inventory is required.")).Build();
    builder.Services.AddSingleton(dataSource);
    builder.Services.AddSingleton<IInventoryReservations, PostgresInventoryReservations>();
    builder.Services.AddPostgresInbox(builder.Configuration, "Inventory");
}
else builder.Services.AddSingleton<IInventoryReservations, InMemoryInventoryReservations>();

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

public sealed class InventoryProgram;
