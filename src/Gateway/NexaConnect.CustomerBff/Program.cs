using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using NexaConnect.Contracts.Platform;
using NexaConnect.CustomerBff;
using NexaConnect.CustomerBff.Application.Catalog;
using NexaConnect.CustomerBff.Application.Inventory;
using NexaConnect.CustomerBff.Application.Orders;
using NexaConnect.CustomerBff.Infrastructure.Catalog;
using NexaConnect.CustomerBff.Infrastructure.Inventory;
using NexaConnect.CustomerBff.Infrastructure.Orders;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.AddNexaConnectObservability("nexaconnect-customer-bff");
NexaConnect.Infrastructure.Authentication.AuthenticationServiceCollectionExtensions.EnsureProductionHttps(builder.Configuration, builder.Environment);
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddNexaConnectDataProtection(builder.Configuration, builder.Environment, "customer-bff");
builder.Services.AddNexaConnectBffSessionCache(builder.Configuration, builder.Environment);
builder.Services.AddSingleton<ITicketStore, DistributedCacheTicketStore>();
builder.Services.AddOptions<CookieAuthenticationOptions>("CustomerCookie")
    .Configure<ITicketStore>((options, ticketStore) => options.SessionStore = ticketStore);
builder.Services.AddSingleton<TenantSelectionCookie>();
builder.Services.AddHttpClient(nameof(BffAccessTokenService));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<BffAccessTokenService>();
builder.Services.AddHttpClient("PlatformDirectory", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:PlatformDirectory"]
        ?? throw new InvalidOperationException("Services:PlatformDirectory is required."));
}).AddNexaConnectCorrelationPropagation();
builder.Services.AddHttpClient("Restaurant",client=>client.BaseAddress=new Uri(builder.Configuration["Services:Restaurant"]??throw new InvalidOperationException("Services:Restaurant is required."))).AddNexaConnectCorrelationPropagation();
builder.Services.AddHttpClient("Reporting",client=>client.BaseAddress=new Uri(builder.Configuration["Services:Reporting"]??throw new InvalidOperationException("Services:Reporting is required."))).AddNexaConnectCorrelationPropagation();
builder.Services.AddHttpClient("Media",client=>client.BaseAddress=new Uri(builder.Configuration["Services:Media"]??throw new InvalidOperationException("Services:Media is required."))).AddNexaConnectCorrelationPropagation();
builder.Services.AddHttpClient<ICustomerCatalogPort, HttpCustomerCatalogPort>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Catalog"]
        ?? throw new InvalidOperationException("Services:Catalog is required."));
}).AddNexaConnectCorrelationPropagation();
builder.Services.AddHttpClient<ICustomerInventoryPort, HttpCustomerInventoryPort>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Inventory"]
        ?? throw new InvalidOperationException("Services:Inventory is required."));
}).AddNexaConnectCorrelationPropagation();
builder.Services.AddHttpClient<ICustomerOrderPort, HttpCustomerOrderPort>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Order"]
        ?? throw new InvalidOperationException("Services:Order is required."));
}).AddNexaConnectCorrelationPropagation();
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
        options.LoginPath = "/bff/customer/login";
        options.LogoutPath = "/bff/customer/logout";
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/bff/customer"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/bff/customer"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
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
        // The local Keycloak realm does not advertise a usable PAR client configuration.
        // Keep PAR available for production providers, but use the normal authorization
        // request for the Development realm.
        if (builder.Environment.IsDevelopment())
            options.PushedAuthorizationBehavior = PushedAuthorizationBehavior.Disable;
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
app.UseNexaConnectRequestLogging();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = item => item.Context.Response.Headers.CacheControl =
        item.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase) ? "no-store" : "public,max-age=31536000,immutable"
});
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok(new { Status = "Healthy" })).AllowAnonymous();
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
    string? accessToken = await GetCustomerAccessTokenAsync(context, cancellationToken);
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
    string? accessToken = await GetCustomerAccessTokenAsync(context, cancellationToken);
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

