using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using NexaConnect.Contracts.Platform;
using NexaConnect.CustomerBff;
using NexaConnect.CustomerBff.Application.Catalog;
using NexaConnect.CustomerBff.Application.Inventory;
using NexaConnect.CustomerBff.Infrastructure.Catalog;
using NexaConnect.CustomerBff.Infrastructure.Inventory;
using NexaConnect.Infrastructure.Authentication;

var builder = WebApplication.CreateBuilder(args);
NexaConnect.Infrastructure.Authentication.AuthenticationServiceCollectionExtensions.EnsureProductionHttps(builder.Configuration, builder.Environment);
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddNexaConnectDataProtection(builder.Configuration, builder.Environment, "customer-bff");
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSingleton<ITicketStore, DistributedCacheTicketStore>();
builder.Services.AddOptions<CookieAuthenticationOptions>("CustomerCookie")
    .Configure<ITicketStore>((options, ticketStore) => options.SessionStore = ticketStore);
builder.Services.AddSingleton<TenantSelectionCookie>();
builder.Services.AddHttpClient("PlatformDirectory", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:PlatformDirectory"]
        ?? throw new InvalidOperationException("Services:PlatformDirectory is required."));
});
builder.Services.AddHttpClient<ICustomerCatalogPort, HttpCustomerCatalogPort>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Catalog"]
        ?? throw new InvalidOperationException("Services:Catalog is required."));
});
builder.Services.AddHttpClient<ICustomerInventoryPort, HttpCustomerInventoryPort>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Inventory"]
        ?? throw new InvalidOperationException("Services:Inventory is required."));
});
builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = "CustomerCookie";
        options.DefaultSignInScheme = "CustomerCookie";
        options.DefaultChallengeScheme = "CustomerOidc";
    })
    .AddCookie("CustomerCookie", options =>
    {
        options.Cookie.Name = "__Host-nexa-customer-bff";
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.SlidingExpiration = true;
    })
    .AddOpenIdConnect("CustomerOidc", options =>
    {
        IConfigurationSection settings = builder.Configuration.GetRequiredSection("Bff");
        options.Authority = settings["Authority"]
            ?? throw new InvalidOperationException("Bff:Authority is required.");
        options.RequireHttpsMetadata = settings.GetValue<bool>("RequireHttpsMetadata");
        options.ClientId = settings["ClientId"] ?? "nexaconnect-web-bff";
        options.ClientSecret = settings["ClientSecret"]
            ?? throw new InvalidOperationException("Bff:ClientSecret is required.");
        options.ResponseType = "code";
        options.UsePkce = true;
        options.SaveTokens = true;
        options.SignInScheme = "CustomerCookie";
        options.Scope.Add("nexaconnect-api");
        options.MapInboundClaims = false;
    });
builder.Services.AddAuthorization(options => options.AddPolicy("CustomerSession", policy =>
{
    policy.AuthenticationSchemes.Add("CustomerCookie");
    policy.RequireAuthenticatedUser();
}));

var app = builder.Build();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Text("NexaConnect Customer BFF is running."));
app.MapGet("/bff/customer/login", (string? returnUrl) =>
    Results.Challenge(new AuthenticationProperties { RedirectUri = NormalizeReturnUrl(returnUrl) }, ["CustomerOidc"])).AllowAnonymous();
app.MapGet("/bff/customer/logout", () =>
    Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, ["CustomerCookie", "CustomerOidc"])).AllowAnonymous();
app.MapGet("/bff/customer/me", (HttpContext context) => Results.Ok(new
{
    SubjectId = context.User.FindFirstValue("sub"),
    Username = context.User.FindFirstValue("preferred_username") ?? context.User.Identity?.Name
})).RequireAuthorization("CustomerSession");

app.MapGet("/bff/customer/access", async (
    HttpContext context,
    IHttpClientFactory clients,
    CancellationToken cancellationToken) =>
{
    HttpResponseMessage response = await CallPlatformDirectoryAsync(context, clients, "api/platform-directory/v1/me/access", cancellationToken);
    return await ForwardJsonAsync(response, cancellationToken);
}).RequireAuthorization("CustomerSession");

app.MapPost("/bff/customer/tenant", async (
    TenantSelectionRequest request,
    HttpContext context,
    IHttpClientFactory clients,
    TenantSelectionCookie selectionCookie,
    CancellationToken cancellationToken) =>
{
    HttpResponseMessage response = await CallPlatformDirectoryAsync(context, clients, "api/platform-directory/v1/me/access", cancellationToken);
    if (!response.IsSuccessStatusCode) return await ForwardJsonAsync(response, cancellationToken);
    CurrentPlatformAccessResponse? access = await response.Content.ReadFromJsonAsync<CurrentPlatformAccessResponse>(cancellationToken: cancellationToken);
    OrganizationApplicationAccess? match = access?.Organizations.FirstOrDefault(item =>
        item.OrganizationId == request.OrganizationId
        && string.Equals(item.ApplicationCode, request.ApplicationCode, StringComparison.Ordinal));
    if (match is null) return Results.Forbid();

    TenantContext tenant = new(access!.SubjectId, match.OrganizationId, match.ApplicationCode);
    context.Response.Cookies.Append("__Host-nexa-customer-tenant", selectionCookie.Protect(tenant), new CookieOptions
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        IsEssential = true,
        Path = "/"
    });
    return Results.Ok(tenant);
}).RequireAuthorization("CustomerSession");

