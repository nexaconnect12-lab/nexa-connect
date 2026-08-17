using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Notification.Application.Messages;
using NexaConnect.Services.Notification.Infrastructure;
using NexaConnect.Services.Notification.Application.Tenant;
using NexaConnect.Services.Notification.Application.Delivery;
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
builder.Services.AddOptions<NotificationProviderOptions>().Bind(builder.Configuration.GetSection("NotificationProvider"))
    .Validate(options => options.ProviderCode.Length is > 0 and <= 64
        && options.ProviderCode.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
        && Uri.TryCreate(options.Path, UriKind.Relative, out _) && !options.Path.StartsWith("//", StringComparison.Ordinal)
        && Uri.TryCreate(options.ReceiptPath, UriKind.Relative, out _) && !options.ReceiptPath.StartsWith("//", StringComparison.Ordinal)
        && options.ReceiptPath.Contains("{id}", StringComparison.Ordinal), "Notification provider settings are invalid.")
    .ValidateOnStart();
if (builder.Configuration.GetValue<string>("Persistence:Provider")?.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase) == true)
{
    builder.Services.AddPostgresInbox(builder.Configuration, "Notification");
    builder.Services.AddPostgresOutbox(builder.Configuration, "Notification");
    builder.Services.AddSingleton<INotificationSender, PostgresNotificationSender>();
    builder.Services.AddOptions<NotificationDeliveryOptions>().Bind(builder.Configuration.GetSection("NotificationDelivery"))
        .Validate(options => options.MaximumAttempts is >= 1 and <= 100 && options.PollInterval > TimeSpan.Zero
            && options.Lease >= TimeSpan.FromSeconds(10), "Notification delivery settings are invalid.")
        .ValidateOnStart();
    if (builder.Configuration.GetValue<bool>("NotificationDelivery:Enabled"))
    {
        if (!Uri.TryCreate(builder.Configuration["NotificationProvider:BaseUrl"], UriKind.Absolute, out Uri? providerBaseUrl))
            throw new InvalidOperationException("NotificationProvider:BaseUrl must be an absolute URL when delivery is enabled.");
        if (!builder.Environment.IsDevelopment() && providerBaseUrl.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("NotificationProvider:BaseUrl must use HTTPS outside Development.");
        string? providerToken = builder.Configuration["NotificationProvider:ApiToken"];
        if (string.IsNullOrWhiteSpace(providerToken) || providerToken.Length > 4096 || providerToken.Any(char.IsControl))
            throw new InvalidOperationException("NotificationProvider:ApiToken is required when delivery is enabled.");
        builder.Services.AddSingleton<INotificationDeliveryRepository, PostgresNotificationDeliveryRepository>();
        builder.Services.AddSingleton<NotificationDeliveryProcessor>();
        builder.Services.AddHttpClient<INotificationProvider, HttpNotificationProvider>(client => client.BaseAddress = providerBaseUrl);
        builder.Services.AddHostedService<NotificationDeliveryWorker>();
    }
    builder.Services.AddScoped<NotificationIntegrationHandler>();
    builder.Services.Configure<NotificationConsumerOptions>(builder.Configuration.GetSection("NotificationConsumer"));
    if (builder.Configuration.GetValue<bool>("NotificationConsumer:Enabled")) builder.Services.AddHostedService<NotificationRequestedConsumer>();
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