app.MapPost("/bff/customer/orders/branches/{branchId:guid}/place", async (
    Guid branchId,
    CustomerPlaceOrderRequest request,
    HttpContext context,
    IHttpClientFactory clients,
    ICustomerOrderPort orders,
    TenantSelectionCookie selectionCookie,
    CancellationToken cancellationToken) =>
{
    TenantContext? tenant = selectionCookie.Unprotect(context.Request.Cookies["__Host-nexa-customer-tenant"]);
    string? subjectId = context.User.FindFirstValue("sub");
    string? accessToken = await GetCustomerAccessTokenAsync(context, cancellationToken);
    if (tenant is null || string.IsNullOrWhiteSpace(subjectId) || subjectId != tenant.SubjectId || string.IsNullOrWhiteSpace(accessToken))
        return Results.Unauthorized();

    HttpResponseMessage accessResponse = await CallPlatformDirectoryAsync(context, clients, "api/platform-directory/v1/me/access", cancellationToken);
    if (!accessResponse.IsSuccessStatusCode) return await ForwardJsonAsync(accessResponse, cancellationToken);
    CurrentPlatformAccessResponse? access = await accessResponse.Content.ReadFromJsonAsync<CurrentPlatformAccessResponse>(cancellationToken: cancellationToken);
    bool stillGranted = access?.SubjectId == tenant.SubjectId && access.Organizations.Any(item =>
        item.OrganizationId == tenant.OrganizationId && item.ApplicationCode == tenant.ApplicationCode);
    if (!stillGranted) return Results.Forbid();

    using HttpResponseMessage response = await orders.PlaceAsync(tenant, branchId, request, accessToken, cancellationToken);
    return await ForwardJsonAsync(response, cancellationToken);
}).RequireAuthorization("CustomerSession");

app.MapGet("/bff/customer/memberships", async (HttpContext context, IHttpClientFactory clients, TenantSelectionCookie cookie, ILogger<Program> logger, CancellationToken cancellationToken) =>
{
    TenantContext? tenant = ValidTenant(context, cookie);
    if (tenant is null || tenant.ApplicationCode != "nexa_connect") return Results.Unauthorized();
    using HttpResponseMessage response = await CallPlatformDirectoryAsync(context, clients, $"api/platform-directory/v1/customer/organizations/{tenant.OrganizationId:D}/members", cancellationToken);
    if (!response.IsSuccessStatusCode) logger.LogWarning("Customer membership list downstream failure for organization {OrganizationId}, status {StatusCode}", tenant.OrganizationId, (int)response.StatusCode);
    return await ForwardJsonAsync(response, cancellationToken);
}).RequireAuthorization("CustomerSession");

app.MapPut("/bff/customer/memberships/{subjectId}", async (string subjectId, ChangeCustomerMembershipRequest request, HttpContext context, IHttpClientFactory clients, TenantSelectionCookie cookie, ILogger<Program> logger, CancellationToken cancellationToken) =>
{
    TenantContext? tenant = ValidTenant(context, cookie);
    if (tenant is null || tenant.ApplicationCode != "nexa_connect") return Results.Unauthorized();
    string? token = await GetCustomerAccessTokenAsync(context, cancellationToken); if (string.IsNullOrWhiteSpace(token)) return Results.Unauthorized();
    HttpClient client = clients.CreateClient("PlatformDirectory");
    using var downstream = new HttpRequestMessage(HttpMethod.Put, $"api/platform-directory/v1/customer/organizations/{tenant.OrganizationId:D}/members/{Uri.EscapeDataString(subjectId)}") { Content = JsonContent.Create(request) };
    downstream.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    using HttpResponseMessage response = await client.SendAsync(downstream, cancellationToken);
    if (!response.IsSuccessStatusCode) logger.LogWarning("Customer membership change downstream failure for organization {OrganizationId}, target {TargetSubjectId}, status {StatusCode}", tenant.OrganizationId, subjectId, (int)response.StatusCode);
    return await ForwardJsonAsync(response, cancellationToken);
}).RequireAuthorization("CustomerSession");

