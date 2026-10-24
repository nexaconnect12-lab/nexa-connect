extern alias CUSTOMERBFF;

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NexaConnect.Contracts.Platform;
using CUSTOMERBFF::NexaConnect.CustomerBff;
using CUSTOMERBFF::NexaConnect.CustomerBff.Application.Reporting;

namespace NexaConnect.IntegrationTests;

public sealed class CustomerSettlementReportsBoundaryTests
{
    private static readonly Guid Branch=Guid.NewGuid();
    private static string Path=>$"/bff/customer/reports/day-settlements?branchId={Branch}&fromDate=2026-09-01&toDate=2026-09-02";
    private static string ExportPath => Path.Replace("day-settlements?", "day-settlements/export.csv?");
    [Fact]
    public async Task Export_requires_live_session_membership_sanitizes_failures_and_preserves_csv_bytes()
    {
        await using var factory = new Factory(); using var client = factory.CreateClient(Options());
        using var anonymous = await client.GetAsync(ExportPath); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.True(anonymous.Headers.CacheControl?.NoStore);
        await SignIn(factory, client);
        using var export = await client.GetAsync(ExportPath);
        Assert.Equal(HttpStatusCode.OK, export.StatusCode); Assert.True(export.Headers.CacheControl?.NoStore);
        Assert.Equal("text/csv", export.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", export.Content.Headers.ContentDisposition?.DispositionType);
        Assert.True(factory.Port.LastRequest!.ExportCsv);
        Assert.Equal(CsvBytes, await export.Content.ReadAsByteArrayAsync());
        factory.Port.Allowed = false;
        using var revoked = await client.GetAsync(ExportPath); Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        Assert.Equal(1, factory.Port.Sends);
        factory.Port.Allowed = true; factory.Port.Status = HttpStatusCode.ServiceUnavailable;
        using var unavailable = await client.GetAsync(ExportPath); Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Null(unavailable.Content.Headers.ContentDisposition);
        Assert.DoesNotContain("downstream-sensitive", await unavailable.Content.ReadAsStringAsync());
        factory.Port.Status = HttpStatusCode.OK; factory.Port.BadCsvType = true;
        using var malformed = await client.GetAsync(ExportPath); Assert.Equal(HttpStatusCode.ServiceUnavailable, malformed.StatusCode);
        factory.Port.BadCsvType = false;
        using var invalid = await client.GetAsync(ExportPath.Replace("2026-09-02", "2026-11-02"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }
    [Fact]
    public async Task Export_adapter_forwards_protected_scope_and_correlation_and_ignores_upstream_filename()
    {
        await using var factory = new Factory { UseTransport = true }; using var client = factory.CreateClient(Options());
        await SignIn(factory, client); client.DefaultRequestHeaders.Add("X-Correlation-ID", "settlement-export-test");
        using var response = await client.GetAsync(ExportPath + "&organizationId=" + Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"organizations/{factory.Port.OrganizationId:D}/reports/day-settlements/export.csv?", factory.ReportingPath);
        Assert.Equal("settlement-export-test", factory.ReportingCorrelation); Assert.Equal("settlement-export-test", factory.DirectoryCorrelation);
        Assert.Equal("Bearer test-server-token", factory.ReportingToken);
        Assert.Equal(factory.Port.OrganizationId.ToString("D"), factory.ReportingOrganization);
        Assert.Equal(CsvBytes, await response.Content.ReadAsByteArrayAsync());
        Assert.Contains(Branch.ToString(), response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.DoesNotContain("untrusted", response.Content.Headers.ContentDisposition!.ToString());
    }
    private static readonly byte[] CsvBytes = System.Text.Encoding.UTF8.GetBytes("\uFEFF\"record_type\",\"organization_id\",\"branch_id\"\r\n\"observation\",\"scope\",\"branch\"\r\n");
    private static HttpResponseMessage CsvResponse()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(CsvBytes) };
        response.Content.Headers.ContentType = new("text/csv") { CharSet = "utf-8" };
        response.Content.Headers.ContentDisposition = new("attachment") { FileName = "untrusted.csv" };
        return response;
    }
    [Fact]
    public async Task Oversized_export_transport_is_rejected_before_attachment()
    {
        await using var factory = new Factory { UseTransport = true, OversizedExport = true };
        using var client = factory.CreateClient(Options()); await SignIn(factory, client);
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "settlement-export-test");
        using var response = await client.GetAsync(ExportPath);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore); Assert.Null(response.Content.Headers.ContentDisposition);
    }
    [Fact] public async Task Report_requires_session_protected_tenant_and_current_membership()
    {
        await using var factory=new Factory();using var client=factory.CreateClient(Options());
        using var anonymous=await client.GetAsync(Path);Assert.Equal(HttpStatusCode.Unauthorized,anonymous.StatusCode);
        Assert.True(anonymous.Headers.CacheControl?.NoStore);
        await SignIn(factory,client);
        using var response=await client.GetAsync(Path+"&organizationId="+Guid.NewGuid());Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);Assert.Equal(factory.Port.OrganizationId,factory.Port.LastTenant!.OrganizationId);
        Assert.Equal(Branch,factory.Port.LastRequest!.BranchId);Assert.Equal(1,factory.Port.Sends);
        factory.Port.Allowed=false;using var denied=await client.GetAsync(Path);Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.Equal(1,factory.Port.Sends);
        factory.Port.Allowed=true;factory.Port.AccessSubject="another-user";
        using var wrongAccess=await client.GetAsync(Path);Assert.Equal(HttpStatusCode.Forbidden,wrongAccess.StatusCode);Assert.Equal(1,factory.Port.Sends);
    }
    [Theory][InlineData("other-subject","nexa_connect",401)][InlineData("operator","other_product",403)]
    public async Task Other_subject_or_product_cannot_read(string subject,string product,int expected)
    {
        await using var factory=new Factory();using var client=factory.CreateClient(Options());await SignIn(factory,client,subject,product);
        using var response=await client.GetAsync(Path);Assert.Equal(expected,(int)response.StatusCode);Assert.Equal(0,factory.Port.Sends);
    }
    [Fact] public async Task Invalid_range_and_upstream_financial_errors_are_not_forwarded()
    {
        await using var factory=new Factory();using var client=factory.CreateClient(Options());await SignIn(factory,client);
        using var invalid=await client.GetAsync(Path.Replace("2026-09-02","2026-11-02"));Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);Assert.Equal(0,factory.Port.Sends);
        factory.Port.Status=HttpStatusCode.ServiceUnavailable;using var response=await client.GetAsync(Path);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);Assert.DoesNotContain("downstream-sensitive",await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.CacheControl?.NoStore);
        factory.Port.Status=HttpStatusCode.Forbidden;
        using var revoked=await client.GetAsync(Path);Assert.Equal(HttpStatusCode.Forbidden,revoked.StatusCode);
        Assert.DoesNotContain("downstream-sensitive",await revoked.Content.ReadAsStringAsync());
        factory.Port.Throw=true;
        using var unavailable=await client.GetAsync(Path);Assert.Equal(HttpStatusCode.ServiceUnavailable,unavailable.StatusCode);
        Assert.DoesNotContain("restricted-transport",await unavailable.Content.ReadAsStringAsync());
    }
    [Theory]
    [InlineData("branchId", "00000000-0000-0000-0000-000000000000")]
    [InlineData("fromDate", "2026-09-02")]
    [InlineData("toDate", "2099-09-02")]
    [InlineData("fromDate", "")]
    public async Task Invalid_or_open_window_cannot_reach_reporting(string name,string value)
    {
        await using var factory=new Factory();using var client=factory.CreateClient(Options());await SignIn(factory,client);
        string path=System.Text.RegularExpressions.Regex.Replace(Path,$"{name}=[^&]*",$"{name}={Uri.EscapeDataString(value)}");
        using var response=await client.GetAsync(path);Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);Assert.Equal(0,factory.Port.Sends);
    }
    [Fact]
    public async Task Adapter_uses_only_protected_scope_token_and_validated_correlation()
    {
        await using var factory=new Factory{UseTransport=true};using var client=factory.CreateClient(Options());await SignIn(factory,client);
        client.DefaultRequestHeaders.Add("X-Correlation-ID","financial-portal-test");
        using var response=await client.GetAsync(Path+"&organizationId="+Guid.NewGuid()+"&actor=browser-value");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("financial-portal-test",response.Headers.GetValues("X-Correlation-ID").Single());
        Assert.Equal("financial-portal-test",factory.DirectoryCorrelation);Assert.Equal("financial-portal-test",factory.ReportingCorrelation);
        Assert.Contains($"organizations/{factory.Port.OrganizationId:D}/reports/day-settlements",factory.ReportingPath);
        Assert.Contains("fromDate=2026-09-01",factory.ReportingPath);Assert.Contains("toDate=2026-09-02",factory.ReportingPath);
        Assert.DoesNotContain("organizationId=",factory.ReportingPath);Assert.DoesNotContain("actor=",factory.ReportingPath);
        Assert.Equal(factory.Port.OrganizationId.ToString("D"),factory.ReportingOrganization);
        Assert.Equal("nexa_connect",factory.ReportingApplication);Assert.Equal("customer",factory.ReportingPortal);
        Assert.Equal("Bearer test-server-token",factory.ReportingToken);
        Assert.Contains("not_checked",await response.Content.ReadAsStringAsync());
    }
    private static WebApplicationFactoryClientOptions Options()=>new(){AllowAutoRedirect=false,BaseAddress=new Uri("https://localhost")};
    private static async Task SignIn(Factory factory,HttpClient client,string tenantSubject="operator",string product="nexa_connect")
    {
        var options=factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get("CustomerCookie");
        var properties=new AuthenticationProperties();properties.StoreTokens([
            new AuthenticationToken{Name="access_token",Value="test-server-token"},
            new AuthenticationToken{Name="expires_at",Value=DateTimeOffset.UtcNow.AddHours(1).ToString("O")},
        ]);
        var principal=new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub","operator"),new Claim(ClaimTypes.NameIdentifier,"operator")],"CustomerCookie"));
        var ticket=new AuthenticationTicket(principal,properties,"CustomerCookie");
        string key=await options.SessionStore!.StoreAsync(ticket);
        var cookiePrincipal=new ClaimsPrincipal(new ClaimsIdentity([new Claim("Microsoft.AspNetCore.Authentication.Cookies-SessionId",key)],"CustomerCookie"));
        string session=options.TicketDataFormat.Protect(new AuthenticationTicket(cookiePrincipal,null,"CustomerCookie"));
        string tenant=factory.Services.GetRequiredService<TenantSelectionCookie>().Protect(new(tenantSubject,factory.Port.OrganizationId,product));
        client.DefaultRequestHeaders.Add("Cookie",$"{options.Cookie.Name}={session}; __Host-nexa-customer-tenant={tenant}");
    }

    private sealed class Factory:WebApplicationFactory<CUSTOMERBFF::Program>
    {
        public CompletenessPort Port{get;}=new();
        public bool UseTransport, OversizedExport;
        public string? DirectoryCorrelation,ReportingCorrelation,ReportingPath,ReportingOrganization,ReportingApplication,ReportingPortal,ReportingToken;
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_,configuration)=>configuration.AddInMemoryCollection(new Dictionary<string,string?>
            {
                ["Logging:LogLevel:Default"]="None", ["Bff:Authority"]="https://identity.test/realms/test",["Bff:ClientId"]="test",["Bff:ClientSecret"]="test-secret",
                ["Services:PlatformDirectory"]="https://directory.test/",["Services:Order"]="https://order.test/",
                ["Services:Restaurant"]="https://restaurant.test/",["Services:Reporting"]="https://reporting.test/",
                ["Services:Media"]="https://media.test/",["Services:Notification"]="https://notification.test/",
                ["Services:Catalog"]="https://catalog.test/",["Services:Inventory"]="https://inventory.test/",
            }));
            builder.ConfigureTestServices(services=>
            {
                if(!UseTransport){services.RemoveAll<ICustomerSettlementReportsPort>();services.AddSingleton<ICustomerSettlementReportsPort>(Port);return;}
                services.AddHttpClient("PlatformDirectory").ConfigurePrimaryHttpMessageHandler(()=>new Handler(request=>
                {
                    DirectoryCorrelation=request.Headers.GetValues("X-Correlation-ID").Single();
                    Assert.Equal("Bearer test-server-token",request.Headers.Authorization!.ToString());
                    return new(HttpStatusCode.OK){Content=JsonContent.Create(new CurrentPlatformAccessResponse("operator",[new(Port.OrganizationId,"test","Test","nexa_connect")]))};
                }));
                services.AddHttpClient<ICustomerSettlementReportsPort,CUSTOMERBFF::NexaConnect.CustomerBff.Infrastructure.Reporting.HttpCustomerSettlementReportsPort>()
                    .ConfigurePrimaryHttpMessageHandler(()=>new Handler(request=>
                    {
                        ReportingCorrelation=request.Headers.GetValues("X-Correlation-ID").Single();ReportingPath=request.RequestUri!.PathAndQuery;
                        ReportingOrganization=request.Headers.GetValues(TenantContextHeaders.OrganizationId).Single();
                        ReportingApplication=request.Headers.GetValues(TenantContextHeaders.ApplicationCode).Single();
                        ReportingPortal=request.Headers.GetValues(TenantContextHeaders.PortalRequest).Single();ReportingToken=request.Headers.Authorization!.ToString();
                        if (OversizedExport) return new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[4 * 1024 * 1024 + 1]) };
                        return request.RequestUri!.AbsolutePath.EndsWith("/export.csv") ? CsvResponse()
                            : new(HttpStatusCode.OK){Content=JsonContent.Create(new {status="not_checked",observation=(object?)null})};
                    }));
            });
        }
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> send):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(send(request));}
    private sealed class CompletenessPort:ICustomerSettlementReportsPort
    {
        public Guid OrganizationId{get;}=Guid.NewGuid();public bool Allowed=true,Throw,BadCsvType;public int Sends;public HttpStatusCode Status=HttpStatusCode.OK;
        public TenantContext? LastTenant;public SettlementReportsRequest? LastRequest;
        public string AccessSubject="operator";
        public Task<CurrentPlatformAccessResponse?> GetAccessAsync(string token,CancellationToken ct)=>Task.FromResult<CurrentPlatformAccessResponse?>(new(AccessSubject,Allowed?[new(OrganizationId,"test","Test","nexa_connect")]:[]));
        public Task<HttpResponseMessage> ReadAsync(TenantContext tenant,string token,SettlementReportsRequest request,CancellationToken ct)
        {Assert.Equal("test-server-token",token);Sends++;LastTenant=tenant;LastRequest=request;if(Throw)throw new HttpRequestException("restricted-transport");
            return Task.FromResult(request.ExportCsv && Status==HttpStatusCode.OK && !BadCsvType ? CsvResponse() : new HttpResponseMessage(Status){Content=JsonContent.Create(new{message="downstream-sensitive"})});}
    }
}
