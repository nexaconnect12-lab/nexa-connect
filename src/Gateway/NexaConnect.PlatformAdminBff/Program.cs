using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Caching.Distributed;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Observability;
using NexaConnect.PlatformAdminBff;

var builder = WebApplication.CreateBuilder(args);
builder.AddNexaConnectObservability("nexaconnect-platform-admin-bff");
NexaConnect.Infrastructure.Authentication.AuthenticationServiceCollectionExtensions.EnsureProductionHttps(builder.Configuration, builder.Environment);
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddNexaConnectDataProtection(builder.Configuration, builder.Environment, "platform-admin-bff");
builder.Services.AddNexaConnectBffSessionCache(builder.Configuration, builder.Environment);
builder.Services.AddSingleton<ITicketStore, AdminTicketStore>();
builder.Services.AddOptions<CookieAuthenticationOptions>("AdminCookie").Configure<ITicketStore>((o, store) => o.SessionStore = store);
builder.Services.AddHttpClient("PlatformDirectory", client => client.BaseAddress = new Uri(builder.Configuration["Services:PlatformDirectory"] ?? throw new InvalidOperationException("Services:PlatformDirectory is required.")));
builder.Services.AddAuthentication(o => { o.DefaultAuthenticateScheme = "AdminCookie"; o.DefaultSignInScheme = "AdminCookie"; o.DefaultChallengeScheme = "AdminOidc"; })
    .AddCookie("AdminCookie", o => { o.Cookie.Name = "__Host-nexa-platform-admin"; o.Cookie.SecurePolicy = CookieSecurePolicy.Always; o.Cookie.HttpOnly = true; o.LoginPath = "/bff/platform-admin/login"; o.LogoutPath = "/bff/platform-admin/logout"; })
    .AddOpenIdConnect("AdminOidc", o => { var s = builder.Configuration.GetRequiredSection("Bff"); o.Authority = s["Authority"] ?? throw new InvalidOperationException("Bff:Authority is required."); o.ClientId = s["ClientId"] ?? "platform-admin-bff"; o.ClientSecret = s["ClientSecret"] ?? throw new InvalidOperationException("Bff:ClientSecret is required."); o.ResponseType = "code"; o.UsePkce = true; o.SaveTokens = true; o.SignInScheme = "AdminCookie"; o.Scope.Add("nexaconnect-api"); o.RequireHttpsMetadata = s.GetValue<bool>("RequireHttpsMetadata"); o.MapInboundClaims = false; o.TokenValidationParameters.RoleClaimType = "roles"; o.Events.OnTokenValidated = context => { if (context.Principal?.Identity is ClaimsIdentity identity && context.Principal.FindFirst("realm_access") is { } realmAccess && JsonDocument.Parse(realmAccess.Value).RootElement.TryGetProperty("roles", out var roles)) foreach (var role in roles.EnumerateArray()) if (role.GetString() is { } value && !identity.HasClaim("roles", value)) identity.AddClaim(new Claim("roles", value)); return Task.CompletedTask; }; if (builder.Environment.IsDevelopment()) o.PushedAuthorizationBehavior = PushedAuthorizationBehavior.Disable; });
