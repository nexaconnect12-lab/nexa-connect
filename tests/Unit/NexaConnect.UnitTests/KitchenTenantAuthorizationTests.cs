using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NexaConnect.Contracts.Platform;
using NexaConnect.Infrastructure.Authorization;
using NexaConnect.Services.Kitchen.Infrastructure;

namespace NexaConnect.UnitTests;

public sealed class KitchenTenantAuthorizationTests
{
    [Fact]
    public async Task Operator_access_uses_customer_membership_and_kitchen_workload_for_branch_scope()
    {
        Guid organizationId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        bool usedWorkloadToken = false;
        var handler = new RoutingHandler(request => request.RequestUri!.Host switch
        {
            "directory.test" when request.Headers.Authorization?.Parameter == "customer" =>
                new HttpResponseMessage(HttpStatusCode.OK),
            "restaurant.test" when request.Headers.Authorization?.Parameter == "workload-token" =>
                MarkWorkloadAndJson(),
            _ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        });
        var authorization = new ProductAuthorizationClient(
            new HttpClient(new RoutingHandler(_ => Json(new { DecisionId = Guid.NewGuid(), Granted = true })))
            { BaseAddress = new Uri("https://authorization.test/") },
            NullLogger<ProductAuthorizationClient>.Instance);
        var authorizer = new HttpKitchenTenantAuthorizer(new StubClientFactory(handler), new StubTokenProvider(), authorization);

        Assert.True(await authorizer.HasBranchAccessAsync(organizationId, branchId,
            ProductPermissions.KitchenTicketRead, "Bearer customer", CancellationToken.None));
        Assert.True(usedWorkloadToken);
        return;

        HttpResponseMessage MarkWorkloadAndJson()
        {
            usedWorkloadToken = true;
            return Json(new { OrganizationId = organizationId, RestaurantId = restaurantId, BranchId = branchId });
        }
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json")
    };
}
