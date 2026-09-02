using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Payment.Application.Intents;
using NexaConnect.Services.Payment.Infrastructure;
using NexaConnect.Services.Payment.Infrastructure.Providers;
using Npgsql;
using NexaConnect.Infrastructure.Http;
using NexaConnect.Services.Payment.Application.Tenant;
using NexaConnect.Infrastructure.Authorization;
using NexaConnect.Observability;
using NexaConnect.Infrastructure.Messaging;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
builder.AddNexaConnectObservability("nexaconnect-payment");
NexaConnect.Infrastructure.Authentication.AuthenticationServiceCollectionExtensions.EnsureProductionHttps(builder.Configuration, builder.Environment);

// Add services to the container.

builder.Services.AddControllers();
IHealthChecksBuilder healthChecks = builder.Services.AddHealthChecks();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<IServiceWorkloadTokenProvider, ServiceWorkloadTokenProvider>();
builder.Services.AddHttpClient("PaymentPlatformDirectory", client => client.BaseAddress = new Uri(
    builder.Configuration["Services:PlatformDirectory"] ?? throw new InvalidOperationException("Services:PlatformDirectory is required."))).AddNexaConnectCorrelationPropagation();
builder.Services.AddHttpClient("PaymentRestaurant", client => client.BaseAddress = new Uri(
    builder.Configuration["Services:Restaurant"] ?? throw new InvalidOperationException("Services:Restaurant is required."))).AddNexaConnectCorrelationPropagation();
builder.Services.AddHttpClient("PaymentOrder", client => client.BaseAddress = new Uri(
    builder.Configuration["Services:Order"] ?? throw new InvalidOperationException("Services:Order is required."))).AddNexaConnectCorrelationPropagation();
builder.Services.AddHttpClient<ProductAuthorizationClient>(client => client.BaseAddress = new Uri(
    builder.Configuration["Services:Authorization"] ?? throw new InvalidOperationException("Services:Authorization is required."))).AddNexaConnectCorrelationPropagation();
builder.Services.AddScoped<IPaymentTenantAuthorizer, HttpPaymentTenantAuthorizer>();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddNexaConnectApiAuthentication(builder.Configuration);
builder.Services.AddNexaConnectDataProtection(builder.Configuration, builder.Environment, "payment");
builder.Services.AddOptions<PaymentProviderOptions>()
    .Bind(builder.Configuration.GetSection("PaymentProvider"))
    .Validate(options => options.Adapter is "Disabled" or "GenericHttp",
        "PaymentProvider:Adapter must be Disabled or GenericHttp.")
    .Validate(options => options.Adapter == "Disabled"
        || Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out Uri? uri)
        && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase),
        "PaymentProvider:BaseUrl must be an absolute HTTPS URI when GenericHttp is selected.")
    .Validate(options => options.RequestTimeout > TimeSpan.Zero && options.RequestTimeout <= TimeSpan.FromMinutes(2),
        "PaymentProvider:RequestTimeout must be greater than zero and no more than two minutes.")
    .ValidateOnStart();
builder.Services.AddScoped<PaymentAuthorizationService>();
builder.Services.AddScoped<IPaymentAuthorizationService>(services => services.GetRequiredService<PaymentAuthorizationService>());
builder.Services.AddScoped<IPaymentCaptureService, PaymentCaptureService>();
builder.Services.AddScoped<PaymentCaptureRecoveryService>();
builder.Services.AddScoped<IPaymentVoidService, PaymentVoidService>();
builder.Services.AddScoped<PaymentVoidRecoveryService>();
builder.Services.AddTransient<RetryingHttpMessageHandler>();
builder.Services.AddHttpClient<HttpPaymentProvider>((services, client) =>
{
    PaymentProviderOptions options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<PaymentProviderOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = options.RequestTimeout;
}).AddHttpMessageHandler<RetryingHttpMessageHandler>();
builder.Services.AddSingleton<DisabledPaymentProvider>();
builder.Services.AddScoped<IPaymentProvider>(services =>
    services.GetRequiredService<Microsoft.Extensions.Options.IOptions<PaymentProviderOptions>>().Value.Adapter switch
    {
        "GenericHttp" => services.GetRequiredService<HttpPaymentProvider>(),
        _ => services.GetRequiredService<DisabledPaymentProvider>()
    });
if (builder.Configuration.GetValue<string>("Persistence:Provider")?.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase) == true)
{
    builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Payment")
        ?? throw new InvalidOperationException("ConnectionStrings:Payment is required.")));
    builder.Services.AddSingleton<IPaymentIntents, PostgresPaymentIntents>();
    healthChecks.AddCheck<PaymentDatabaseReadinessHealthCheck>("payment_database", tags: ["ready"]);
    builder.Services.Configure<PaymentOperationalMetricsOptions>(builder.Configuration.GetSection("OperationalMetrics"));
    builder.Services.AddHostedService<PaymentOperationalMetricsWorker>();
    if (builder.Configuration["PaymentProvider:Adapter"]?.Equals("GenericHttp", StringComparison.Ordinal) == true)
    {
        builder.Services.AddHostedService<PaymentAuthorizationRecoveryWorker>();
        builder.Services.AddPaymentCaptureRecoveryWorker(builder.Configuration);
        if (builder.Configuration.GetValue("PaymentProvider:VoidRecoveryEnabled", true))
            builder.Services.AddHostedService<PaymentVoidRecoveryWorker>();
    }
    if (builder.Configuration.GetValue<bool>("Outbox:Enabled"))
        builder.Services.AddPostgresOutbox(builder.Configuration, "Payment");
}
else
{
    builder.Services.AddSingleton<IPaymentIntents, InMemoryPaymentIntents>();
}

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
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
}).AllowAnonymous();

app.Run();

public sealed class PaymentProgram;
