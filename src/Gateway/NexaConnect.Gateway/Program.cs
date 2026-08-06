using NexaConnect.Infrastructure.Authentication;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddNexaConnectApiAuthentication(builder.Configuration);
builder.Services.AddAuthentication()
    .AddCookie("BffCookie", options =>
    {
        options.Cookie.Name = "__Host-nexa-bff";
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
    })
    .AddOpenIdConnect("BffOidc", options =>
    {
        IConfigurationSection settings = builder.Configuration.GetRequiredSection("Bff");
        options.Authority = settings["Authority"]!;
        options.RequireHttpsMetadata = settings.GetValue<bool>("RequireHttpsMetadata");
        options.ClientId = "nexaconnect-web-bff";
        options.ClientSecret = settings["ClientSecret"]!;
        options.ResponseType = "code";
        options.UsePkce = true;
        options.SaveTokens = true;
        options.SignInScheme = "BffCookie";
        options.Scope.Add("nexaconnect-api");
    });
builder.Services.AddHttpClient("POS");
builder.Services.AddAuthorization(options => options.AddPolicy("BffSession", policy =>
{
    policy.AuthenticationSchemes.Add("BffCookie");
    policy.RequireAuthenticatedUser();
}));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/bff/login", (string? returnUrl) =>
    Results.Challenge(new AuthenticationProperties
    {
        RedirectUri = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl
    }, ["BffOidc"])).AllowAnonymous();
app.MapGet("/bff/logout", () =>
    Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, ["BffCookie", "BffOidc"])).AllowAnonymous();
app.MapGet("/", () => Results.Content(
    "NexaConnect BFF is running. Use /bff/login to sign in.",
    "text/plain")).AllowAnonymous();
app.MapGet("/bff/me", async (HttpContext context) =>
{
    AuthenticateResult session = await context.AuthenticateAsync("BffCookie");
    return !session.Succeeded || session.Principal is null
        ? Results.Unauthorized()
        : Results.Ok(new
        {
            Subject = session.Principal.FindFirst("sub")?.Value
                ?? session.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            Username = session.Principal.FindFirst("preferred_username")?.Value
                ?? session.Principal.Identity?.Name,
            Roles = session.Principal.FindAll("roles")
                .Concat(session.Principal.FindAll(ClaimTypes.Role))
                .Select(claim => claim.Value)
                .Distinct()
        });
}).RequireAuthorization("BffSession");
app.MapPost("/bff/pos/shifts/open", async (
    BffOpenShiftRequest request,
    HttpContext context,
    IHttpClientFactory clients,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    string? accessToken = await context.GetTokenAsync("BffCookie", "access_token");
    if (string.IsNullOrWhiteSpace(accessToken)) return Results.Unauthorized();
    var pos = clients.CreateClient("POS");
    pos.BaseAddress = new Uri(configuration["Services:POS"]
        ?? throw new InvalidOperationException("Services:POS is required."));
    pos.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
        "Bearer", accessToken);
    using HttpResponseMessage response = await pos.PostAsJsonAsync(
        "api/pos/v1/shifts/open", request, cancellationToken);
    string content = await response.Content.ReadAsStringAsync(cancellationToken);
    return Results.Content(content, "application/json", statusCode: (int)response.StatusCode);
}).RequireAuthorization("BffSession");
app.MapPost("/bff/pos/shifts/{shiftId:guid}/close", async (
    Guid shiftId,
    HttpContext context,
    IHttpClientFactory clients,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    string? accessToken = await context.GetTokenAsync("BffCookie", "access_token");
    if (string.IsNullOrWhiteSpace(accessToken)) return Results.Unauthorized();
    var pos = clients.CreateClient("POS");
    pos.BaseAddress = new Uri(configuration["Services:POS"]
        ?? throw new InvalidOperationException("Services:POS is required."));
    pos.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
        "Bearer", accessToken);
    using HttpResponseMessage response = await pos.PostAsync(
        $"api/pos/v1/shifts/{shiftId}/close", content: null, cancellationToken);
    if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return Results.NoContent();
    string content = await response.Content.ReadAsStringAsync(cancellationToken);
    return Results.Content(content, "application/json", statusCode: (int)response.StatusCode);
}).RequireAuthorization("BffSession");

app.MapControllers();

app.Run();

public partial class Program;

public sealed record BffOpenShiftRequest(Guid BranchId, Guid StoreId, Guid TerminalId, string ShiftNumber);
