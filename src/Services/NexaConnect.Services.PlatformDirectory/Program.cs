using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.PlatformDirectory;
using NexaConnect.Services.PlatformDirectory.Application.Access;
using NexaConnect.Services.PlatformDirectory.Application.ControlPlane;
using NexaConnect.Services.PlatformDirectory.Application.Support;
using NexaConnect.Services.PlatformDirectory.Infrastructure.Persistence;
using NexaConnect.Observability;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.AddNexaConnectObservability("nexaconnect-platform-directory");
NexaConnect.Infrastructure.Authentication.AuthenticationServiceCollectionExtensions.EnsureProductionHttps(builder.Configuration, builder.Environment);
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddNexaConnectApiAuthentication(builder.Configuration);
builder.Services.AddNexaConnectDataProtection(builder.Configuration, builder.Environment, "platform-directory");
builder.Services.AddSingleton<NpgsqlDataSource>(_ =>
{
    string connectionString = builder.Configuration.GetConnectionString("PlatformDirectory")
        ?? throw new InvalidOperationException("ConnectionStrings:PlatformDirectory is required.");
    return NpgsqlDataSource.Create(connectionString);
});
builder.Services.AddScoped<IOrganizationAccessReader, PostgresOrganizationAccessReader>();
builder.Services.AddScoped<IPlatformDirectoryManagement, PlatformDirectoryManagementService>();
builder.Services.AddScoped<IPlatformDirectoryManagementRepository, PostgresPlatformDirectoryManagementRepository>();
builder.Services.AddScoped<ISupportElevationRepository, PostgresSupportElevationRepository>();
builder.Services.AddScoped<SupportElevationApplicationService>();
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();
app.UseNexaConnectRequestLogging();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapControllers();
app.Run();

public partial class Program;
