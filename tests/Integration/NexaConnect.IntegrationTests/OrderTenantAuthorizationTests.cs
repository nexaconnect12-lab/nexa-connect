extern alias ORDER;

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using NexaConnect.Contracts.Platform;
using OrderTenantAuthorizer = ORDER::NexaConnect.Services.Order.Application.Tenant.IOrderTenantAuthorizer;

namespace NexaConnect.IntegrationTests;

public sealed class OrderTenantAuthorizationTests : IClassFixture<RestaurantWorkflowServiceFixture>
{
    private readonly RestaurantWorkflowServiceFixture fixture;

    public OrderTenantAuthorizationTests(RestaurantWorkflowServiceFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task Customer_workflow_rejects_when_order_tenant_is_not_authorized()
    {
        using HttpClient client = fixture.Order.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/order/v1/workflows/place");
        request.Headers.TryAddWithoutValidation(TenantContextHeaders.PortalRequest, "customer");
        request.Headers.TryAddWithoutValidation(TenantContextHeaders.OrganizationId, Guid.NewGuid().ToString("D"));
        request.Headers.TryAddWithoutValidation(TenantContextHeaders.ApplicationCode, "nexa_connect");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer customer-token");
        request.Content = JsonContent.Create(new
        {
            RestaurantId = RestaurantWorkflowServiceFixture.RestaurantId,
            OrganizationId = RestaurantWorkflowServiceFixture.OrganizationId,
            BranchId = RestaurantWorkflowServiceFixture.BranchId,
            Currency = "USD",
            PaymentMethod = "card",
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            Lines = Array.Empty<object>()
        });

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Trusted_workload_cannot_bypass_payment_review_permission()
    {
        using HttpClient client=fixture.Order.CreateClient();
        Guid organizationId=RestaurantWorkflowServiceFixture.OrganizationId,branchId=RestaurantWorkflowServiceFixture.BranchId;
        using var request=new HttpRequestMessage(HttpMethod.Get,$"/api/order/v1/payment-reviews?organizationId={organizationId:D}&branchId={branchId:D}");
        request.Headers.TryAddWithoutValidation(TenantContextHeaders.OrganizationId,organizationId.ToString("D"));
        request.Headers.TryAddWithoutValidation(TenantContextHeaders.ApplicationCode,"nexa_connect");
        using HttpResponseMessage response=await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
    }
}

public sealed class DenyOrderTenantAuthorizer : OrderTenantAuthorizer
{
    public Task<bool> HasBranchAccessAsync(Guid organizationId, Guid branchId, string permission, string authorizationHeader, CancellationToken cancellationToken) =>
        Task.FromResult(false);
}
