extern alias REPORTING;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NexaConnect.Contracts.Platform;
using REPORTING::NexaConnect.Services.Reporting.Application;
using REPORTING::NexaConnect.Services.Reporting.Domain;

namespace NexaConnect.IntegrationTests;

public sealed class CashCloseReportHttpTests
{
    [Fact]
    public async Task Exact_store_scope_tenant_headers_and_live_authorization_are_required()
    {
        await using var factory = new Factory(); using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        string url = $"/api/reporting/v1/customer/organizations/{factory.Access.Organization}/reports/cash-close?branchId={factory.Access.Branch}&storeId={factory.Access.Store}&fromUtc=2026-09-01T00:00:00Z&toUtc=2026-09-02T00:00:00Z";
        client.DefaultRequestHeaders.Authorization = new("Bearer", "unauthenticated");
        using var anonymous = await client.GetAsync(url); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "operator");
        client.DefaultRequestHeaders.Add(TenantContextHeaders.ApplicationCode, "nexa_connect");
        client.DefaultRequestHeaders.Add(TenantContextHeaders.OrganizationId, factory.Access.Organization.ToString());
        using var allowed = await client.GetAsync(url); Assert.Equal(HttpStatusCode.OK, allowed.StatusCode); Assert.True(allowed.Headers.CacheControl?.NoStore);
        Assert.Equal(factory.Access.Store, factory.Repository.Last!.StoreId);
        using var foreign = await client.GetAsync(url.Replace(factory.Access.Store.ToString(), Guid.NewGuid().ToString())); Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        factory.Access.Allowed = false;
        using var revoked = await client.GetAsync(url); Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        Assert.Equal(1, factory.Repository.Reads);
    }
    private sealed class Factory : WebApplicationFactory<REPORTING::Program>
    {
        public Access Access { get; } = new(); public Repo Repository { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            TestServiceConfiguration.Configure(builder, "reporting");
            builder.ConfigureServices(s => { s.RemoveAll<ICashCloseAccess>(); s.AddSingleton<ICashCloseAccess>(Access); s.RemoveAll<ICashCloseRepository>(); s.AddSingleton<ICashCloseRepository>(Repository); });
        }
    }
    private sealed class Access : ICashCloseAccess
    {
        public Guid Organization = Guid.NewGuid(), Branch = Guid.NewGuid(), Store = Guid.NewGuid(); public bool Allowed = true;
        public Task<bool> CanReadAsync(Guid o, Guid b, Guid s, string a, CancellationToken ct) => Task.FromResult(Allowed && o == Organization && b == Branch && s == Store);
    }
    private sealed class Repo : ICashCloseRepository
    {
        public int Reads; public CashCloseQuery? Last;
        public Task<bool> ProjectAsync(Guid id, CashCloseSnapshot snapshot, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<CashCloseRow>> ReadAsync(CashCloseQuery query, CancellationToken ct) { Reads++; Last = query; return Task.FromResult<IReadOnlyList<CashCloseRow>>([]); }
    }
}
