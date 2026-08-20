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

var builder = WebApplication.CreateBuilder(args);
builder.AddNexaConnectObservability("nexaconnect-payment");
NexaConnect.Infrastructure.Authentication.AuthenticationServiceCollectionExtensions.EnsureProductionHttps(builder.Configuration, builder.Environment);

// Add services to the container.

builder.Services.AddControllers();
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
builder.Services.Configure<PaymentProviderOptions>(builder.Configuration.GetSection("PaymentProvider"));
builder.Services.AddScoped<IPaymentAuthorizationService, PaymentAuthorizationService>();
builder.Services.AddTransient<RetryingHttpMessageHandler>();
builder.Services.AddHttpClient<IPaymentProvider, HttpPaymentProvider>((services, client) =>
{
    PaymentProviderOptions options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<PaymentProviderOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
}).AddHttpMessageHandler<RetryingHttpMessageHandler>();
if (builder.Configuration.GetValue<string>("Persistence:Provider")?.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase) == true)
{
    builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Payment")
        ?? throw new InvalidOperationException("ConnectionStrings:Payment is required.")));
    builder.Services.AddSingleton<IPaymentIntents, PostgresPaymentIntents>();
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

app.Run();

public sealed class PaymentProgram;
