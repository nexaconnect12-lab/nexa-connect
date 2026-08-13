using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Restaurant.Application.Authorization;
using NexaConnect.Services.Restaurant.Infrastructure.Persistence;
using Npgsql;
using NexaConnect.Observability;
using NexaConnect.Services.Restaurant.Application.Provisioning;
using NexaConnect.Services.Restaurant.Application.Branches;
using NexaConnect.Services.Restaurant.Infrastructure;
using NexaConnect.Infrastructure.Authorization;
using NexaConnect.Services.Restaurant.Application.Configuration;

var builder = WebApplication.CreateBuilder(args);
builder.AddNexaConnectObservability("nexaconnect-restaurant");
NexaConnect.Infrastructure.Authentication.AuthenticationServiceCollectionExtensions.EnsureProductionHttps(builder.Configuration, builder.Environment);
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddNexaConnectApiAuthentication(builder.Configuration);
builder.Services.AddNexaConnectDataProtection(builder.Configuration, builder.Environment, "restaurant");
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(
    builder.Configuration.GetConnectionString("Restaurant")
    ?? throw new InvalidOperationException("ConnectionStrings:Restaurant is required.")));
builder.Services.AddScoped<IAuthorizationScopeReader, PostgresAuthorizationScopeReader>();
builder.Services.AddScoped<IRestaurantProvisioning, RestaurantProvisioningService>();
builder.Services.AddScoped<IRestaurantProvisioningRepository, PostgresRestaurantProvisioningRepository>();
builder.Services.AddScoped<BranchManagement>();
builder.Services.AddScoped<IBranchManagementRepository, PostgresBranchManagementRepository>();
builder.Services.AddScoped<BranchProductConfigurationService>();
builder.Services.AddScoped<IBranchProductConfigurationRepository, PostgresBranchProductConfigurationRepository>();
builder.Services.AddHttpClient("PlatformDirectory",client=>client.BaseAddress=new Uri(builder.Configuration["Services:PlatformDirectory"]??throw new InvalidOperationException("Services:PlatformDirectory is required."))).AddNexaConnectCorrelationPropagation();
builder.Services.AddHttpClient<ProductAuthorizationClient>(client=>client.BaseAddress=new Uri(builder.Configuration["Services:Authorization"]??throw new InvalidOperationException("Services:Authorization is required."))).AddNexaConnectCorrelationPropagation();
builder.Services.AddScoped<IBranchCustomerAuthorizer>(provider=>new HttpBranchCustomerAuthorizer(provider.GetRequiredService<IHttpClientFactory>().CreateClient("PlatformDirectory"),provider.GetRequiredService<ProductAuthorizationClient>()));
var app = builder.Build();
app.UseNexaConnectRequestLogging();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program;
