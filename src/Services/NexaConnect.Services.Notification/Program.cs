using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Notification.Application.Messages;
using NexaConnect.Services.Notification.Infrastructure;
using NexaConnect.Services.Notification.Application.Tenant;
using NexaConnect.Infrastructure.Authorization;
using NexaConnect.Infrastructure.Http;
using NexaConnect.Infrastructure.Messaging;
using NexaConnect.Observability;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.AddNexaConnectObservability("nexaconnect-notification");
NexaConnect.Infrastructure.Authentication.AuthenticationServiceCollectionExtensions.EnsureProductionHttps(builder.Configuration, builder.Environment);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddNexaConnectApiAuthentication(builder.Configuration);
builder.Services.AddNexaConnectDataProtection(builder.Configuration, builder.Environment, "notification");
builder.Services.AddHttpClient("NotificationPlatformDirectory", client => client.BaseAddress = new Uri(builder.Configuration["Services:PlatformDirectory"] ?? throw new InvalidOperationException("Services:PlatformDirectory is required."))).AddNexaConnectCorrelationPropagation();
builder.Services.AddHttpClient<ProductAuthorizationClient>(client => client.BaseAddress = new Uri(builder.Configuration["Services:Authorization"] ?? throw new InvalidOperationException("Services:Authorization is required."))).AddNexaConnectCorrelationPropagation();
builder.Services.AddScoped<INotificationTenantAuthorizer, HttpNotificationTenantAuthorizer>();
builder.Services.Configure<NotificationProviderOptions>(builder.Configuration.GetSection("NotificationProvider"));
if (builder.Configuration.GetValue<string>("Persistence:Provider")?.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase) == true)
{
    builder.Services.AddPostgresInbox(builder.Configuration, "Notification");
    builder.Services.AddPostgresOutbox(builder.Configuration, "Notification");
    builder.Services.AddSingleton<INotificationSender, PostgresNotificationSender>();
    builder.Services.AddScoped<NotificationIntegrationHandler>();
    builder.Services.Configure<NotificationConsumerOptions>(builder.Configuration.GetSection("NotificationConsumer"));
    if (builder.Configuration.GetValue<bool>("NotificationConsumer:Enabled")) builder.Services.AddHostedService<NotificationRequestedConsumer>();
}
else if (Uri.TryCreate(builder.Configuration["NotificationProvider:BaseUrl"], UriKind.Absolute, out var notificationBaseUrl))
{
    builder.Services.AddHttpClient<INotificationSender, HttpNotificationSender>(client => client.BaseAddress = notificationBaseUrl);
}
else builder.Services.AddSingleton<INotificationSender, InMemoryNotificationSender>();

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

public partial class Program;
