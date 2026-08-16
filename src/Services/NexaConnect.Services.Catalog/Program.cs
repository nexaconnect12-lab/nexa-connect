using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Catalog.Application.Menu;
using NexaConnect.Services.Catalog.Application.Tenant;
using NexaConnect.Services.Catalog.Infrastructure;
using Npgsql;
using NexaConnect.Infrastructure.Authorization;
using NexaConnect.Observability;
using NexaConnect.Infrastructure.Messaging;

var builder = WebApplication.CreateBuilder(args);
builder.AddNexaConnectObservability("nexaconnect-catalog");
NexaConnect.Infrastructure.Authentication.AuthenticationServiceCollectionExtensions.EnsureProductionHttps(builder.Configuration, builder.Environment);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddMemoryCache();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddNexaConnectApiAuthentication(builder.Configuration);
builder.Services.AddNexaConnectDataProtection(builder.Configuration, builder.Environment, "catalog");
if (builder.Configuration.GetValue<string>("Persistence:Provider")?.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase) == true)
{
    var dataSource = new NpgsqlDataSourceBuilder(builder.Configuration.GetConnectionString("Catalog") ?? throw new InvalidOperationException("ConnectionStrings:Catalog is required.")).Build();
    builder.Services.AddSingleton(dataSource);
    builder.Services.AddSingleton<IMenuCatalog, PostgresMenuCatalog>();
    if (builder.Configuration.GetValue<bool>("Outbox:Enabled")) builder.Services.AddPostgresOutbox(builder.Configuration, "Catalog");
}
else builder.Services.AddSingleton<IMenuCatalog, InMemoryMenuCatalog>();
builder.Services.AddHttpClient<ICatalogTenantAuthorizer, HttpOrganizationAccessChecker>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:PlatformDirectory"]
        ?? throw new InvalidOperationException("Services:PlatformDirectory is required."));
}).AddNexaConnectCorrelationPropagation();
builder.Services.AddHttpClient<CatalogWorkloadTokenProvider>();
builder.Services.AddHttpClient<IRestaurantBranchScopeReader, RestaurantBranchScopeClient>().AddNexaConnectCorrelationPropagation();
builder.Services.AddHttpClient<ProductAuthorizationClient>(client => client.BaseAddress = new Uri(
    builder.Configuration["Services:Authorization"] ?? throw new InvalidOperationException("Services:Authorization is required."))).AddNexaConnectCorrelationPropagation();

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

public sealed class CatalogProgram;
