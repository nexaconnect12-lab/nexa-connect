using NexaConnect.Observability;

var builder = WebApplication.CreateBuilder(args);
if (!builder.Environment.IsDevelopment())
    throw new InvalidOperationException("The Omise token helper requires Development.");
// Do not allow inherited launcher settings to expose this acceptance-only page.
if (builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren().Any())
    throw new InvalidOperationException("Custom Kestrel endpoints are not supported.");
builder.WebHost.UseUrls("https://localhost:54443");
builder.AddNexaConnectObservability("nexaconnect-omise-token-helper");
var app = builder.Build();
app.UseNexaConnectRequestLogging();
app.Use(async (context, next) =>
{
    context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; script-src 'self' https://cdn.omise.co; connect-src https://vault.omise.co; style-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    await next(context);
});
app.UseStaticFiles();
app.MapGet("/", () => Results.Redirect("/index.html"));
app.Run();
