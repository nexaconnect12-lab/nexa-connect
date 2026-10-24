extern alias REPORTING;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaConnect.Contracts.Platform;
using NexaConnect.Contracts.Reporting;
using REPORTING::NexaConnect.Services.Reporting.Application;

namespace NexaConnect.IntegrationTests;

public sealed class SettlementExportHttpTests
{
    [Fact]
    public async Task Export_requires_customer_scope_live_permission_and_owning_source_on_every_download()
    {
        await using var factory = new Factory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var port = factory.Ports;
        string url = $"/api/reporting/v1/customer/organizations/{port.Organization}/reports/day-settlements/export.csv?branchId={port.Branch}&fromDate=2026-09-01&toDate=2026-09-02";
        client.DefaultRequestHeaders.Authorization = new("Bearer", "unauthenticated");
        using var anonymous = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode); Assert.True(anonymous.Headers.CacheControl?.NoStore);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "operator");
        client.DefaultRequestHeaders.Add(TenantContextHeaders.OrganizationId, port.Organization.ToString());
        client.DefaultRequestHeaders.Add(TenantContextHeaders.ApplicationCode, "nexa_connect");
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "settlement-export-test");
        using var success = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, success.StatusCode); Assert.True(success.Headers.CacheControl?.NoStore);
        Assert.Equal("text/csv", success.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", success.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Contains(port.Branch.ToString(), success.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal("settlement-export-test", success.Headers.GetValues("X-Correlation-ID").Single());
        Assert.Contains("\"observation\"", await success.Content.ReadAsStringAsync());
        Assert.Equal(1, port.SourceReads); Assert.Equal(1, port.Comparisons);
        using var invalid = await client.GetAsync(url.Replace("2026-09-02", "2026-11-02"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var foreignBranch = await client.GetAsync(url.Replace(port.Branch.ToString(), Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.Forbidden, foreignBranch.StatusCode);
        client.DefaultRequestHeaders.Remove(TenantContextHeaders.OrganizationId);
        client.DefaultRequestHeaders.Add(TenantContextHeaders.OrganizationId, Guid.NewGuid().ToString());
        using var foreignTenant = await client.GetAsync(url); Assert.Equal(HttpStatusCode.Forbidden, foreignTenant.StatusCode);
        client.DefaultRequestHeaders.Remove(TenantContextHeaders.OrganizationId);
        client.DefaultRequestHeaders.Add(TenantContextHeaders.OrganizationId, port.Organization.ToString());
        port.Allowed = false;
        using var revoked = await client.GetAsync(url); Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        Assert.Equal(1, port.SourceReads); Assert.Equal(1, port.Comparisons);
        port.Allowed = true; port.SourceDenied = true;
        using var sourceDenied = await client.GetAsync(url); Assert.Equal(HttpStatusCode.Forbidden, sourceDenied.StatusCode);
        port.SourceDenied = false; port.SourceDown = true;
        using var down = await client.GetAsync(url); Assert.Equal(HttpStatusCode.ServiceUnavailable, down.StatusCode);
        Assert.True(down.Headers.CacheControl?.NoStore); Assert.Null(down.Content.Headers.ContentDisposition);
        Assert.DoesNotContain("private-source-diagnostic", await down.Content.ReadAsStringAsync());
        Assert.Equal(1, port.Comparisons);
    }
    private sealed class Factory : WebApplicationFactory<REPORTING::Program>
    {
        public Ports Ports { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            TestServiceConfiguration.Configure(builder, "reporting");
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:Reporting"] = "Host=localhost;Database=unused" }));
            builder.ConfigureServices(s =>
            {
                s.RemoveAll<IReportingCustomerAuthorizer>(); s.AddSingleton<IReportingCustomerAuthorizer>(Ports);
                s.RemoveAll<ISettlementSource>(); s.AddSingleton<ISettlementSource>(Ports);
                s.RemoveAll<ISettlementFactRepository>(); s.AddSingleton<ISettlementFactRepository>(Ports);
                s.AddAuthentication(o => { o.DefaultAuthenticateScheme = "ExportTest"; o.DefaultChallengeScheme = "ExportTest"; })
                    .AddScheme<AuthenticationSchemeOptions, Auth>("ExportTest", _ => { });
            });
        }
    }
    private sealed class Auth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(Request.Headers.Authorization == "Bearer unauthenticated"
            ? AuthenticateResult.NoResult() : AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "operator")], Scheme.Name)), Scheme.Name)));
    }
    private sealed class Ports : IReportingCustomerAuthorizer, ISettlementSource, ISettlementFactRepository
    {
        public Guid Organization = Guid.NewGuid(), Branch = Guid.NewGuid(), Restaurant = Guid.NewGuid();
        public bool Allowed = true, SourceDown, SourceDenied; public int SourceReads, Comparisons;
        public Task<bool> IsGrantedAsync(Guid o, Guid? b, string permission, string bearer, CancellationToken ct)
        { Assert.Equal("reporting.sales.read", permission); Assert.Equal("Bearer operator", bearer); return Task.FromResult(Allowed && o == Organization && b == Branch); }
        public Task<SettlementManifest> ReadAsync(SettlementReportRange range, string bearer, CancellationToken ct)
        {
            SourceReads++; Assert.Equal("Bearer operator", bearer);
            if (SourceDenied) throw new UnauthorizedAccessException();
            if (SourceDown) throw new HttpRequestException("private-source-diagnostic");
            return Task.FromResult(new SettlementManifest(Organization, Restaurant, Branch, range.FromDate, range.ToDate, DateTimeOffset.UtcNow, []));
        }
        public Task<SettlementReport> CompareAsync(SettlementManifest manifest, CancellationToken ct)
        {
            Comparisons++;
            return Task.FromResult(new SettlementReport(Organization, Branch, manifest.FromDate, manifest.ToDate, manifest.ObservedAtUtc,
                DateTimeOffset.UtcNow, new('A', 64), "matched", 0, 0, 0, 0, 0, []));
        }
        public Task<bool> ProjectAsync(REPORTING::NexaConnect.Services.Reporting.Domain.SettlementFact f, CancellationToken ct) => throw new NotSupportedException();
    }
}
