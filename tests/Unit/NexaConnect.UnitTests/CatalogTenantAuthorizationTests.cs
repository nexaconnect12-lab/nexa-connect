using System.Net;
using NexaConnect.Services.Catalog.Application.Tenant;
using NexaConnect.Services.Catalog.Infrastructure;
using NexaConnect.Infrastructure.Authorization;
using NexaConnect.Contracts.Platform;

namespace NexaConnect.UnitTests;

public sealed class CatalogTenantAuthorizationTests
{
    [Fact]
    public async Task Branch_access_requires_platform_membership_and_matching_restaurant_scope()
    {
        Guid organizationId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        using var platformClient = new HttpClient(new StatusHandler(HttpStatusCode.OK))
        {
            BaseAddress = new Uri("https://platform.test/")
        };
        var checker = new HttpOrganizationAccessChecker(
            platformClient,
            new StubBranchScopeReader(new RestaurantBranchScope(organizationId, Guid.NewGuid(), branchId)),
            GrantedAuthorizationClient());

        Assert.True(await checker.HasBranchAccessAsync(organizationId, branchId, ProductPermissions.CatalogMenuRead, "Bearer customer-token", CancellationToken.None));
        Assert.False(await checker.HasBranchAccessAsync(Guid.NewGuid(), branchId, ProductPermissions.CatalogMenuRead, "Bearer customer-token", CancellationToken.None));
    }

    [Fact]
    public async Task Branch_access_rejects_scope_owned_by_another_organization()
    {
        Guid organizationId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        using var platformClient = new HttpClient(new StatusHandler(HttpStatusCode.OK))
        {
            BaseAddress = new Uri("https://platform.test/")
        };
        var checker = new HttpOrganizationAccessChecker(
            platformClient,
            new StubBranchScopeReader(new RestaurantBranchScope(Guid.NewGuid(), Guid.NewGuid(), branchId)),
            GrantedAuthorizationClient());

        Assert.False(await checker.HasBranchAccessAsync(organizationId, branchId, ProductPermissions.CatalogMenuRead, "Bearer customer-token", CancellationToken.None));
    }

    private static ProductAuthorizationClient GrantedAuthorizationClient() => new(new HttpClient(
        new JsonHandler()) { BaseAddress = new Uri("https://authorization.test/") });

    private sealed class StubBranchScopeReader(RestaurantBranchScope? scope) : IRestaurantBranchScopeReader
    {
        public Task<RestaurantBranchScope?> GetAsync(Guid branchId, CancellationToken cancellationToken) => Task.FromResult(scope);
    }

    private sealed class StatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }

    private sealed class JsonHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"decisionId\":\"00000000-0000-0000-0000-000000000001\",\"granted\":true}",
                    System.Text.Encoding.UTF8, "application/json")
            });
    }
}