app.MapMethods("/bff/customer/branches/{branchId?}",["GET","POST","PUT"],async(HttpContext context,IHttpClientFactory clients,TenantSelectionCookie cookie,ILogger<Program> logger,CancellationToken c)=>
{
 TenantContext? tenant=ValidTenant(context,cookie);if(tenant is null||tenant.ApplicationCode!="nexa_connect")return Results.Unauthorized();string? token=await GetCustomerAccessTokenAsync(context,c);if(string.IsNullOrWhiteSpace(token))return Results.Unauthorized();
 string suffix=context.Request.RouteValues["branchId"] is { } id?"/"+Uri.EscapeDataString(id.ToString()!):"";using var request=new HttpRequestMessage(new HttpMethod(context.Request.Method),$"api/restaurant/v1/customer/organizations/{tenant.OrganizationId:D}/branches{suffix}");request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",token);if(context.Request.ContentLength>0)request.Content=await ReplayableJsonContent(context.Request,c);using HttpResponseMessage response=await clients.CreateClient("Restaurant").SendAsync(request,c);if(!response.IsSuccessStatusCode)logger.LogWarning("Customer branch downstream failure for organization {OrganizationId}, method {Method}, status {StatusCode}",tenant.OrganizationId,context.Request.Method,(int)response.StatusCode);return await ForwardJsonAsync(response,c);
}).RequireAuthorization("CustomerSession");

app.MapMethods("/bff/customer/configuration/branches/{branchId:guid}",["GET","PUT"],async(Guid branchId,HttpContext context,IHttpClientFactory clients,TenantSelectionCookie cookie,ILogger<Program> logger,CancellationToken c)=>
{
 TenantContext? tenant=ValidTenant(context,cookie);if(tenant is null||tenant.ApplicationCode!="nexa_connect")return Results.Unauthorized();string? token=await GetCustomerAccessTokenAsync(context,c);if(string.IsNullOrWhiteSpace(token))return Results.Unauthorized();using var request=new HttpRequestMessage(new HttpMethod(context.Request.Method),$"api/restaurant/v1/customer/organizations/{tenant.OrganizationId:D}/configuration/branches/{branchId:D}");request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",token);if(context.Request.ContentLength>0)request.Content=await ReplayableJsonContent(context.Request,c);using HttpResponseMessage response=await clients.CreateClient("Restaurant").SendAsync(request,c);if(!response.IsSuccessStatusCode)logger.LogWarning("Customer configuration downstream failure for organization {OrganizationId}, branch {BranchId}, method {Method}, status {StatusCode}",tenant.OrganizationId,branchId,context.Request.Method,(int)response.StatusCode);return await ForwardJsonAsync(response,c);
}).RequireAuthorization("CustomerSession");

app.MapGet("/bff/customer/dashboard",async(HttpContext context,IHttpClientFactory clients,TenantSelectionCookie cookie,ILogger<Program> logger,CancellationToken c)=>await ProxyCustomerQuery("Reporting","api/reporting/v1/customer/organizations/{organizationId}/dashboard",context,clients,cookie,logger,c)).RequireAuthorization("CustomerSession");
app.MapGet("/bff/customer/reports/sales",async(HttpContext context,IHttpClientFactory clients,TenantSelectionCookie cookie,ILogger<Program> logger,CancellationToken c)=>await ProxyCustomerQuery("Reporting","api/reporting/v1/customer/organizations/{organizationId}/reports/sales",context,clients,cookie,logger,c)).RequireAuthorization("CustomerSession");
app.MapGet("/bff/customer/media",async(HttpContext context,IHttpClientFactory clients,TenantSelectionCookie cookie,ILogger<Program> logger,CancellationToken c)=>await ProxyCustomerQuery("Media","api/media/v1/customer/organizations/{organizationId}/assets",context,clients,cookie,logger,c)).RequireAuthorization("CustomerSession");

