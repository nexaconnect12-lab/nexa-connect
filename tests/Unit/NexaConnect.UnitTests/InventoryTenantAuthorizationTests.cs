using System.Net;
using System.Text.Json;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Inventory.Infrastructure;

namespace NexaConnect.UnitTests;

public sealed class InventoryTenantAuthorizationTests
{
    [Fact]
    public async Task Access_requires_membership_and_matching_branch_scope()
    {
        Guid organizationId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        var handler = new RoutingHandler(request => request.RequestUri!.Host switch
        {
            "directory.test" => new HttpResponseMessage(HttpStatusCode.OK),
            "restaurant.test" => Json(new { OrganizationId = organizationId, RestaurantId = Guid.NewGuid(), BranchId = branchId }),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var authorizer = new HttpInventoryTenantAuthorizer(
            new StubClientFactory(handler), new StubTokenProvider());

        Assert.True(await authorizer.HasBranchAccessAsync(organizationId, branchId, "Bearer customer", CancellationToken.None));
        Assert.False(await authorizer.HasBranchAccessAsync(Guid.NewGuid(), branchId, "Bearer customer", CancellationToken.None));
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json")
    };
}

internal sealed class StubTokenProvider : IServiceWorkloadTokenProvider
{
    public Task<string> GetAsync(CancellationToken cancellationToken) => Task.FromResult("workload-token");
}

internal sealed class StubClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
    {
        BaseAddress = name.Contains("Directory", StringComparison.Ordinal) ? new Uri("https://directory.test/") :
            name.Contains("Restaurant", StringComparison.Ordinal) ? new Uri("https://restaurant.test/") : new Uri("https://order.test/")
    };
}

internal sealed class RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(route(request));
}
