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
using NexaConnect.Infrastructure.Messaging;

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
builder.Services.AddScoped<OperationalBranchDiscovery>();
builder.Services.AddScoped<IBranchManagementRepository, PostgresBranchManagementRepository>();
builder.Services.AddScoped<BranchProductConfigurationService>();
builder.Services.AddScoped<BranchGalleryService>();
builder.Services.AddScoped<IBranchPromptPayQrRepository,PostgresBranchPromptPayQrRepository>();
builder.Services.AddScoped<BranchPromptPayQrService>();
builder.Services.AddHttpClient<IPromptPayQrAssetValidator,HttpPromptPayQrAssetValidator>(client=>{client.BaseAddress=new Uri(builder.Configuration["Services:Media"]??"https://media.invalid/");client.Timeout=TimeSpan.FromSeconds(15);})
    .ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler{AllowAutoRedirect=false}).AddNexaConnectCorrelationPropagation();
builder.Services.AddScoped<GalleryReaderSelection>();
builder.Services.AddScoped<IGalleryReaderAuthorizer,HttpGalleryReaderAuthorizer>();
foreach(var service in new[]{("GalleryDirectory","PlatformDirectory"),("GalleryAuthorization","Authorization")})
    builder.Services.AddHttpClient(service.Item1,client=>{client.BaseAddress=new Uri(builder.Configuration[$"Services:{service.Item2}"]??throw new InvalidOperationException("Gallery authority service is required."));client.Timeout=TimeSpan.FromSeconds(10);})
        .ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler{AllowAutoRedirect=false}).AddNexaConnectCorrelationPropagation();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<NexaConnect.Services.Restaurant.Application.Qr.IQrSecrets, QrSecrets>();
builder.Services.AddScoped<NexaConnect.Services.Restaurant.Application.Qr.IQrEntryRepository, PostgresQrEntryRepository>();
builder.Services.AddScoped<NexaConnect.Services.Restaurant.Application.Qr.QrEntryService>();
builder.Services.AddSingleton(new NexaConnect.Infrastructure.Http.BoundedRequestGate(TimeProvider.System, 1000, 1000, 1));
builder.Services.AddScoped<IBranchGalleryRepository, PostgresBranchGalleryRepository>();
builder.Services.AddHttpClient<IGalleryAssetValidator,HttpGalleryAssetValidator>(client=>{client.BaseAddress=new Uri(builder.Configuration["Services:Media"]??"https://media.invalid/");client.Timeout=TimeSpan.FromSeconds(15);}).AddNexaConnectCorrelationPropagation();
builder.Services.AddScoped<IBranchProductConfigurationRepository, PostgresBranchProductConfigurationRepository>();
if(builder.Configuration.GetValue<bool>("Outbox:Enabled"))builder.Services.AddPostgresOutbox(builder.Configuration,"Restaurant");
builder.Services.AddHttpClient("PlatformDirectory",client=>client.BaseAddress=new Uri(builder.Configuration["Services:PlatformDirectory"]??throw new InvalidOperationException("Services:PlatformDirectory is required."))).AddNexaConnectCorrelationPropagation();
builder.Services.AddHttpClient<ProductAuthorizationClient>(client=>client.BaseAddress=new Uri(builder.Configuration["Services:Authorization"]??throw new InvalidOperationException("Services:Authorization is required."))).AddNexaConnectCorrelationPropagation();
builder.Services.AddScoped<IBranchCustomerAuthorizer>(provider=>new HttpBranchCustomerAuthorizer(provider.GetRequiredService<IHttpClientFactory>().CreateClient("PlatformDirectory"),provider.GetRequiredService<ProductAuthorizationClient>()));
var app = builder.Build();
app.UseNexaConnectRequestLogging();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/restaurant/v1/guest/qr") || context.Request.Path.Value?.Contains("/qr-entries", StringComparison.Ordinal) == true)
    { context.Response.Headers.CacheControl = "no-store"; context.Response.Headers["Referrer-Policy"] = "no-referrer"; }
    await next();
});
app.Use(async (context, next) => { if (context.Request.Path.Value?.EndsWith("/business-calendar", StringComparison.OrdinalIgnoreCase) == true) context.Response.Headers.CacheControl = "no-store"; await next(context); });
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program;
