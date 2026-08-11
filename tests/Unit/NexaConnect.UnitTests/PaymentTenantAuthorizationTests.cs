using System.Net;
using System.Text.Json;
using NexaConnect.Services.Payment.Infrastructure;

namespace NexaConnect.UnitTests;

public sealed class PaymentTenantAuthorizationTests
{
    [Fact]
    public async Task Access_requires_membership_matching_order_and_matching_restaurant_scope()
    {
        Guid organizationId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        var handler = new RoutingHandler(request => request.RequestUri!.Host switch
        {
            "directory.test" => new HttpResponseMessage(HttpStatusCode.OK),
            "order.test" => Json(new { OrganizationId = organizationId, BranchId = branchId }),
            "restaurant.test" => Json(new { OrganizationId = organizationId, RestaurantId = restaurantId, BranchId = branchId }),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var authorizer = new HttpPaymentTenantAuthorizer(new StubClientFactory(handler), new StubTokenProvider());

        Assert.True(await authorizer.CanAccessAsync(organizationId, restaurantId, branchId, orderId,
            "Bearer customer", CancellationToken.None));
        Assert.False(await authorizer.CanAccessAsync(Guid.NewGuid(), restaurantId, branchId, orderId,
            "Bearer customer", CancellationToken.None));
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json")
    };
}
