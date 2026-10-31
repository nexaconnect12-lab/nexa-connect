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
    [Fact]public async Task Menu_organization_guest_route_uses_cookie_and_drops_private_fields()
    {
        await using var f=new Factory();using var c=f.CreateClient(Options());
        Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync("/bff/guest/qr/menu-organization")).StatusCode);
        c.DefaultRequestHeaders.Add("Origin","https://localhost");await c.PostAsJsonAsync("/bff/guest/qr/sessions",new QrEntryExchange(new string('E',43)));
        using var r=await c.GetAsync("/bff/guest/qr/menu-organization");Assert.Equal(HttpStatusCode.OK,r.StatusCode);Assert.True(r.Headers.CacheControl?.NoStore);
        var body=await r.Content.ReadAsStringAsync();Assert.DoesNotContain("internalSecret",body);Assert.DoesNotContain("organizationId",body);Assert.DoesNotContain("branchId",body);Assert.DoesNotContain("version",body);
    }
    [Fact]public async Task Menu_organization_staff_write_requires_csrf_subject_and_current_membership()
    {
        await using var f=new Factory();using var c=f.CreateClient(Options());await SignIn(f,c);var branch=Guid.NewGuid();string url=$"/bff/customer/menu-organization/branches/{branch}";
        var update=new NexaConnect.Contracts.Catalog.UpdateMenuOrganization(0,[],[]);
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PutAsJsonAsync(url,update)).StatusCode);
        var csrf=await c.GetFromJsonAsync<JsonElement>("/bff/customer/menu-organization/csrf");c.DefaultRequestHeaders.Add("X-Nexa-CSRF",csrf.GetProperty("requestToken").GetString());
        using var saved=await c.PutAsJsonAsync(url,update);Assert.Equal(HttpStatusCode.OK,saved.StatusCode);Assert.Equal(1,f.Layout!.Sends);Assert.Equal(f.Port.Organization,f.Layout.LastTenant!.OrganizationId);
        f.Port.Membership=false;Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync(url)).StatusCode);Assert.Equal(1,f.Layout.Sends);
        f.Port.Membership=true;await SignIn(f,c,"another-subject");Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync(url)).StatusCode);
    }
    [Fact]public async Task Acceptance_and_price_confirmation_keep_staff_csrf_and_guest_origin_authority_separate()
    {
        await using var factory=new Factory(true,true,true);using var guest=factory.CreateClient(Options());Guid id=Guid.NewGuid();string confirm=$"/bff/guest/qr/requests/{id}/confirm-price";
        guest.DefaultRequestHeaders.Add("Origin","https://localhost");using(var missing=await guest.PostAsJsonAsync(confirm,new QrPriceConfirmation(2,new string('A',64))))Assert.Equal(HttpStatusCode.Unauthorized,missing.StatusCode);
        await guest.PostAsJsonAsync("/bff/guest/qr/sessions",new QrEntryExchange(new string('E',43)));guest.DefaultRequestHeaders.Remove("Origin");using(var cross=await guest.PostAsJsonAsync(confirm,new QrPriceConfirmation(2,new string('A',64))))Assert.Equal(HttpStatusCode.Forbidden,cross.StatusCode);
        guest.DefaultRequestHeaders.Add("Origin","https://localhost");using(var own=await guest.PostAsJsonAsync(confirm,new QrPriceConfirmation(2,new string('A',64))))Assert.Equal(HttpStatusCode.OK,own.StatusCode);
        using var staff=factory.CreateClient(Options());await SignIn(factory,staff);string root=$"/bff/customer/qr-requests/branches/{Guid.NewGuid()}/{id}/accept";
        using(var noCsrf=await staff.PostAsJsonAsync(root,new QrAcceptance(1)))Assert.Equal(HttpStatusCode.BadRequest,noCsrf.StatusCode);
        var csrf=await staff.GetFromJsonAsync<JsonElement>("/bff/customer/qr-requests/csrf");staff.DefaultRequestHeaders.Add("X-Nexa-CSRF",csrf.GetProperty("requestToken").GetString());
        using(var accepted=await staff.PostAsJsonAsync(root,new QrAcceptance(1)))Assert.Equal(HttpStatusCode.OK,accepted.StatusCode);
        factory.Port.Membership=false;using var denied=await staff.PostAsJsonAsync(root,new QrAcceptance(1));Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
    }
    [Fact]public async Task Request_bff_cookie_origin_privacy_staff_csrf_and_live_membership_are_independent()
    {
        await using var factory=new Factory(true,true);using var guest=factory.CreateClient(Options());
        guest.DefaultRequestHeaders.Add("Origin","https://localhost");await guest.PostAsJsonAsync("/bff/guest/qr/sessions",new QrEntryExchange(new string('E',43)));
        var input=new QrSubmission(Guid.NewGuid(),new string('A',64));
        guest.DefaultRequestHeaders.Remove("Origin");using(var forbidden=await guest.PostAsJsonAsync("/bff/guest/qr/requests",input))Assert.Equal(HttpStatusCode.Forbidden,forbidden.StatusCode);
        guest.DefaultRequestHeaders.Add("Origin","https://localhost");using var submitted=await guest.PostAsJsonAsync("/bff/guest/qr/requests",input);Assert.Equal(HttpStatusCode.OK,submitted.StatusCode);Assert.True(submitted.Headers.CacheControl?.NoStore);
        Assert.DoesNotContain("visitorId",await submitted.Content.ReadAsStringAsync());
        using var other=factory.CreateClient(Options());other.DefaultRequestHeaders.Add("Origin","https://localhost");await other.PostAsJsonAsync("/bff/guest/qr/sessions",new QrEntryExchange(new string('E',43)));
        Assert.Empty((await other.GetFromJsonAsync<QrRequestList>("/bff/guest/qr/requests?visitorId=ignored"))!.Items);
        using(var noStaff=await guest.GetAsync("/bff/customer/qr-requests/branches/"+Guid.NewGuid()))Assert.Equal(HttpStatusCode.Unauthorized,noStaff.StatusCode);
        using var staff=factory.CreateClient(Options());await SignIn(factory,staff);string root="/bff/customer/qr-requests/branches/"+Guid.NewGuid();
        using(var missingCsrf=await staff.PostAsJsonAsync(root+$"/{input.QuoteId}/reject",new QrRejection(1,"closing")))Assert.Equal(HttpStatusCode.BadRequest,missingCsrf.StatusCode);
        var csrf=await staff.GetFromJsonAsync<JsonElement>("/bff/customer/qr-requests/csrf");staff.DefaultRequestHeaders.Add("X-Nexa-CSRF",csrf.GetProperty("requestToken").GetString());
        using(var accepted=await staff.PostAsJsonAsync(root+$"/{input.QuoteId}/reject",new QrRejection(1,"closing")))Assert.Equal(HttpStatusCode.OK,accepted.StatusCode);
        Assert.Equal(factory.Port.Organization,factory.Port.LastTenant!.OrganizationId);
        factory.Port.Membership=false;using var revoked=await staff.GetAsync(root);Assert.Equal(HttpStatusCode.Forbidden,revoked.StatusCode);
        await using var disabled=new Factory();using var absent=disabled.CreateClient(Options());using var response=await absent.GetAsync("/bff/guest/qr/requests");Assert.Equal(HttpStatusCode.NotFound,response.StatusCode);
    }
    [Fact] public async Task Shopping_uses_only_guest_cookie_exact_origin_and_typed_public_responses()
    {
        await using var factory=new Factory();using var client=factory.CreateClient(Options());
        client.DefaultRequestHeaders.Add("X-Nexa-Guest-Session",new string('H',43));
        using(var missing=await client.GetAsync("/bff/guest/qr/menu?sessionToken=ignored"))Assert.Equal(HttpStatusCode.Unauthorized,missing.StatusCode);
        client.DefaultRequestHeaders.Add("Origin","https://localhost");
        using var start=await client.PostAsJsonAsync("/bff/guest/qr/sessions",new QrEntryExchange(new string('E',43)));Assert.Equal(HttpStatusCode.OK,start.StatusCode);
        using var menu=await client.GetAsync("/bff/guest/qr/menu?branchId="+Guid.NewGuid());Assert.Equal(HttpStatusCode.OK,menu.StatusCode);Assert.True(menu.Headers.CacheControl?.NoStore);
        string json=await menu.Content.ReadAsStringAsync();Assert.DoesNotContain("internalSecret",json);Assert.DoesNotContain("preparationStation",json);
        client.DefaultRequestHeaders.Remove("Origin");
        using(var forbidden=await client.PostAsJsonAsync("/bff/guest/qr/quote",new QrCart([new(Guid.NewGuid(),1)])))Assert.Equal(HttpStatusCode.Forbidden,forbidden.StatusCode);
        client.DefaultRequestHeaders.Add("Origin","https://localhost");
        using var tampered=await client.PostAsync("/bff/guest/qr/quote",new StringContent("{\"lines\":[],\"organizationId\":\"foreign\",\"total\":0.01}",System.Text.Encoding.UTF8,"application/json"));Assert.Equal(HttpStatusCode.BadRequest,tampered.StatusCode);
        using(var quote=await client.PostAsJsonAsync("/bff/guest/qr/quote",new QrCart([new(Guid.NewGuid(),1)])))Assert.Equal(HttpStatusCode.Conflict,quote.StatusCode);
        factory.Port.Status=401;using(var revoked=await client.GetAsync("/bff/guest/qr/menu"))Assert.Equal(HttpStatusCode.Unauthorized,revoked.StatusCode);
        Assert.Equal(0,factory.Port.StaffSends);
    }
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
        using var landing = await a.GetAsync("/order"); Assert.NotEqual(HttpStatusCode.Unauthorized, landing.StatusCode); Assert.True(landing.Headers.CacheControl?.NoStore);
        Assert.Contains("base-uri 'none'",landing.Headers.GetValues("Content-Security-Policy").Single());
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
        enabled.Port.Status = 200; enabled.Port.ExpiredContext = true;
        using var expired = await browser.PostAsJsonAsync("/bff/guest/qr/sessions", new QrEntryExchange(new string('E', 43)));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, expired.StatusCode); Assert.False(expired.Headers.Contains("Set-Cookie"));
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
    private sealed class Factory(bool enabled = true,bool requests=false,bool acceptance=false) : WebApplicationFactory<CUSTOMERBFF::Program>
    {
        public OrganizationPort? Layout;
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
                ["QrGuest:Enabled"]=enabled.ToString(),["QrGuest:MenuEnabled"]="true",["QrGuest:RequestsEnabled"]=requests.ToString(),["QrGuest:AcceptanceEnabled"]=acceptance.ToString(),["QrGuest:PublicOrigin"]="https://localhost"
            }));
            builder.ConfigureTestServices(services => { services.RemoveAll<IQrGatewayPort>(); services.AddSingleton<IQrGatewayPort>(Port);Layout=new(Port);services.RemoveAll<CUSTOMERBFF::NexaConnect.CustomerBff.Application.Catalog.ICustomerMenuOrganizationPort>();services.AddSingleton<CUSTOMERBFF::NexaConnect.CustomerBff.Application.Catalog.ICustomerMenuOrganizationPort>(Layout); });
        }
    }
    private sealed class OrganizationPort(Port access):CUSTOMERBFF::NexaConnect.CustomerBff.Application.Catalog.ICustomerMenuOrganizationPort
    {
        public int Sends;public TenantContext? LastTenant;
        public Task<CurrentPlatformAccessResponse?> AccessAsync(string token,CancellationToken ct)=>access.AccessAsync(token,ct);
        public Task<HttpResponseMessage> SendAsync(TenantContext tenant,Guid branch,NexaConnect.Contracts.Catalog.UpdateMenuOrganization? input,string token,CancellationToken ct)
        {Assert.Equal("server-held-test-token",token);Sends++;LastTenant=tenant;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new NexaConnect.Contracts.Catalog.BranchMenuOrganization(tenant.OrganizationId,branch,1,[],[]))});}
    }
    private sealed class Port : IQrGatewayPort
    {
        public Task<QrGatewayReply> AcceptAsync(TenantContext tenant,string bearer,Guid branch,Guid id,QrAcceptance input,CancellationToken ct){Assert.Equal("server-held-test-token",bearer);return Task.FromResult(new QrGatewayReply(200,JsonSerializer.SerializeToElement(View(id) with{Status="accepted",OrderId=id,OrderStatus="submitted"},JsonSerializerOptions.Web)));}
        public Task<QrGatewayReply> ConfirmAsync(string session,Guid id,QrPriceConfirmation input,CancellationToken ct)=>Task.FromResult(sessions.ContainsKey(session)?new QrGatewayReply(200,JsonSerializer.SerializeToElement(View(id),JsonSerializerOptions.Web)):new QrGatewayReply(401,null));
        private readonly Dictionary<string,QrRequestView> submitted=[];
        public Task<QrGatewayReply> RequestsAsync(string session,QrSubmission? input,CancellationToken ct)
        {
            if(!sessions.ContainsKey(session))return Task.FromResult(new QrGatewayReply(401,null));
            if(input is not null&&!submitted.ContainsKey(session))submitted.Add(session,View(input.QuoteId));
            return Task.FromResult(new QrGatewayReply(200,input is null?JsonSerializer.SerializeToElement(new QrRequestList(submitted.TryGetValue(session,out var r)?[r]:[],false),JsonSerializerOptions.Web):JsonSerializer.SerializeToElement(submitted[session],JsonSerializerOptions.Web)));
        }
        public Task<QrGatewayReply> ReviewAsync(TenantContext tenant,string bearer,Guid branch,string state,Guid? id,QrRejection? input,CancellationToken ct)
        {Assert.Equal("server-held-test-token",bearer);LastTenant=tenant;return Task.FromResult(new QrGatewayReply(200,input is null?JsonSerializer.SerializeToElement(new QrRequestList([],false),JsonSerializerOptions.Web):JsonSerializer.SerializeToElement(View(id!.Value),JsonSerializerOptions.Web)));}
        private static QrRequestView View(Guid id)=>new(id,"A1","pending",1,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddMinutes(15),null,new(new string('A',64),"THB",DateTimeOffset.UtcNow.AddMinutes(2),[],107,100,10,7.7m,117.7m,7,true,10,id));
        public Task<QrGatewayReply> ShoppingAsync(string operation,string session,QrCart? cart,CancellationToken ct)
        {
            Assert.NotEqual(new string('H',43),session);
            if(Status!=200||!sessions.ContainsKey(session))return Task.FromResult(new QrGatewayReply(401,null));
            if(operation=="organization")return Task.FromResult(new QrGatewayReply(200,JsonSerializer.SerializeToElement(new{organizationId=Organization,branchId=Guid.NewGuid(),version=9,internalSecret="hidden",categories=new[]{new{id=Guid.NewGuid(),name="Food",internalSecret="hidden"}},items=Array.Empty<object>()},JsonSerializerOptions.Web)));
            return Task.FromResult(operation=="menu"?new QrGatewayReply(200,JsonSerializer.SerializeToElement(new{items=new[]{new{productId=Guid.NewGuid(),name="Soup",unitPrice=107,currency="THB",available=true,preparationStation="secret",internalSecret="secret"}},internalSecret="secret"},JsonSerializerOptions.Web)):new QrGatewayReply(409,null));
        }
        private readonly Dictionary<string, QrGuestContext> sessions = []; public int Starts; public int Status = 200; public bool ExpiredContext;
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
                var context = new QrGuestContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Restaurant", "Branch", "A1", "THB", ExpiredContext ? DateTimeOffset.UtcNow.AddMinutes(-1) : DateTimeOffset.UtcNow.AddHours(2)); sessions.Add(token, context);
                return Task.FromResult(new QrGatewayReply(200, JsonSerializer.SerializeToElement(new QrSessionIssue(token, context), JsonSerializerOptions.Web)));
            }
            if (session is null || !sessions.TryGetValue(session, out var value)) return Task.FromResult(new QrGatewayReply(401, null));
            if (operation == "end") { sessions.Remove(session); return Task.FromResult(new QrGatewayReply(204, null)); }
            return Task.FromResult(new QrGatewayReply(200, JsonSerializer.SerializeToElement(value, JsonSerializerOptions.Web)));
        }
    }
}
