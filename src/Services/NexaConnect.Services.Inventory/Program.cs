using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Inventory.Application.Reservations;
using NexaConnect.Services.Inventory.Infrastructure;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
NexaConnect.Infrastructure.Authentication.AuthenticationServiceCollectionExtensions.EnsureProductionHttps(builder.Configuration, builder.Environment);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddNexaConnectApiAuthentication(builder.Configuration);
builder.Services.AddNexaConnectDevelopmentDataProtection(builder.Environment, "inventory");
if (builder.Configuration.GetValue<string>("Persistence:Provider")?.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase) == true)
{
    var dataSource = new NpgsqlDataSourceBuilder(builder.Configuration.GetConnectionString("Inventory") ?? throw new InvalidOperationException("ConnectionStrings:Inventory is required.")).Build();
    builder.Services.AddSingleton(dataSource);
    builder.Services.AddSingleton<IInventoryReservations, PostgresInventoryReservations>();
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
