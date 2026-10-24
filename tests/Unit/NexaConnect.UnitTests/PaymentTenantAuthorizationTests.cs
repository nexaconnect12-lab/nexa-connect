using System.Net;
using System.Text.Json;
using NexaConnect.Services.Payment.Infrastructure;
using NexaConnect.Infrastructure.Authorization;
using NexaConnect.Contracts.Platform;
using Microsoft.Extensions.Logging.Abstractions;

namespace NexaConnect.UnitTests;

public sealed class PaymentTenantAuthorizationTests
{
    [Fact]
    public async Task End_of_day_read_requires_both_live_permissions_and_matching_branch_hierarchy()
    {
        Guid organization = Guid.NewGuid(), restaurant = Guid.NewGuid(), branch = Guid.NewGuid();
        bool membership=true, refundAllowed=true;
        int decisions=0;
        var handler = new RoutingHandler(request =>
        {
            if(request.RequestUri!.Host=="directory.test")
            { Assert.Equal("Bearer customer",request.Headers.Authorization!.ToString());return new(membership?HttpStatusCode.OK:HttpStatusCode.Forbidden); }
            Assert.Equal("restaurant.test",request.RequestUri.Host);
            Assert.Equal("Bearer workload-token",request.Headers.Authorization!.ToString());
            return Json(new{OrganizationId=organization,RestaurantId=restaurant,BranchId=branch});
        });
        var authorization=new ProductAuthorizationClient(new HttpClient(new RoutingHandler(request=>
        {
            Assert.Equal("Bearer customer",request.Headers.Authorization!.ToString());decisions++;
            bool refund=request.Content!.ReadAsStringAsync().GetAwaiter().GetResult().Contains("payment.refund.read",StringComparison.Ordinal);
            return Json(new{DecisionId=Guid.NewGuid(),Granted=!refund||refundAllowed});
        })){BaseAddress=new Uri("https://authorization.test/")},NullLogger<ProductAuthorizationClient>.Instance);
        var reader=new HttpPaymentTenantAuthorizer(new StubClientFactory(handler),new StubTokenProvider(),authorization);
        Assert.True(await reader.CanReadBranchFinancialsAsync(organization,restaurant,branch,"Bearer customer",default));Assert.Equal(2,decisions);
        refundAllowed=false;Assert.False(await reader.CanReadBranchFinancialsAsync(organization,restaurant,branch,"Bearer customer",default));
        int before=decisions;
        Assert.False(await reader.CanReadBranchFinancialsAsync(Guid.NewGuid(),restaurant,branch,"Bearer customer",default));Assert.Equal(before,decisions);
        Assert.False(await reader.CanReadBranchFinancialsAsync(organization,Guid.NewGuid(),branch,"Bearer customer",default));Assert.Equal(before,decisions);
        membership=false;Assert.False(await reader.CanReadBranchFinancialsAsync(organization,restaurant,branch,"Bearer customer",default));Assert.Equal(before,decisions);
    }
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
        var authorizer = new HttpPaymentTenantAuthorizer(new StubClientFactory(handler), new StubTokenProvider(),
            new ProductAuthorizationClient(new HttpClient(new RoutingHandler(_ => Json(new { DecisionId = Guid.NewGuid(), Granted = true })))
            { BaseAddress = new Uri("https://authorization.test/") }, NullLogger<ProductAuthorizationClient>.Instance));

        Assert.True(await authorizer.CanAccessAsync(organizationId, restaurantId, branchId, orderId,
            ProductPermissions.PaymentIntentRead, "Bearer customer", CancellationToken.None));
        Assert.False(await authorizer.CanAccessAsync(Guid.NewGuid(), restaurantId, branchId, orderId,
            ProductPermissions.PaymentIntentRead, "Bearer customer", CancellationToken.None));
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json")
    };
}
