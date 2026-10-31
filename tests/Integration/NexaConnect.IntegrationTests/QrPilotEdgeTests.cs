extern alias QRPILOT;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using QRPILOT::NexaConnect.QrPilotEdge;

namespace NexaConnect.IntegrationTests;

public sealed class QrPilotEdgeTests
{
    [Fact]public void Photo_allowlist_accepts_only_exact_product_get_without_parameters()
    {
        string path=$"/bff/guest/qr/menu/{Guid.NewGuid():D}/photo";
        Assert.True(PilotOptions.Allowed("GET",path));Assert.False(PilotOptions.Allowed("POST",path));
        Assert.False(PilotOptions.Allowed("GET",path+"?token=private"));Assert.False(PilotOptions.Allowed("GET",path.Replace("/photo","/original")));
    }
    [Fact] public async Task Real_loopback_tls_backend_requires_the_original_certificate_pin_without_trust_store_changes()
    {
        using var key=RSA.Create(2048);var request=new CertificateRequest("CN=localhost",key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        var san=new SubjectAlternativeNameBuilder();san.AddDnsName("localhost");request.CertificateExtensions.Add(san.Build());
        using var created=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddDays(2));
        using var certificate=X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx),null);
        var builder=WebApplication.CreateBuilder();builder.Logging.ClearProviders();builder.WebHost.ConfigureKestrel(s=>s.Listen(IPAddress.Loopback,0,listen=>listen.UseHttps(certificate)));
        await using var backend=builder.Build();backend.MapGet("/order",(HttpContext c)=>c.Request.Host.Value=="qr.example.test:8443"?Results.Text("guest-shell"):Results.StatusCode(403));await backend.StartAsync();
        var address=backend.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        string uri="https://localhost:"+new Uri(address).Port;string pin=Convert.ToHexStringLower(SHA256.HashData(certificate.RawData));
        await using(var edge=new Factory(uri,pin)){using var client=edge.CreateClient(new(){BaseAddress=new("https://qr.example.test:8443")});Assert.Equal("guest-shell",await client.GetStringAsync("/order"));}
        await using(var edge=new Factory(uri,new string('0',64))){using var client=edge.CreateClient(new(){BaseAddress=new("https://qr.example.test:8443")});Assert.Equal(HttpStatusCode.ServiceUnavailable,(await client.GetAsync("/order")).StatusCode);}
        var policy=new PilotOptions("https://qr.example.test:8443","192.168.1.10",uri,pin,"fixture.pem","fixture.key");
        Assert.True(policy.BackendCertificate(certificate,SslPolicyErrors.RemoteCertificateNameMismatch)); // Public HTTP Host differs; the backend certificate still matches the fixed localhost transport.
        var badRequest=new CertificateRequest("CN=other.example.test",key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        var badSan=new SubjectAlternativeNameBuilder();badSan.AddDnsName("other.example.test");badRequest.CertificateExtensions.Add(badSan.Build());
        using var badName=badRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddDays(2));
        Assert.False((policy with{BackendCertificateSha256=Convert.ToHexStringLower(SHA256.HashData(badName.RawData))}).BackendCertificate(badName,SslPolicyErrors.RemoteCertificateNameMismatch));
    }
    [Theory]
    [InlineData("/bff/customer/me")][InlineData("/kitchen")][InlineData("/")][InlineData("/api/order/v1/guest/qr/menu")]
    [InlineData("/bff/guest/qr/requests?visitorId=other")][InlineData("/assets/../index.html")][InlineData("/bff/guest/qr/sessions/secret")]
    public void Edge_policy_denies_staff_private_or_unbounded_targets(string target)=>Assert.False(PilotOptions.Allowed("GET",target));
    [Theory]
    [InlineData("0.0.0.0")][InlineData("127.0.0.1")][InlineData("8.8.8.8")][InlineData("169.254.1.2")][InlineData("::")]
    public void Edge_listener_never_binds_public_wildcard_or_link_local_addresses(string address)=>Assert.False(PilotOptions.PrivateAddress(address));
    [Fact]public async Task Guest_proxy_strips_staff_authority_and_only_returns_guest_cookie_with_safe_correlation()
    {
        await using var host=new Factory();using var client=host.CreateClient(new(){BaseAddress=new("https://qr.example.test:8443"),AllowAutoRedirect=false});
        client.DefaultRequestHeaders.Authorization=new("Bearer","private-staff-token");client.DefaultRequestHeaders.Add("X-Nexa-Organization-Id",Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("Cookie","__Host-nexa-customer=staff; __Host-nexa-qr-guest="+new string('S',43));
        client.DefaultRequestHeaders.Add("Origin","https://qr.example.test:8443");client.DefaultRequestHeaders.Add("X-Correlation-ID","pilot-correlation");
        using var response=await client.PostAsJsonAsync("/bff/guest/qr/sessions",new{entryToken=new string('E',43)});
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal("pilot-correlation",response.Headers.GetValues("X-Correlation-ID").Single());
        Assert.Contains("__Host-nexa-qr-guest",Assert.Single(response.Headers.GetValues("Set-Cookie")));
        Assert.Null(host.Handler.Authorization);Assert.Null(host.Handler.Organization);Assert.Equal("__Host-nexa-qr-guest="+new string('S',43),host.Handler.Cookie);
        Assert.Equal("pilot-correlation",host.Handler.Correlation);Assert.Equal("localhost",host.Handler.Host);
        Assert.Equal("qr.example.test:8443",host.Handler.HttpHost);
    }
    [Fact]public async Task Wrong_origin_host_method_and_body_are_rejected_without_contacting_backend()
    {
        await using var host=new Factory();using var client=host.CreateClient(new(){BaseAddress=new("https://qr.example.test:8443"),AllowAutoRedirect=false});
        using(var denied=await client.PostAsJsonAsync("/bff/guest/qr/sessions",new{}))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        client.DefaultRequestHeaders.Add("Origin","https://qr.example.test:8443");
        using(var denied=await client.PutAsync("/bff/guest/qr/requests",JsonContent.Create(new{})))Assert.Equal(HttpStatusCode.NotFound,denied.StatusCode);
        using(var denied=await client.PostAsync("/bff/guest/qr/sessions",new StringContent("{}")))Assert.Equal(HttpStatusCode.UnsupportedMediaType,denied.StatusCode);
        using(var denied=await client.PostAsJsonAsync("/bff/guest/qr/quote",new{value=new string('x',9000)}))Assert.Equal(HttpStatusCode.RequestEntityTooLarge,denied.StatusCode);
        client.DefaultRequestHeaders.Host="foreign.example.test";
        using(var denied=await client.GetAsync("/order"))Assert.Equal(HttpStatusCode.NotFound,denied.StatusCode);
        Assert.Equal(0,host.Handler.Calls);
    }
    [Fact]public async Task Ended_guest_cookie_is_deleted_without_forwarding_staff_cookies()
    {
        await using var host=new Factory();host.Handler.DeleteCookie=true;
        using var client=host.CreateClient(new(){BaseAddress=new("https://qr.example.test:8443"),AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Origin","https://qr.example.test:8443");
        using var response=await client.DeleteAsync("/bff/guest/qr/sessions");Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var value=Assert.Single(response.Headers.GetValues("Set-Cookie"));Assert.StartsWith("__Host-nexa-qr-guest=;",value);Assert.Contains("expires=",value,StringComparison.OrdinalIgnoreCase);
    }
    [Theory][InlineData(302)][InlineData(500)]public async Task Redirects_or_backend_diagnostics_never_reach_phone(int code)
    {
        await using var host=new Factory();host.Handler.Code=code;using var client=host.CreateClient(new(){BaseAddress=new("https://qr.example.test:8443"),AllowAutoRedirect=false});
        using var response=await client.GetAsync("/order");Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);Assert.DoesNotContain("private-upstream-detail",await response.Content.ReadAsStringAsync());Assert.Null(response.Headers.Location);
    }
    private sealed class Factory(string? backend=null,string? pin=null):WebApplicationFactory<QRPILOT::QrPilotProgram>
    {
        public Handler Handler=new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");builder.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{
                ["QrPilot:PublicOrigin"]="https://qr.example.test:8443",["QrPilot:ListenAddress"]="192.168.1.10",["QrPilot:BackendOrigin"]=backend??"https://localhost:12345",["QrPilot:BackendCertificateSha256"]=pin??new string('a',64),["QrPilot:CertificatePath"]="fixture.pem",["QrPilot:PrivateKeyPath"]="fixture.key"}));
            if(backend is null)builder.ConfigureTestServices(s=>s.AddHttpClient("pilot").ConfigurePrimaryHttpMessageHandler(()=>Handler));
        }
    }
    private sealed class Handler:HttpMessageHandler
    {
        public int Code=200,Calls;public bool DeleteCookie;public string? Authorization,Organization,Cookie,Correlation,Host,HttpHost;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Calls++;Authorization=request.Headers.Authorization?.ToString();Organization=request.Headers.TryGetValues("X-Nexa-Organization-Id",out var org)?org.Single():null;
            Cookie=request.Headers.TryGetValues("Cookie",out var cookies)?cookies.Single():null;Correlation=request.Headers.TryGetValues("X-Correlation-ID",out var correlation)?correlation.Single():null;Host=request.RequestUri!.Host;HttpHost=request.Headers.Host;
            var response=new HttpResponseMessage((HttpStatusCode)Code){Content=new StringContent("private-upstream-detail")};
            response.Headers.TryAddWithoutValidation("Set-Cookie",["__Host-nexa-customer=staff; path=/; secure; httponly; samesite=strict","__Host-nexa-qr-guest="+(DeleteCookie?"; expires=Thu, 01 Jan 1970 00:00:00 GMT":new string('S',43))+"; path=/; secure; httponly; samesite=strict"]);
            return Task.FromResult(response);
        }
    }
}
