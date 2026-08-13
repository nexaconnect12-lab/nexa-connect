using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Infrastructure.Authorization;
using NexaConnect.Observability;
using NexaConnect.Services.Reporting.Application;
using NexaConnect.Services.Reporting.Infrastructure;
using NexaConnect.Services.Reporting.Infrastructure.Persistence;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.AddNexaConnectObservability("nexaconnect-reporting");
NexaConnect.Infrastructure.Authentication.AuthenticationServiceCollectionExtensions.EnsureProductionHttps(builder.Configuration, builder.Environment);
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddNexaConnectApiAuthentication(builder.Configuration);
builder.Services.AddNexaConnectDataProtection(builder.Configuration, builder.Environment, "reporting");
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Reporting") ?? throw new InvalidOperationException("ConnectionStrings:Reporting is required.")));
builder.Services.AddScoped<ReportingQueries>();
builder.Services.AddScoped<IReportingReadRepository, PostgresReportingReadRepository>();
builder.Services.AddScoped<ActivityService>();
builder.Services.AddScoped<IActivityProjectionRepository, PostgresActivityProjectionRepository>();
builder.Services.AddHttpClient("PlatformDirectory", client => client.BaseAddress = new Uri(builder.Configuration["Services:PlatformDirectory"] ?? throw new InvalidOperationException("Services:PlatformDirectory is required."))).AddNexaConnectCorrelationPropagation();
builder.Services.AddHttpClient<ProductAuthorizationClient>(client => client.BaseAddress = new Uri(builder.Configuration["Services:Authorization"] ?? throw new InvalidOperationException("Services:Authorization is required."))).AddNexaConnectCorrelationPropagation();
builder.Services.AddScoped<IReportingCustomerAuthorizer>(provider => new HttpReportingCustomerAuthorizer(provider.GetRequiredService<IHttpClientFactory>().CreateClient("PlatformDirectory"), provider.GetRequiredService<ProductAuthorizationClient>()));

var app = builder.Build();
app.UseNexaConnectRequestLogging();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program;