builder.Services.AddAuthorization(o =>
{
    static void Configure(Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder policy, params string[] roles)
    {
        policy.AuthenticationSchemes.Add("AdminCookie");
        policy.RequireAuthenticatedUser();
        policy.RequireRole(roles);
    }
    o.AddPolicy("PlatformUser", p => Configure(p, "platform-owner", "platform-admin", "platform-support", "platform-auditor"));
    o.AddPolicy("PlatformAdmin", p => Configure(p, "platform-owner", "platform-admin"));
    o.AddPolicy("PlatformSupport", p => Configure(p, "platform-owner", "platform-admin", "platform-support"));
    o.AddPolicy("PlatformAudit", p => Configure(p, "platform-owner", "platform-admin", "platform-auditor"));
});
var app = builder.Build();
app.UseNexaConnectRequestLogging();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseHttpsRedirection(); app.UseAuthentication(); app.UseAuthorization();
app.MapGet("/", () => Results.Text("NexaConnect Platform Admin BFF is running."));
app.MapHealthChecks("/health").AllowAnonymous();
app.MapGet("/bff/platform-admin/login", (string? returnUrl) => Results.Challenge(new AuthenticationProperties { RedirectUri = NormalizeReturnUrl(returnUrl) }, ["AdminOidc"])).AllowAnonymous();
app.MapGet("/bff/platform-admin/logout", () => Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, ["AdminCookie", "AdminOidc"])).AllowAnonymous();
app.MapGet("/bff/platform-admin/me", (HttpContext c) => Results.Ok(new { SubjectId = c.User.FindFirstValue("sub"), Username = c.User.FindFirstValue("preferred_username") })).RequireAuthorization("PlatformUser");
MapProxy("organizations", HttpMethod.Post); MapProxy("products", HttpMethod.Post);
app.MapMethods("/bff/platform-admin/organizations/{organizationId:guid}", ["PATCH"], Proxy("api/platform-directory/v1/organizations/{organizationId}")).RequireAuthorization("PlatformAdmin");
app.MapMethods("/bff/platform-admin/organizations/{organizationId:guid}/members/{subjectId}", ["PUT"], Proxy("api/platform-directory/v1/organizations/{organizationId}/members/{subjectId}")).RequireAuthorization("PlatformAdmin");
app.MapMethods("/bff/platform-admin/organizations/{organizationId:guid}/products", ["PUT"], Proxy("api/platform-directory/v1/organizations/{organizationId}/products")).RequireAuthorization("PlatformAdmin");
app.MapMethods("/bff/platform-admin/support-elevations", ["POST"], Proxy("api/platform-directory/v1/support-elevations")).RequireAuthorization("PlatformSupport");
app.MapMethods("/bff/platform-admin/support-elevations/effective", ["GET"], Proxy("api/platform-directory/v1/support-elevations/effective")).RequireAuthorization("PlatformSupport");
app.MapMethods("/bff/platform-admin/support-elevations/{elevationId:guid}", ["GET"], Proxy("api/platform-directory/v1/support-elevations/{elevationId}")).RequireAuthorization("PlatformAudit");
app.MapMethods("/bff/platform-admin/support-elevations/{elevationId:guid}/approve", ["POST"], Proxy("api/platform-directory/v1/support-elevations/{elevationId}/approve")).RequireAuthorization("PlatformAdmin");
app.MapMethods("/bff/platform-admin/support-elevations/{elevationId:guid}/revoke", ["POST"], Proxy("api/platform-directory/v1/support-elevations/{elevationId}/revoke")).RequireAuthorization("PlatformAdmin");
app.MapControllers(); app.Run();

static string NormalizeReturnUrl(string? returnUrl) =>
    string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith("/", StringComparison.Ordinal)
        || returnUrl.StartsWith("//", StringComparison.Ordinal)
        ? "/"
        : returnUrl;

void MapProxy(string route, HttpMethod method) => app.MapMethods($"/bff/platform-admin/{route}", [method.Method], Proxy($"api/platform-directory/v1/{route}")).RequireAuthorization("PlatformAdmin");
RequestDelegate Proxy(string path) => async context =>
{
    string? token = await context.GetTokenAsync("AdminCookie", "access_token");
    if (string.IsNullOrWhiteSpace(token))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    string organizationId = Uri.EscapeDataString(context.Request.RouteValues["organizationId"]?.ToString() ?? string.Empty);
    string subjectId = Uri.EscapeDataString(context.Request.RouteValues["subjectId"]?.ToString() ?? string.Empty);
    string elevationId = Uri.EscapeDataString(context.Request.RouteValues["elevationId"]?.ToString() ?? string.Empty);
    string target = path.Replace("{organizationId}", organizationId, StringComparison.Ordinal)
        .Replace("{subjectId}", subjectId, StringComparison.Ordinal)
        .Replace("{elevationId}", elevationId, StringComparison.Ordinal);
    if (context.Request.QueryString.HasValue) target += context.Request.QueryString.Value;
    using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), target);
    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    if (context.Request.ContentLength is > 0)
        request.Content = await ReplayableProxyContent.CreateAsync(context.Request, context.RequestAborted);
    HttpClient client = context.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient("PlatformDirectory");
    using HttpResponseMessage response = await client.SendAsync(request, context.RequestAborted);
    context.Response.StatusCode = (int)response.StatusCode;
    if (response.Content.Headers.ContentType is not null)
        context.Response.ContentType = response.Content.Headers.ContentType.ToString();
    await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
};
public partial class Program;

public static class ReplayableProxyContent
{
    public static async Task<HttpContent> CreateAsync(HttpRequest request, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);
        var content = new ByteArrayContent(buffer.ToArray());
        if (!string.IsNullOrWhiteSpace(request.ContentType))
            content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(request.ContentType);
        return content;
    }
}