// Phase 8 owns the browser experience, but these capabilities still require versioned,
// product-owned contracts. Return an explicitly tenant-bound availability response until
// each owning service publishes its API rather than querying another service's data.
app.MapGet("/bff/customer/features/{feature}", async (
    string feature,
    HttpContext context,
    IHttpClientFactory clients,
    TenantSelectionCookie selectionCookie,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    string[] allowed = ["activity"];
    if (!allowed.Contains(feature, StringComparer.Ordinal)) return Results.NotFound();
    TenantContext? tenant = selectionCookie.Unprotect(context.Request.Cookies["__Host-nexa-customer-tenant"]);
    string? subjectId = context.User.FindFirstValue("sub");
    if (tenant is null || subjectId != tenant.SubjectId)
    {
        logger.LogWarning("Customer feature access denied because tenant context was absent or did not match the session for feature {Feature}", feature);
        return Results.Unauthorized();
    }

    using HttpResponseMessage accessResponse = await CallPlatformDirectoryAsync(context, clients, "api/platform-directory/v1/me/access", cancellationToken);
    if (!accessResponse.IsSuccessStatusCode)
    {
        logger.LogWarning("Customer feature access validation failed for organization {OrganizationId}, application {ApplicationCode}, feature {Feature}, status {StatusCode}", tenant.OrganizationId, tenant.ApplicationCode, feature, (int)accessResponse.StatusCode);
        return await ForwardJsonAsync(accessResponse, cancellationToken);
    }
    CurrentPlatformAccessResponse? access = await accessResponse.Content.ReadFromJsonAsync<CurrentPlatformAccessResponse>(cancellationToken: cancellationToken);
    bool stillGranted = access?.SubjectId == tenant.SubjectId && access.Organizations.Any(item =>
        item.OrganizationId == tenant.OrganizationId && item.ApplicationCode == tenant.ApplicationCode);
    if (!stillGranted)
    {
        logger.LogWarning("Customer feature access was revoked for organization {OrganizationId}, application {ApplicationCode}, feature {Feature}", tenant.OrganizationId, tenant.ApplicationCode, feature);
        return Results.Forbid();
    }

    return Results.Ok(new
    {
        Status = "contract-pending",
        Message = $"{feature} is not available until the active product publishes its tenant-aware API contract.",
        tenant.OrganizationId,
        tenant.ApplicationCode,
        Items = Array.Empty<object>()
    });
}).RequireAuthorization("CustomerSession");

app.MapControllers();
app.MapFallbackToFile("index.html").AllowAnonymous();
app.Run();

static async Task<IResult> ProxyCustomerQuery(string clientName,string path,HttpContext context,IHttpClientFactory clients,TenantSelectionCookie cookie,ILogger logger,CancellationToken cancellationToken)
{
 TenantContext? tenant=ValidTenant(context,cookie);if(tenant is null||tenant.ApplicationCode!="nexa_connect")return Results.Unauthorized();string? token=await GetCustomerAccessTokenAsync(context,cancellationToken);if(string.IsNullOrWhiteSpace(token))return Results.Unauthorized();string query=context.Request.QueryString.Value??"";using var request=new HttpRequestMessage(HttpMethod.Get,path.Replace("{organizationId}",tenant.OrganizationId.ToString("D"))+query);request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",token);using HttpResponseMessage response=await clients.CreateClient(clientName).SendAsync(request,cancellationToken);if(!response.IsSuccessStatusCode)logger.LogWarning("Customer {DownstreamService} query failed for organization {OrganizationId}, status {StatusCode}",clientName,tenant.OrganizationId,(int)response.StatusCode);return await ForwardJsonAsync(response,cancellationToken);
}

static async Task<HttpResponseMessage> CallPlatformDirectoryAsync(HttpContext context, IHttpClientFactory clients, string path, CancellationToken cancellationToken)
{
    string? token = await GetCustomerAccessTokenAsync(context, cancellationToken);
    if (string.IsNullOrWhiteSpace(token))
        return new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized);
    HttpClient client = clients.CreateClient("PlatformDirectory");
    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    return await client.GetAsync(path, cancellationToken);
}

static Task<string?> GetCustomerAccessTokenAsync(HttpContext context, CancellationToken cancellationToken)
{
    IConfiguration configuration = context.RequestServices.GetRequiredService<IConfiguration>();
    IConfigurationSection bff = configuration.GetRequiredSection("Bff");
    return context.RequestServices.GetRequiredService<BffAccessTokenService>().GetValidAccessTokenAsync(
        context, "CustomerCookie", bff["Authority"]!, bff["ClientId"]!, bff["ClientSecret"]!, cancellationToken);
}

static TenantContext? ValidTenant(HttpContext context, TenantSelectionCookie cookie)
{
    TenantContext? tenant = cookie.Unprotect(context.Request.Cookies["__Host-nexa-customer-tenant"]);
    return tenant is not null && tenant.SubjectId == context.User.FindFirstValue("sub") ? tenant : null;
}

static async Task<HttpContent> ReplayableJsonContent(HttpRequest request,CancellationToken c){using var reader=new StreamReader(request.Body);string value=await reader.ReadToEndAsync(c);return new StringContent(value,System.Text.Encoding.UTF8,request.ContentType??"application/json");}

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
