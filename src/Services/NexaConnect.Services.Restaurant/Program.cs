using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Restaurant.Application.Authorization;
using NexaConnect.Services.Restaurant.Infrastructure.Persistence;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
NexaConnect.Infrastructure.Authentication.AuthenticationServiceCollectionExtensions.EnsureProductionHttps(builder.Configuration, builder.Environment);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddNexaConnectApiAuthentication(builder.Configuration);
builder.Services.AddNexaConnectDevelopmentDataProtection(builder.Environment, "restaurant");
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(
    builder.Configuration.GetConnectionString("Restaurant")
    ?? throw new InvalidOperationException("ConnectionStrings:Restaurant is required.")));
builder.Services.AddScoped<IAuthorizationScopeReader, PostgresAuthorizationScopeReader>();
var app = builder.Build();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program;
