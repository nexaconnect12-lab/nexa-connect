extern alias REPORTING;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NexaConnect.Contracts.Platform;
using REPORTING::NexaConnect.Services.Reporting.Application;

namespace NexaConnect.IntegrationTests;

public sealed class EndOfDayHttpTests
{
    [Fact]
    public async Task Scope_live_permission_no_store_and_unchecked_status_are_enforced()
    {
        await using var factory=new Factory(); using var client=factory.CreateClient(new(){BaseAddress=new Uri("https://localhost")});
        string url=$"/api/reporting/v1/customer/organizations/{factory.Access.Organization}/reports/end-of-day?branchId={factory.Access.Branch}&businessDate=2026-09-01";
        client.DefaultRequestHeaders.Authorization=new("Bearer","unauthenticated");
        using var anonymous=await client.GetAsync(url); Assert.Equal(HttpStatusCode.Unauthorized,anonymous.StatusCode);
        client.DefaultRequestHeaders.Authorization=new("Bearer","operator");
        client.DefaultRequestHeaders.Add(TenantContextHeaders.ApplicationCode,"nexa_connect");
        client.DefaultRequestHeaders.Add(TenantContextHeaders.OrganizationId,factory.Access.Organization.ToString());
        using var allowed=await client.GetAsync(url); Assert.Equal(HttpStatusCode.OK,allowed.StatusCode);
        Assert.True(allowed.Headers.CacheControl?.NoStore); Assert.Contains("financial_evidence_not_checked",await allowed.Content.ReadAsStringAsync());
        Assert.Equal(ProductPermissions.ReportingSalesRead,factory.Access.Permission);
        using var foreign=await client.GetAsync(url.Replace(factory.Access.Branch.ToString(),Guid.NewGuid().ToString())); Assert.Equal(HttpStatusCode.Forbidden,foreign.StatusCode);
        factory.Access.Allowed=false;
        using var denied=await client.GetAsync(url); Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        Assert.Equal(1,factory.Repository.Reads);
        factory.Access.Allowed=true;
        using var invalid=await client.GetAsync(url.Replace("2026-09-01","2099-09-01")); Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        factory.Repository.Unavailable=true;
        using var unavailable=await client.GetAsync(url);Assert.Equal(HttpStatusCode.ServiceUnavailable,unavailable.StatusCode);
        Assert.True(unavailable.Headers.CacheControl?.NoStore);
        Assert.DoesNotContain("restricted-diagnostic",await unavailable.Content.ReadAsStringAsync());
    }
    private sealed class Factory:WebApplicationFactory<REPORTING::Program>
    {
        public Access Access {get;}=new(); public Repo Repository {get;}=new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            TestServiceConfiguration.Configure(builder,"reporting");
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string,string?>
                { ["ConnectionStrings:Reporting"]="Host=localhost;Database=unused" }));
            builder.ConfigureServices(s=>{s.RemoveAll<IReportingCustomerAuthorizer>();s.AddSingleton<IReportingCustomerAuthorizer>(Access);
                s.RemoveAll<IEndOfDaySources>();s.AddSingleton<IEndOfDaySources>(new Sources(Access));
                s.RemoveAll<IReportingReadRepository>();s.AddSingleton<IReportingReadRepository>(Repository);
                s.RemoveAll<IFinancialCompletenessRepository>();s.AddSingleton<IFinancialCompletenessRepository>(Repository);
                s.AddAuthentication(o=>{o.DefaultAuthenticateScheme="CompletenessTest";o.DefaultChallengeScheme="CompletenessTest";})
                    .AddScheme<AuthenticationSchemeOptions,Auth>("CompletenessTest",_=>{});});
        }
    }
    private sealed class Auth(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()=>Task.FromResult(Request.Headers.Authorization=="Bearer unauthenticated"
            ?AuthenticateResult.NoResult():AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub","operator"),new Claim(ClaimTypes.Role,"customer-manager")],Scheme.Name)),Scheme.Name)));
    }
    private sealed class Access:IReportingCustomerAuthorizer
    {
        public Guid Organization=Guid.NewGuid(),Branch=Guid.NewGuid(); public bool Allowed=true; public string? Permission;
        public Task<bool> IsGrantedAsync(Guid o,Guid? b,string permission,string authorization,CancellationToken ct)
        {Permission=permission;return Task.FromResult(Allowed && o==Organization && b==Branch);}
    }
    private sealed class Sources(Access access):IEndOfDaySources
    {
        public Task<BranchBusinessCalendar?> CalendarAsync(Guid branch,CancellationToken ct)=>Task.FromResult<BranchBusinessCalendar?>(new(access.Organization,Guid.Parse("55555555-5555-5555-5555-555555555555"),access.Branch,"Asia/Bangkok","THB"));
        public Task<NexaConnect.Contracts.Reporting.OrderDaySummary> OrderAsync(NexaConnect.Contracts.Reporting.EndOfDayWindow w,string bearer,CancellationToken ct)=>Task.FromResult(new NexaConnect.Contracts.Reporting.OrderDaySummary(w,DateTimeOffset.UtcNow,100,1,0,0,[new("card","THB",100)],["THB"]));
        public Task<NexaConnect.Contracts.Reporting.PaymentDaySummary> PaymentAsync(NexaConnect.Contracts.Reporting.EndOfDayWindow w,string bearer,CancellationToken ct)=>Task.FromResult(new NexaConnect.Contracts.Reporting.PaymentDaySummary(w,DateTimeOffset.UtcNow,25,0,0,0,["THB"]));
        public Task<NexaConnect.Contracts.Reporting.PosDaySummary> PosAsync(NexaConnect.Contracts.Reporting.EndOfDayWindow w,string bearer,CancellationToken ct)=>Task.FromResult(new NexaConnect.Contracts.Reporting.PosDaySummary(w,DateTimeOffset.UtcNow,0,0,0,0,["THB"]));
    }
    private sealed class Repo:IFinancialCompletenessRepository,IReportingReadRepository
    {
        public Task<DashboardSummary> DashboardAsync(ReportingRange r,CancellationToken ct)=>Task.FromResult(new DashboardSummary(1,100,100,25,75,"THB",null));
        public Task<SalesReport> SalesAsync(ReportingRange r,CancellationToken ct)=>throw new NotSupportedException();
        public int Reads; public bool Unavailable;
        public Task<FinancialCompletenessObservation?> LatestAsync(ReportingRange range,CancellationToken ct)
        {Reads++;if(Unavailable)throw new InvalidOperationException("restricted-diagnostic");return Task.FromResult<FinancialCompletenessObservation?>(null);}
        public Task<FinancialCompletenessObservation> CheckAsync(FinancialCompletenessSource source,CancellationToken ct)=>throw new NotSupportedException();
        public Task RecordAsync(FinancialCompletenessObservation observation,string actor,CancellationToken ct)=>throw new NotSupportedException();
    }
}

