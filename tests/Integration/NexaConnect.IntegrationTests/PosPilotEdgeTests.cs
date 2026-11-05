extern alias POSPILOT;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using POSPILOT::NexaConnect.PosPilotEdge;

namespace NexaConnect.IntegrationTests;
public sealed class PosPilotEdgeTests
{
    private const string Realm="nexa-review-it-0123456789abcdef0123456789abcdef";
    [Fact]public async Task Configured_otlp_is_rejected_before_identity_traffic_can_be_exported()
    {await using var f=new Factory(otlp:true);var error=Assert.ThrowsAny<InvalidOperationException>(()=>f.Client());Assert.Contains("OTLP",error.ToString());Assert.Equal(0,f.Handler.Calls);}
    [Theory]
    [InlineData("/admin/realms",false)][InlineData("/realms/master/protocol/openid-connect/token",false)]
    [InlineData("/metrics",false)][InlineData("/internal/source-cutoffs",false)][InlineData("/bff/guest/qr/menu",false)]
    [InlineData("/api/pos/v1/internal/read",false)][InlineData("/api/pos/v1/../internal",false)]
    [InlineData("/api/pos/v1/%2e%2e/read",false)][InlineData("/api/order/v1/customer/branches/123/orders",true)]
    [InlineData("/resources/theme/login/keycloak.v2/style.css",true)]
    public void Route_policy_is_staff_only(string target,bool allowed)=>Assert.Equal(allowed,Options().Route("GET",target)is not null);
    [Theory][InlineData("0.0.0.0")][InlineData("8.8.8.8")][InlineData("127.0.0.1")][InlineData("::1")]
    public void Public_or_wildcard_listener_rejected(string address)=>Assert.Throws<InvalidOperationException>(()=>(Options()with{ListenAddress=address}).Validate());
    [Fact]public async Task Bearer_tenant_and_correlation_forwarded_without_guest_cookie_or_spoofed_proxy_headers()
    {
        await using var f=new Factory();using var client=f.Client();client.DefaultRequestHeaders.Add("Authorization","Bearer synthetic");client.DefaultRequestHeaders.Add("X-Nexa-Organization-Id",Guid.NewGuid().ToString());client.DefaultRequestHeaders.Add("Cookie","staff=private");client.DefaultRequestHeaders.Add("X-Forwarded-Host","evil.invalid");
        using var response=await client.GetAsync("/api/order/v1/orders/123");Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal("Bearer synthetic",f.Handler.Authorization);Assert.NotNull(f.Handler.Organization);Assert.NotNull(f.Handler.Correlation);Assert.Null(f.Handler.Cookie);Assert.Equal("192.168.1.10:9443",f.Handler.ForwardedHost);Assert.False(response.Headers.Contains("Set-Cookie"));Assert.False(response.Headers.Contains("Content-Security-Policy"));Assert.Equal("strict-origin",response.Headers.GetValues("Referrer-Policy").Single());
    }
    [Fact]public async Task Anonymous_api_is_denied_before_upstream()
    {await using var f=new Factory();using var client=f.Client();using var response=await client.GetAsync("/api/payment/v1/intents/123");Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);Assert.Equal(0,f.Handler.Calls);}
    [Fact]public async Task Native_terminal_and_original_operation_headers_are_preserved_without_retry()
    {
        await using var f=new Factory();using var client=f.Client();var terminal=Guid.NewGuid();var operation=Guid.NewGuid();
        client.DefaultRequestHeaders.Add("Authorization","Bearer synthetic");client.DefaultRequestHeaders.Add("X-Nexa-Terminal-Id",terminal.ToString("D"));client.DefaultRequestHeaders.Add("X-Client-Operation-Id",operation.ToString("D"));
        using var response=await client.PostAsync("/api/pos/v1/cash-sessions/synthetic/movements",new StringContent("{}"));
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal(terminal.ToString("D"),f.Handler.Terminal);Assert.Equal(operation.ToString("D"),f.Handler.Operation);Assert.Equal(1,f.Handler.Calls);
    }
    [Theory][InlineData("X-Nexa-Terminal-Id","invalid")][InlineData("X-Client-Operation-Id","invalid")][InlineData("X-Nexa-Terminal-Id","00000000-0000-0000-0000-000000000000")][InlineData("X-Client-Operation-Id","00000000-0000-0000-0000-000000000000")]
    public async Task Invalid_native_recovery_headers_fail_before_upstream(string header,string value)
    {await using var f=new Factory();using var client=f.Client();client.DefaultRequestHeaders.Add("Authorization","Bearer synthetic");client.DefaultRequestHeaders.Add(header,value);using var response=await client.PostAsync("/api/pos/v1/cash-sessions/synthetic/movements",new StringContent("{}"));Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);Assert.Equal(0,f.Handler.Calls);}
    [Theory][InlineData("192.168.1.99","https://192.168.1.10:9443")][InlineData("192.168.1.20","https://other.invalid:9443")][InlineData("192.168.1.20","http://192.168.1.10:9443")]
    public async Task Wrong_client_host_or_scheme_cannot_reach_upstream(string ip,string origin)
    {await using var f=new Factory(ip);using var client=f.Client(origin);using var response=await client.GetAsync($"/realms/{Realm}/.well-known/openid-configuration");Assert.Equal(HttpStatusCode.NotFound,response.StatusCode);Assert.Equal(0,f.Handler.Calls);}
    [Fact]public async Task Identity_form_and_callback_work_without_following_redirect()
    {await using var f=new Factory();f.Handler.Location=new("nexaconnect-pos://oauth/callback?code=synthetic&state=synthetic");using var client=f.Client();client.DefaultRequestHeaders.Add("Origin","https://192.168.1.10:9443");client.DefaultRequestHeaders.Add("Cookie","AUTH_SESSION_ID=synthetic");using var response=await client.PostAsync($"/realms/{Realm}/login-actions/authenticate?session_code=synthetic",new FormUrlEncodedContent(new Dictionary<string,string>{{"username","synthetic"}}));Assert.Equal(HttpStatusCode.Found,response.StatusCode);Assert.Equal("nexaconnect-pos",response.Headers.Location!.Scheme);Assert.Equal("AUTH_SESSION_ID=synthetic",f.Handler.Cookie);Assert.Equal(1,f.Handler.Calls);Assert.True(response.Headers.Contains("Set-Cookie"));Assert.Equal("frame-ancestors 'none'",response.Headers.GetValues("Content-Security-Policy").Single());Assert.Equal("DENY",response.Headers.GetValues("X-Frame-Options").Single());}
    [Fact]public async Task External_redirect_is_not_exposed()
    {await using var f=new Factory();f.Handler.Location=new("https://evil.invalid/");using var client=f.Client();using var response=await client.GetAsync($"/realms/{Realm}/protocol/openid-connect/auth");Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);Assert.Null(response.Headers.Location);Assert.DoesNotContain("private-detail",await response.Content.ReadAsStringAsync());}
    [Fact]public async Task Locked_gallery_capability_only_reaches_its_read_route()
    {await using var f=new Factory();using var client=f.Client();client.DefaultRequestHeaders.Add("X-Nexa-Gallery-Reader","synthetic-gallery");using var read=await client.GetAsync($"/api/media/v1/gallery-readers/organizations/{Guid.NewGuid()}/branches/{Guid.NewGuid()}");Assert.Equal(HttpStatusCode.OK,read.StatusCode);Assert.Equal("synthetic-gallery",f.Handler.Gallery);using var other=await client.GetAsync("/api/media/v1/customer/files");Assert.Equal(HttpStatusCode.Unauthorized,other.StatusCode);Assert.Equal(1,f.Handler.Calls);}
    [Fact]public async Task Cross_origin_mutation_is_denied()
    {await using var f=new Factory();using var client=f.Client();client.DefaultRequestHeaders.Add("Origin","https://evil.invalid");using var response=await client.PostAsync($"/realms/{Realm}/protocol/openid-connect/token",new StringContent("synthetic"));Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);Assert.Equal(0,f.Handler.Calls);}
    [Fact]public async Task Null_origin_is_not_an_authentication_bypass()
    {await using var f=new Factory();using var client=f.Client();client.DefaultRequestHeaders.Add("Origin","null");using var response=await client.PostAsync($"/realms/{Realm}/login-actions/authenticate",new StringContent("synthetic"));Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);Assert.Equal(0,f.Handler.Calls);}
    [Fact]public async Task Oversized_body_never_reaches_upstream()
    {await using var f=new Factory();using var client=f.Client();using var response=await client.PostAsync($"/realms/{Realm}/protocol/openid-connect/token",new StringContent(new string('x',65537)));Assert.Equal(HttpStatusCode.RequestEntityTooLarge,response.StatusCode);Assert.Equal(0,f.Handler.Calls);}
    private static StaffPilotOptions Options()=>new("https://192.168.1.10:9443","192.168.1.10","192.168.1.20",Realm,"fixture.pem","fixture.key",new(){["identity"]="http://127.0.0.1:5001/",["pos"]="http://127.0.0.1:5002/",["order"]="http://127.0.0.1:5003/",["catalog"]="http://127.0.0.1:5004/",["payment"]="http://127.0.0.1:5005/",["media"]="http://127.0.0.1:5006/"});
    private sealed class Factory(string ip="192.168.1.20",bool otlp=false):WebApplicationFactory<POSPILOT::PosPilotProgram>
    {
        public Handler Handler=new();public HttpClient Client(string origin="https://192.168.1.10:9443")=>CreateClient(new(){BaseAddress=new(origin),AllowAutoRedirect=false});
        protected override void ConfigureWebHost(IWebHostBuilder builder){builder.UseEnvironment("Testing");builder.ConfigureAppConfiguration((_,c)=>{
            var o=Options();var values=new Dictionary<string,string?>{["Observability:OtlpEnabled"]=otlp.ToString(),["StaffPilot:PublicOrigin"]=o.PublicOrigin,["StaffPilot:ListenAddress"]=o.ListenAddress,["StaffPilot:DeviceAddress"]=o.DeviceAddress,["StaffPilot:Realm"]=o.Realm,["StaffPilot:CertificatePath"]=o.CertificatePath,["StaffPilot:PrivateKeyPath"]=o.PrivateKeyPath};foreach(var v in o.Backends)values["StaffPilot:Backends:"+v.Key]=v.Value;c.AddInMemoryCollection(values);
        });builder.ConfigureTestServices(s=>{s.AddHttpClient("staff-pilot").ConfigurePrimaryHttpMessageHandler(()=>Handler);s.AddSingleton<IStartupFilter>(new AddressFilter(ip));});}
    }
    private sealed class AddressFilter(string ip):IStartupFilter {public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)=>app=>{app.Use((ctx,n)=>{ctx.Connection.RemoteIpAddress=IPAddress.Parse(ip);return n();});next(app);};}
    private sealed class Handler:HttpMessageHandler {
        public int Calls;public string? Authorization,Organization,Correlation,Cookie,ForwardedHost,Gallery,Terminal,Operation;public Uri? Location;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Calls++;string? Header(string key)=>request.Headers.TryGetValues(key,out var v)?v.Single():null;Authorization=Header("Authorization");Organization=Header("X-Nexa-Organization-Id");Correlation=Header("X-Correlation-ID");Cookie=Header("Cookie");ForwardedHost=Header("X-Forwarded-Host");Gallery=Header("X-Nexa-Gallery-Reader");Terminal=Header("X-Nexa-Terminal-Id");Operation=Header("X-Client-Operation-Id");var response=new HttpResponseMessage(Location is null?HttpStatusCode.OK:HttpStatusCode.Found){Content=new StringContent("private-detail")};response.Headers.Location=Location;response.Headers.TryAddWithoutValidation("Set-Cookie","AUTH_SESSION_ID=synthetic; Secure; HttpOnly; Path=/");response.Headers.TryAddWithoutValidation("Content-Security-Policy","frame-ancestors 'none'");return Task.FromResult(response);}
    }
}
