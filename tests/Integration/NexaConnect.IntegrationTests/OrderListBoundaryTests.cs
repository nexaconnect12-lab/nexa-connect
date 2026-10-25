extern alias ORDER;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ORDER::NexaConnect.Services.Order.Application.Orders;
using ORDER::NexaConnect.Services.Order.Application.Tenant;

namespace NexaConnect.IntegrationTests;

public sealed class OrderListBoundaryTests
{
    [Fact]
    public async Task History_requires_staff_tenant_and_live_branch_authority_and_is_no_store()
    {
        await using var factory = new Factory(); using var client = factory.CreateClient(new() { BaseAddress = new("https://localhost") });
        string path = factory.Path;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-Staff", "yes"); client.DefaultRequestHeaders.Authorization = new("Bearer", "synthetic");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Nexa-Organization-Id", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Nexa-Application-Code", "nexa_connect");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Nexa-Organization-Id");
        client.DefaultRequestHeaders.Add("X-Nexa-Organization-Id", factory.Scope.OrganizationId.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(0, factory.Store.Calls);
        factory.Authority.Allowed = true;
        string correlation = Guid.NewGuid().ToString("D"); client.DefaultRequestHeaders.Add("X-Correlation-ID", correlation);
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(correlation, Assert.Single(response.Headers.GetValues("X-Correlation-ID")));
        Assert.Equal(factory.Scope.BranchId, (await response.Content.ReadFromJsonAsync<BranchOrderPage>())!.BranchId);
        factory.Authority.Allowed = false;
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData("&limit=101")] [InlineData("&channel=foreign")] [InlineData("&cursor=invalid?value")]
    public async Task Invalid_query_is_rejected_without_a_storage_read(string query)
    {
        await using var factory = new Factory(); using var client = factory.CreateClient(new() { BaseAddress = new("https://localhost") });
        Staff(client, factory.Scope); factory.Authority.Allowed = true;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(factory.Path + query)).StatusCode);
        Assert.Equal(0, factory.Store.Calls);
    }

    [Fact]
    public async Task Repository_failure_cannot_return_private_diagnostics()
    {
        await using var factory = new Factory(); using var client = factory.CreateClient(new() { BaseAddress = new("https://localhost") });
        Staff(client, factory.Scope); factory.Authority.Allowed = true; factory.Store.Fail = true;
        using var response = await client.GetAsync(factory.Path);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("private-database-detail", await response.Content.ReadAsStringAsync());
    }

    private static void Staff(HttpClient client, OrderListScope scope)
    {
        client.DefaultRequestHeaders.Add("X-Test-Staff", "yes"); client.DefaultRequestHeaders.Authorization = new("Bearer", "synthetic");
        client.DefaultRequestHeaders.Add("X-Nexa-Organization-Id", scope.OrganizationId.ToString());
        client.DefaultRequestHeaders.Add("X-Nexa-Application-Code", "nexa_connect");
    }

    private sealed class Factory : WebApplicationFactory<ORDER::OrderProgram>
    {
        public readonly OrderListScope Scope = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        public readonly Store Store = new(); public readonly Authority Authority = new();
        public string Path => $"/api/order/v1/organizations/{Scope.OrganizationId}/branches/{Scope.BranchId}/orders?restaurantId={Scope.RestaurantId}";
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            { ["Persistence:Provider"] = "InMemory", ["Authentication:Authority"] = "https://identity.test/realms/test",
                ["WorkflowRecovery:Enabled"] = "false", ["QrCollection:Enabled"] = "false" }));
            builder.ConfigureTestServices(services =>
            {
                foreach (var descriptor in services.Where(d => d.ImplementationType?.Name is "OrderWorkflowRecoveryWorker" or "PaymentReconciliationOperationalMetricsWorker").ToArray()) services.Remove(descriptor);
                services.RemoveAll<IOrderListRepository>(); services.AddSingleton<IOrderListRepository>(Store);
                services.RemoveAll<IOrderTenantAuthorizer>(); services.AddSingleton<IOrderTenantAuthorizer>(Authority);
                services.AddAuthentication(o => { o.DefaultAuthenticateScheme = "order-list-test"; o.DefaultChallengeScheme = "order-list-test"; o.DefaultForbidScheme = "order-list-test"; })
                    .AddScheme<AuthenticationSchemeOptions, Handler>("order-list-test", _ => { });
            });
        }
    }

    private sealed class Store : IOrderListRepository
    {
        public int Calls; public bool Fail;
        public Task<IReadOnlyList<OrderListRow>> ReadAsync(OrderListScope scope, string channel, string state, OrderListCursor? cursor, int limit, CancellationToken ct)
        { Calls++; if (Fail) throw new InvalidOperationException("private-database-detail"); return Task.FromResult<IReadOnlyList<OrderListRow>>([]); }
    }
    private sealed class Authority : IOrderTenantAuthorizer
    {
        public bool Allowed;
        public Task<bool> HasBranchAccessAsync(Guid org, Guid branch, string permission, string bearer, CancellationToken ct) => Task.FromResult(Allowed);
        public Task<Guid?> GetBranchDecisionAsync(Guid org, Guid branch, string permission, string bearer, CancellationToken ct) => Task.FromResult<Guid?>(Allowed ? Guid.NewGuid() : null);
    }
    private sealed class Handler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(Request.Headers["X-Test-Staff"] == "yes"
            ? AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "staff"), new Claim(ClaimTypes.Role, "customer-user")], Scheme.Name)), Scheme.Name))
            : AuthenticateResult.NoResult());
    }
}
