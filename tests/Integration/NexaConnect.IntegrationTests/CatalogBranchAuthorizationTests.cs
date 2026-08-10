extern alias CATALOG;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NexaConnect.Contracts.Platform;
using CatalogCreateMenuItem = CATALOG::NexaConnect.Services.Catalog.Application.Menu.CreateMenuItem;
using CatalogTenantAuthorizer = CATALOG::NexaConnect.Services.Catalog.Application.Tenant.ICatalogTenantAuthorizer;
using CatalogProgram = CATALOG::CatalogProgram;

namespace NexaConnect.IntegrationTests;

public sealed class CatalogBranchAuthorizationTests : IClassFixture<CatalogAuthorizationFactory>
{
    private static readonly Guid OrganizationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid BranchId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly CatalogAuthorizationFactory factory;

    public CatalogBranchAuthorizationTests(CatalogAuthorizationFactory factory) => this.factory = factory;

    [Fact]
    public async Task Customer_menu_read_allows_only_branch_owned_by_selected_organization()
    {
        using HttpClient client = factory.CreateClient();
        Guid productId = Guid.NewGuid();
        using var create = await client.PostAsJsonAsync(
            $"/api/catalog/v1/branches/{BranchId:D}/menu-items",
            new CatalogCreateMenuItem(productId, "Tenant Burger", 10m, "USD", "grill"));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        using HttpRequestMessage allowed = CustomerRequest(OrganizationId);
        using HttpResponseMessage allowedResponse = await client.SendAsync(allowed);
        Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);

        using HttpRequestMessage denied = CustomerRequest(Guid.NewGuid());
        using HttpResponseMessage deniedResponse = await client.SendAsync(denied);
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
    }

    private static HttpRequestMessage CustomerRequest(Guid organizationId)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/catalog/v1/branches/{BranchId:D}/menu-items");
        request.Headers.TryAddWithoutValidation(TenantContextHeaders.PortalRequest, "customer");
        request.Headers.TryAddWithoutValidation(TenantContextHeaders.OrganizationId, organizationId.ToString("D"));
        request.Headers.TryAddWithoutValidation(TenantContextHeaders.ApplicationCode, "nexa_connect");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer integration-test-token");
        return request;
    }
}

public sealed class CatalogAuthorizationFactory : WebApplicationFactory<CatalogProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        TestServiceConfiguration.Configure(builder, "catalog");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<CatalogTenantAuthorizer>();
            services.AddSingleton<CatalogTenantAuthorizer>(new StubCatalogTenantAuthorizer());
        });
    }

    private sealed class StubCatalogTenantAuthorizer : CatalogTenantAuthorizer
    {
        private static readonly Guid organizationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        private static readonly Guid branchId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        public Task<bool> HasAccessAsync(Guid requestedOrganizationId, string authorizationHeader, CancellationToken cancellationToken) =>
            Task.FromResult(requestedOrganizationId == organizationId);

        public Task<bool> HasBranchAccessAsync(Guid requestedOrganizationId, Guid requestedBranchId, string authorizationHeader, CancellationToken cancellationToken) =>
            Task.FromResult(requestedOrganizationId == organizationId && requestedBranchId == branchId);
    }
}
