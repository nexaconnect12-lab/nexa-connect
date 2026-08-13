using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Infrastructure.Authorization;
using NexaConnect.Observability;
using Npgsql;
using NexaConnect.Services.Media.Application;
using NexaConnect.Services.Media.Infrastructure.Persistence;
using NexaConnect.Services.Media.Infrastructure;

var builder=WebApplication.CreateBuilder(args);
builder.AddNexaConnectObservability("nexaconnect-media");
NexaConnect.Infrastructure.Authentication.AuthenticationServiceCollectionExtensions.EnsureProductionHttps(builder.Configuration,builder.Environment);
builder.Services.AddControllers();builder.Services.AddOpenApi();builder.Services.AddNexaConnectApiAuthentication(builder.Configuration);builder.Services.AddNexaConnectDataProtection(builder.Configuration,builder.Environment,"media");
builder.Services.AddSingleton(_=>NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Media")??throw new InvalidOperationException("ConnectionStrings:Media is required.")));
builder.Services.AddScoped<MediaAssetQueries>();builder.Services.AddScoped<IMediaAssetRepository,PostgresMediaAssetRepository>();
builder.Services.AddHttpClient("PlatformDirectory",client=>client.BaseAddress=new Uri(builder.Configuration["Services:PlatformDirectory"]??throw new InvalidOperationException("Services:PlatformDirectory is required."))).AddNexaConnectCorrelationPropagation();
builder.Services.AddHttpClient<ProductAuthorizationClient>(client=>client.BaseAddress=new Uri(builder.Configuration["Services:Authorization"]??throw new InvalidOperationException("Services:Authorization is required."))).AddNexaConnectCorrelationPropagation();
builder.Services.AddScoped<IMediaCustomerAuthorizer>(provider=>new HttpMediaCustomerAuthorizer(provider.GetRequiredService<IHttpClientFactory>().CreateClient("PlatformDirectory"),provider.GetRequiredService<ProductAuthorizationClient>(),provider.GetRequiredService<ILogger<HttpMediaCustomerAuthorizer>>()));
var app=builder.Build();app.UseNexaConnectRequestLogging();if(app.Environment.IsDevelopment())app.MapOpenApi();app.UseHttpsRedirection();app.UseAuthentication();app.UseAuthorization();app.MapControllers();app.Run();
public partial class Program;
