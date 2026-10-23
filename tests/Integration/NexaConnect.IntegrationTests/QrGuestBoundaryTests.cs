extern alias CUSTOMERBFF;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NexaConnect.Contracts.Platform;
using NexaConnect.Contracts.RestaurantQr;
using CUSTOMERBFF::NexaConnect.CustomerBff.Application.Qr;

namespace NexaConnect.IntegrationTests;

public sealed class QrGuestBoundaryTests
{
    [Fact] public async Task Staff_administration_requires_csrf_protected_tenant_and_live_membership()
    {
        await using var factory = new Factory(); using var client = factory.CreateClient(Options()); string root = "/bff/customer/qr-entries/branches/" + Guid.NewGuid();
        await SignIn(factory, client);
        using var noCsrf = await client.PostAsJsonAsync(root, new CreateQrEntry("A1")); Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode); Assert.Equal(0, factory.Port.StaffSends);
        var csrf = await client.GetFromJsonAsync<JsonElement>("/bff/customer/qr-entries/csrf"); client.DefaultRequestHeaders.Add("X-Nexa-CSRF", csrf.GetProperty("requestToken").GetString());
        using var issue = await client.PostAsJsonAsync(root + "?organizationId=" + Guid.NewGuid(), new CreateQrEntry("A1")); Assert.Equal(HttpStatusCode.OK, issue.StatusCode);
        Assert.Equal(factory.Port.Organization, factory.Port.LastTenant!.OrganizationId); Assert.Equal(1, factory.Port.StaffSends);
        Assert.Contains("https://localhost/order#", await issue.Content.ReadAsStringAsync()); Assert.True(issue.Headers.CacheControl?.NoStore);
        factory.Port.Membership = false; using var revoked = await client.GetAsync(root); Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode); Assert.Equal(1, factory.Port.StaffSends);
        factory.Port.Membership = true; await SignIn(factory, client, "another-subject"); using var wrongSubject = await client.GetAsync(root); Assert.Equal(HttpStatusCode.Unauthorized, wrongSubject.StatusCode);
        await SignIn(factory, client, product: "another-product"); using var wrongProduct = await client.GetAsync(root); Assert.Equal(HttpStatusCode.Forbidden, wrongProduct.StatusCode); Assert.Equal(1, factory.Port.StaffSends);
    }
    [Fact] public async Task Visitors_use_independent_secure_cookies_and_only_public_context()
    {
        await using var factory = new Factory(); using var a = factory.CreateClient(Options()); using var b = factory.CreateClient(Options());
        using var missing = await a.GetAsync("/bff/guest/qr/context"); Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        foreach (var client in new[] { a, b }) client.DefaultRequestHeaders.Add("Origin", "https://localhost");
        using var first = await a.PostAsJsonAsync("/bff/guest/qr/sessions", new QrEntryExchange(new string('E', 43)));
        using var second = await b.PostAsJsonAsync("/bff/guest/qr/sessions", new QrEntryExchange(new string('E', 43)));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode); Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        string cookie = first.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains("__Host-nexa-qr-guest=", cookie); Assert.Contains("secure", cookie); Assert.Contains("httponly", cookie); Assert.Contains("samesite=strict", cookie); Assert.Contains("path=/", cookie); Assert.DoesNotContain("domain=", cookie);
        string json = await first.Content.ReadAsStringAsync(); Assert.DoesNotContain("sessionToken", json); Assert.DoesNotContain("organizationId", json); Assert.DoesNotContain(new string('E', 43), json);
        Assert.True(first.Headers.CacheControl?.NoStore); Assert.Equal("no-referrer", first.Headers.GetValues("Referrer-Policy").Single());
        var firstContext = await a.GetFromJsonAsync<JsonElement>("/bff/guest/qr/context"); var secondContext = await b.GetFromJsonAsync<JsonElement>("/bff/guest/qr/context");
        Assert.NotEqual(firstContext.GetProperty("visitorId"), secondContext.GetProperty("visitorId")); Assert.False(firstContext.GetProperty("orderingAvailable").GetBoolean());
        using var staff = await a.GetAsync("/bff/customer/qr-entries/branches/" + Guid.NewGuid()); Assert.Equal(HttpStatusCode.Unauthorized, staff.StatusCode);
        using var ended = await a.DeleteAsync("/bff/guest/qr/sessions"); Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);
        using var gone = await a.GetAsync("/bff/guest/qr/context"); Assert.Equal(HttpStatusCode.Unauthorized, gone.StatusCode);
        using var alive = await b.GetAsync("/bff/guest/qr/context"); Assert.Equal(HttpStatusCode.OK, alive.StatusCode);
    }
    [Theory][InlineData(null)][InlineData("https://evil.test")][InlineData("null")]
    public async Task Guest_mutations_require_exact_origin_without_consulting_staff_identity(string? origin)
    {
        await using var factory = new Factory(); using var client = factory.CreateClient(Options());
        if (origin is not null) client.DefaultRequestHeaders.Add("Origin", origin);
        client.DefaultRequestHeaders.Add("Authorization", "Bearer not-a-guest-capability");
        using var response = await client.PostAsJsonAsync("/bff/guest/qr/sessions", new QrEntryExchange(new string('E', 43)));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); Assert.Equal(0, factory.Port.Starts);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }
    [Fact] public async Task Missing_cookie_cannot_be_replaced_by_a_header_or_query_token_and_limits_are_enforced()
    {
        await using var factory = new Factory(); using var client = factory.CreateClient(Options()); client.DefaultRequestHeaders.Add("X-Nexa-Guest-Session", new string('X', 43));
        for (int i = 0; i < 60; i++) { using var response = await client.GetAsync("/bff/guest/qr/context?sessionToken=ignored"); Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); }
        using var limited = await client.GetAsync("/bff/guest/qr/context"); Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode); Assert.Equal("60", limited.Headers.RetryAfter?.ToString());
    }
    [Fact] public async Task Disabled_feature_and_safe_dependency_errors_do_not_issue_cookies()
    {
        await using (var factory = new Factory(false)) { using var client = factory.CreateClient(Options()); using var response = await client.GetAsync("/bff/guest/qr/context"); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); }
        await using var enabled = new Factory(); using var browser = enabled.CreateClient(Options()); browser.DefaultRequestHeaders.Add("Origin", "https://localhost");
        enabled.Port.Status = 500; using var failed = await browser.PostAsJsonAsync("/bff/guest/qr/sessions", new QrEntryExchange(new string('E', 43)));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode); Assert.False(failed.Headers.Contains("Set-Cookie")); Assert.DoesNotContain("downstream-secret", await failed.Content.ReadAsStringAsync());
    }
    private static WebApplicationFactoryClientOptions Options() => new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") };
    private static async Task SignIn(Factory factory, HttpClient client, string subject = "operator", string product = "nexa_connect")
    {
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get("CustomerCookie");
        var properties = new AuthenticationProperties(); properties.StoreTokens([new AuthenticationToken { Name="access_token",Value="server-held-test-token" },new AuthenticationToken { Name="expires_at",Value=DateTimeOffset.UtcNow.AddHours(1).ToString("O") }]);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub","operator"),new Claim(ClaimTypes.NameIdentifier,"operator")],"CustomerCookie"));
        string key = await options.SessionStore!.StoreAsync(new AuthenticationTicket(principal,properties,"CustomerCookie"));
        var cookiePrincipal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("Microsoft.AspNetCore.Authentication.Cookies-SessionId",key)],"CustomerCookie"));
        string session = options.TicketDataFormat.Protect(new AuthenticationTicket(cookiePrincipal,null,"CustomerCookie"));
        string tenant = factory.Services.GetRequiredService<CUSTOMERBFF::NexaConnect.CustomerBff.TenantSelectionCookie>().Protect(new(subject,factory.Port.Organization,product));
        client.DefaultRequestHeaders.Remove("Cookie"); client.DefaultRequestHeaders.Add("Cookie",$"{options.Cookie.Name}={session}; __Host-nexa-customer-tenant={tenant}");
    }
    private sealed class Factory(bool enabled = true) : WebApplicationFactory<CUSTOMERBFF::Program>
    {
        public Port Port { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing"); builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bff:Authority"]="https://identity.test/realms/test",["Bff:ClientId"]="test",["Bff:ClientSecret"]="test-secret",
                ["Services:PlatformDirectory"]="https://directory.test/",["Services:Order"]="https://order.test/",
                ["Services:Restaurant"]="https://restaurant.test/",["Services:Reporting"]="https://reporting.test/",
                ["Services:Media"]="https://media.test/",["Services:Notification"]="https://notification.test/",
                ["Services:Catalog"]="https://catalog.test/",["Services:Inventory"]="https://inventory.test/",
                ["QrGuest:Enabled"]=enabled.ToString(),["QrGuest:PublicOrigin"]="https://localhost"
            }));
            builder.ConfigureTestServices(services => { services.RemoveAll<IQrGatewayPort>(); services.AddSingleton<IQrGatewayPort>(Port); });
        }
    }
    private sealed class Port : IQrGatewayPort
    {
        private readonly Dictionary<string, QrGuestContext> sessions = []; public int Starts; public int Status = 200;
        public Guid Organization = Guid.NewGuid(); public bool Membership = true; public int StaffSends; public TenantContext? LastTenant;
        public Task<CurrentPlatformAccessResponse?> AccessAsync(string bearer, CancellationToken ct) => Task.FromResult<CurrentPlatformAccessResponse?>(new("operator", Membership ? [new(Organization,"org","Organization","nexa_connect")] : []));
        public Task<QrGatewayReply> StaffAsync(TenantContext tenant, string bearer, Guid branch, QrAdminOperation operation, Guid? entry, object? body, CancellationToken ct)
        { Assert.Equal("server-held-test-token",bearer); StaffSends++;LastTenant=tenant;return Task.FromResult(new QrGatewayReply(200,JsonSerializer.SerializeToElement(new QrEntryIssue(new(Guid.NewGuid(),"A1",1,true,DateTimeOffset.UtcNow.AddDays(30)),new string('E',43)),JsonSerializerOptions.Web))); }
        public Task<QrGatewayReply> GuestAsync(string operation, string? session, QrEntryExchange? input, CancellationToken ct)
        {
            if (Status != 200) return Task.FromResult(new QrGatewayReply(Status, JsonSerializer.SerializeToElement(new { secret = "downstream-secret" })));
            if (operation == "start")
            {
                Assert.Null(session); Starts++; string token = "v" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..10];
                var context = new QrGuestContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Restaurant", "Branch", "A1", "THB", DateTimeOffset.UtcNow.AddHours(2)); sessions.Add(token, context);
                return Task.FromResult(new QrGatewayReply(200, JsonSerializer.SerializeToElement(new QrSessionIssue(token, context), JsonSerializerOptions.Web)));
            }
            if (session is null || !sessions.TryGetValue(session, out var value)) return Task.FromResult(new QrGatewayReply(401, null));
            if (operation == "end") { sessions.Remove(session); return Task.FromResult(new QrGatewayReply(204, null)); }
            return Task.FromResult(new QrGatewayReply(200, JsonSerializer.SerializeToElement(value, JsonSerializerOptions.Web)));
        }
    }
}
