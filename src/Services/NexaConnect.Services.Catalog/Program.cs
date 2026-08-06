using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Catalog.Application.Menu;
using NexaConnect.Services.Catalog.Infrastructure;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddNexaConnectApiAuthentication(builder.Configuration);
if (builder.Configuration.GetValue<string>("Persistence:Provider")?.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase) == true)
{
    var dataSource = new NpgsqlDataSourceBuilder(builder.Configuration.GetConnectionString("Catalog") ?? throw new InvalidOperationException("ConnectionStrings:Catalog is required.")).Build();
    builder.Services.AddSingleton(dataSource);
    builder.Services.AddSingleton<IMenuCatalog, PostgresMenuCatalog>();
}
else builder.Services.AddSingleton<IMenuCatalog, InMemoryMenuCatalog>();

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
