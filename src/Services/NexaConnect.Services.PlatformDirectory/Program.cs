using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.PlatformDirectory;
using NexaConnect.Services.PlatformDirectory.Application.Access;
using NexaConnect.Services.PlatformDirectory.Application.ControlPlane;
using NexaConnect.Services.PlatformDirectory.Application.Administration;
using NexaConnect.Services.PlatformDirectory.Infrastructure.Identity;
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
builder.Services.AddScoped<IPlatformAdministration, PlatformAdministrationService>();
builder.Services.AddScoped<IPlatformControlPlaneStore, PostgresPlatformControlPlaneStore>();
builder.Services.AddHttpClient<IPlatformIdentityAdministration, KeycloakPlatformIdentityAdministration>(client =>
    client.BaseAddress = new Uri(builder.Configuration["KeycloakAdmin:BaseUrl"] ?? throw new InvalidOperationException("KeycloakAdmin:BaseUrl is required.")));
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();
app.UseNexaConnectRequestLogging();
app.Use(async (context, next) =>
{
    try { await next(); }
    catch (ArgumentException exception)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = exception.Message }, context.RequestAborted);
    }
    catch (HttpRequestException)
    {
        context.Response.StatusCode = StatusCodes.Status502BadGateway;
        await context.Response.WriteAsJsonAsync(new { error = "The identity administration provider is unavailable." }, context.RequestAborted);
    }
});
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
