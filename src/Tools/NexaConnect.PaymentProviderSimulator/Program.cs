using System.Security.Cryptography;
using System.Text;
using NexaConnect.Observability;

var builder = WebApplication.CreateBuilder(args);
if (!builder.Environment.IsEnvironment("Testing"))
    throw new InvalidOperationException("The provider simulator requires the Testing environment.");
string key = builder.Configuration["Simulator:ApiKey"]
    ?? throw new InvalidOperationException("Simulator:ApiKey is required.");
if (!Uri.TryCreate(builder.Configuration["ASPNETCORE_URLS"], UriKind.Absolute, out var address)
    || address.Scheme != "https" || address.Host != "127.0.0.1"
    || builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren().Any())
    throw new InvalidOperationException("Simulator requires one IPv4 loopback HTTPS endpoint.");
builder.AddNexaConnectObservability("nexaconnect-payment-provider-simulator");
var app = builder.Build();
app.UseNexaConnectRequestLogging();
app.Use(async (context, next) =>
{
    await next(context);
    if (context.Response.StatusCode is 401 or 409)
        app.Logger.LogWarning("Simulator boundary rejected with status {StatusCode}", context.Response.StatusCode);
});
var gate = new object();
var authorizations = new Dictionary<Guid, Authorization>();
var captures = new Dictionary<Guid, Capture>();
var voids = new Dictionary<Guid, VoidCommand>();
bool Authorized(HttpRequest request) => CryptographicOperations.FixedTimeEquals(
    SHA256.HashData(Encoding.UTF8.GetBytes(request.Headers.Authorization.ToString())),
    SHA256.HashData(Encoding.UTF8.GetBytes("Bearer " + key)));
app.MapGet("/health/live", () => Results.Ok());
app.MapPost("/v1/authorizations", (Authorization command, HttpRequest request) =>
{
    if (!Authorized(request)) return Results.Unauthorized();
    if (command.PaymentIntentId == Guid.Empty || command.OrderId == Guid.Empty || command.Amount <= 0
        || command.Currency != "THB" || command.PaymentMethod != "card") return Results.BadRequest();
    lock (gate)
    {
        if (authorizations.TryGetValue(command.PaymentIntentId, out var original) && original != command)
            return Results.Conflict();
        authorizations[command.PaymentIntentId] = command;
        return Results.Json(new { succeeded = true, providerTransactionId = $"sim-auth-{command.PaymentIntentId:N}" });
    }
});
app.MapGet("/v1/authorizations/{id:guid}", (Guid id, HttpRequest request) =>
{
    if (!Authorized(request)) return Results.Unauthorized();
    lock (gate) return authorizations.ContainsKey(id)
        ? Results.Json(new { status = "authorized", providerTransactionId = $"sim-auth-{id:N}" }) : Results.NotFound();
});
app.MapPost("/v1/captures", (Capture command, HttpRequest request) =>
{
    if (!Authorized(request)) return Results.Unauthorized();
    if (request.Headers["Idempotency-Key"] != command.PaymentIntentId.ToString("D")) return Results.BadRequest();
    lock (gate)
    {
        if (voids.ContainsKey(command.PaymentIntentId)) return Results.Conflict();
        if (!authorizations.TryGetValue(command.PaymentIntentId, out var authorization)
            || command.ProviderAuthorizationId != $"sim-auth-{command.PaymentIntentId:N}"
            || command.Amount != authorization.Amount || command.Currency != authorization.Currency)
            return Results.BadRequest();
        if (captures.TryGetValue(command.PaymentIntentId, out var original) && original != command)
            return Results.Conflict();
        captures[command.PaymentIntentId] = command;
        return Results.Json(new { succeeded = true, providerTransactionId = $"sim-capture-{command.PaymentIntentId:N}" });
    }
});
app.MapGet("/v1/captures/{id:guid}", (Guid id, HttpRequest request) =>
{
    if (!Authorized(request)) return Results.Unauthorized();
    lock (gate) return captures.ContainsKey(id)
        ? Results.Json(new { status = "captured", providerTransactionId = $"sim-capture-{id:N}" }) : Results.NotFound();
});
app.MapPost("/v1/voids", (VoidCommand command, HttpRequest request) =>
{
    if (!Authorized(request)) return Results.Unauthorized();
    if (request.Headers["Idempotency-Key"] != $"void:{command.PaymentIntentId:D}") return Results.BadRequest();
    lock (gate)
    {
        if (captures.ContainsKey(command.PaymentIntentId)) return Results.Conflict();
        if (!authorizations.ContainsKey(command.PaymentIntentId)
            || command.ProviderAuthorizationId != $"sim-auth-{command.PaymentIntentId:N}") return Results.BadRequest();
        if (voids.TryGetValue(command.PaymentIntentId, out var original) && original != command) return Results.Conflict();
        voids[command.PaymentIntentId] = command;
        return Results.Json(new { succeeded = true, providerTransactionId = $"sim-void-{command.PaymentIntentId:N}" });
    }
});
app.MapGet("/v1/voids/{id:guid}", (Guid id, HttpRequest request) =>
{
    if (!Authorized(request)) return Results.Unauthorized();
    lock (gate) return voids.ContainsKey(id)
        ? Results.Json(new { status = "voided", providerTransactionId = $"sim-void-{id:N}" }) : Results.NotFound();
});
app.Run();
internal sealed record Authorization(Guid PaymentIntentId, Guid OrderId, decimal Amount, string Currency, string PaymentMethod);
internal sealed record Capture(Guid PaymentIntentId, string ProviderAuthorizationId, decimal Amount, string Currency);
internal sealed record VoidCommand(Guid PaymentIntentId, string ProviderAuthorizationId);
