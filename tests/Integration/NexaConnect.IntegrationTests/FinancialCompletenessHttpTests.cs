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

public sealed class FinancialCompletenessHttpTests
{
    [Fact]
    public async Task Scope_live_permission_no_store_and_unchecked_status_are_enforced()
    {
        await using var factory=new Factory(); using var client=factory.CreateClient(new(){BaseAddress=new Uri("https://localhost")});
        string url=$"/api/reporting/v1/customer/organizations/{factory.Access.Organization}/reports/financial-completeness?branchId={factory.Access.Branch}&fromUtc=2026-09-01T00:00:00Z&toUtc=2026-09-02T00:00:00Z";
        client.DefaultRequestHeaders.Authorization=new("Bearer","unauthenticated");
        using var anonymous=await client.GetAsync(url); Assert.Equal(HttpStatusCode.Unauthorized,anonymous.StatusCode);
        client.DefaultRequestHeaders.Authorization=new("Bearer","operator");
        client.DefaultRequestHeaders.Add(TenantContextHeaders.ApplicationCode,"nexa_connect");
        client.DefaultRequestHeaders.Add(TenantContextHeaders.OrganizationId,factory.Access.Organization.ToString());
        using var allowed=await client.GetAsync(url); Assert.Equal(HttpStatusCode.OK,allowed.StatusCode);
        Assert.True(allowed.Headers.CacheControl?.NoStore); Assert.Contains("not_checked",await allowed.Content.ReadAsStringAsync());
        Assert.Equal(ProductPermissions.ReportingSalesRead,factory.Access.Permission);
        using var foreign=await client.GetAsync(url.Replace(factory.Access.Branch.ToString(),Guid.NewGuid().ToString())); Assert.Equal(HttpStatusCode.Forbidden,foreign.StatusCode);
        factory.Access.Allowed=false;
        using var denied=await client.GetAsync(url); Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        Assert.Equal(1,factory.Repository.Reads);
        factory.Access.Allowed=true;
        using var invalid=await client.GetAsync(url.Replace("2026-09-02T00:00:00Z","2026-08-02T00:00:00Z")); Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
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
    private sealed class Repo:IFinancialCompletenessRepository
    {
        public int Reads; public bool Unavailable;
        public Task<FinancialCompletenessObservation?> LatestAsync(ReportingRange range,CancellationToken ct)
        {Reads++;if(Unavailable)throw new InvalidOperationException("restricted-diagnostic");return Task.FromResult<FinancialCompletenessObservation?>(null);}
        public Task<FinancialCompletenessObservation> CheckAsync(FinancialCompletenessSource source,CancellationToken ct)=>throw new NotSupportedException();
        public Task RecordAsync(FinancialCompletenessObservation observation,string actor,CancellationToken ct)=>throw new NotSupportedException();
    }
}