app.MapGet("/bff/customer/tenant", async (
    HttpContext context,
    IHttpClientFactory clients,
    TenantSelectionCookie selectionCookie,
    CancellationToken cancellationToken) =>
{
    TenantContext? tenant = selectionCookie.Unprotect(context.Request.Cookies["__Host-nexa-customer-tenant"]);
    if (tenant is null) return Results.NotFound();
    HttpResponseMessage response = await CallPlatformDirectoryAsync(context, clients, "api/platform-directory/v1/me/access", cancellationToken);
    if (!response.IsSuccessStatusCode) return await ForwardJsonAsync(response, cancellationToken);
    CurrentPlatformAccessResponse? access = await response.Content.ReadFromJsonAsync<CurrentPlatformAccessResponse>(cancellationToken: cancellationToken);
    bool stillGranted = access?.SubjectId == tenant.SubjectId && access.Organizations.Any(item =>
        item.OrganizationId == tenant.OrganizationId && item.ApplicationCode == tenant.ApplicationCode);
    return stillGranted ? Results.Ok(tenant) : Results.Forbid();
}).RequireAuthorization("CustomerSession");

app.MapGet("/bff/customer/catalog/branches/{branchId:guid}/menu-items", async (
    Guid branchId,
    HttpContext context,
    IHttpClientFactory clients,
    ICustomerCatalogPort catalog,
    TenantSelectionCookie selectionCookie,
    CancellationToken cancellationToken) =>
{
    TenantContext? tenant = selectionCookie.Unprotect(context.Request.Cookies["__Host-nexa-customer-tenant"]);
    string? subjectId = context.User.FindFirstValue("sub");
    string? accessToken = await context.GetTokenAsync("CustomerCookie", "access_token");
    if (tenant is null || string.IsNullOrWhiteSpace(subjectId) || subjectId != tenant.SubjectId || string.IsNullOrWhiteSpace(accessToken))
        return Results.Unauthorized();

    HttpResponseMessage accessResponse = await CallPlatformDirectoryAsync(context, clients, "api/platform-directory/v1/me/access", cancellationToken);
    if (!accessResponse.IsSuccessStatusCode) return await ForwardJsonAsync(accessResponse, cancellationToken);
    CurrentPlatformAccessResponse? access = await accessResponse.Content.ReadFromJsonAsync<CurrentPlatformAccessResponse>(cancellationToken: cancellationToken);
    bool stillGranted = access?.SubjectId == tenant.SubjectId && access.Organizations.Any(item =>
        item.OrganizationId == tenant.OrganizationId && item.ApplicationCode == tenant.ApplicationCode);
    if (!stillGranted) return Results.Forbid();

    using HttpResponseMessage response = await catalog.GetMenuAsync(tenant, branchId, accessToken, cancellationToken);
    return await ForwardJsonAsync(response, cancellationToken);
}).RequireAuthorization("CustomerSession");

app.MapGet("/bff/customer/inventory/branches/{branchId:guid}/stock", async (
    Guid branchId,
    HttpContext context,
    IHttpClientFactory clients,
    ICustomerInventoryPort inventory,
    TenantSelectionCookie selectionCookie,
    CancellationToken cancellationToken) =>
{
    TenantContext? tenant = selectionCookie.Unprotect(context.Request.Cookies["__Host-nexa-customer-tenant"]);
    string? subjectId = context.User.FindFirstValue("sub");
    string? accessToken = await context.GetTokenAsync("CustomerCookie", "access_token");
    if (tenant is null || string.IsNullOrWhiteSpace(subjectId) || subjectId != tenant.SubjectId || string.IsNullOrWhiteSpace(accessToken))
        return Results.Unauthorized();

    HttpResponseMessage accessResponse = await CallPlatformDirectoryAsync(context, clients, "api/platform-directory/v1/me/access", cancellationToken);
    if (!accessResponse.IsSuccessStatusCode) return await ForwardJsonAsync(accessResponse, cancellationToken);
    CurrentPlatformAccessResponse? access = await accessResponse.Content.ReadFromJsonAsync<CurrentPlatformAccessResponse>(cancellationToken: cancellationToken);
    bool stillGranted = access?.SubjectId == tenant.SubjectId && access.Organizations.Any(item =>
        item.OrganizationId == tenant.OrganizationId && item.ApplicationCode == tenant.ApplicationCode);
    if (!stillGranted) return Results.Forbid();

    using HttpResponseMessage response = await inventory.GetStockAsync(tenant, branchId, accessToken, cancellationToken);
    return await ForwardJsonAsync(response, cancellationToken);
}).RequireAuthorization("CustomerSession");

app.MapControllers();
app.Run();

static async Task<HttpResponseMessage> CallPlatformDirectoryAsync(HttpContext context, IHttpClientFactory clients, string path, CancellationToken cancellationToken)
{
    string? token = await context.GetTokenAsync("CustomerCookie", "access_token");
    if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Customer BFF session has no access token.");
    HttpClient client = clients.CreateClient("PlatformDirectory");
    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    return await client.GetAsync(path, cancellationToken);
}

static async Task<IResult> ForwardJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
{
    string content = await response.Content.ReadAsStringAsync(cancellationToken);
    return Results.Content(content, "application/json", statusCode: (int)response.StatusCode);
}

static string NormalizeReturnUrl(string? returnUrl) =>
    string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith("/", StringComparison.Ordinal)
        || returnUrl.StartsWith("//", StringComparison.Ordinal)
        ? "/"
        : returnUrl;

public sealed record TenantSelectionRequest(Guid OrganizationId, string ApplicationCode);
public partial class Program;
