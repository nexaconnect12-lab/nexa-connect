using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Customer.Application.Customers;
using NexaConnect.Services.Customer.Infrastructure;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
NexaConnect.Infrastructure.Authentication.AuthenticationServiceCollectionExtensions.EnsureProductionHttps(builder.Configuration, builder.Environment);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddNexaConnectApiAuthentication(builder.Configuration);
builder.Services.AddNexaConnectDevelopmentDataProtection(builder.Environment, "customer");
if (builder.Configuration.GetValue<string>("Persistence:Provider")?.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase) == true)
{
    builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Customer")
        ?? throw new InvalidOperationException("ConnectionStrings:Customer is required.")));
    builder.Services.AddSingleton<ICustomers, PostgresCustomers>();
}
else
{
    builder.Services.AddSingleton<ICustomers, InMemoryCustomers>();
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
