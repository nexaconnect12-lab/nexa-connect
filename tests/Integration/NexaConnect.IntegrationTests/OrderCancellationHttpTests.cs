extern alias ORDER;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NexaConnect.Contracts.Platform;
using ORDER::NexaConnect.Services.Order.Application.Orders;
using ORDER::NexaConnect.Services.Order.Application.Tenant;
using ORDER::NexaConnect.Services.Order.Domain;

namespace NexaConnect.IntegrationTests;

public sealed class OrderCancellationHttpTests : IClassFixture<RestaurantWorkflowServiceFixture>
{
    private readonly RestaurantWorkflowServiceFixture fixture;
    public OrderCancellationHttpTests(RestaurantWorkflowServiceFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task Cancellation_requires_live_branch_permission_and_replays_the_same_operation()
    {
        var authorization = new CancellationAuthorization();
        using var host = fixture.Order.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IOrderTenantAuthorizer>();
            services.AddSingleton<IOrderTenantAuthorizer>(authorization);
        }));
        using var client = host.CreateClient();
        var repository = host.Services.GetRequiredService<InMemoryOrderApplicationService>();
        var order = OrderAggregate.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            [new(Guid.NewGuid(), "Tea", 40, 1, "bar")], "THB");
        order.Submit(); order.MarkInventoryReserved(); order.MarkKitchenAccepted();
        await repository.SaveAsync(order, default);
        Guid operationId = Guid.NewGuid();

        async Task<HttpResponseMessage> Cancel(Guid organization, Guid branch, string reason = "Customer changed order",
            string bearer = "Bearer customer-token")
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"/api/order/v1/orders/{order.Id:D}/cancellations");
            request.Headers.Add(TenantContextHeaders.OrganizationId, organization.ToString("D"));
            request.Headers.Add(TenantContextHeaders.ApplicationCode, "nexa_connect");
            request.Headers.TryAddWithoutValidation("Authorization", bearer);
            request.Content = JsonContent.Create(new { organizationId = organization, branchId = branch,
                operationId, reason, correlationId = operationId });
            return await client.SendAsync(request);
        }

        using (var otherTenant = await Cancel(Guid.NewGuid(), order.BranchId))
            Assert.Equal(HttpStatusCode.NotFound, otherTenant.StatusCode);
        using (var otherBranch = await Cancel(order.OrganizationId, Guid.NewGuid()))
            Assert.Equal(HttpStatusCode.NotFound, otherBranch.StatusCode);
        authorization.Allowed = false;
        using (var revoked = await Cancel(order.OrganizationId, order.BranchId))
            Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        authorization.Allowed = true;
        using (var accepted = await Cancel(order.OrganizationId, order.BranchId))
        {
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            var result = await accepted.Content.ReadFromJsonAsync<CancellationResponse>();
            Assert.Equal("completed", result!.Status);
            Assert.False(result.Replayed);
        }
        using (var replay = await Cancel(order.OrganizationId, order.BranchId))
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.True((await replay.Content.ReadFromJsonAsync<CancellationResponse>())!.Replayed);
        }
        using (var changedReason = await Cancel(order.OrganizationId, order.BranchId, "A different reason"))
            Assert.Equal(HttpStatusCode.Conflict, changedReason.StatusCode);
        Assert.Equal(OrderStatus.Cancelled, repository.Get(order.Id)!.Status);
    }

    private sealed record CancellationResponse(Guid OrderId, Guid OperationId, string Status, bool Replayed);
    private sealed class CancellationAuthorization : IOrderTenantAuthorizer
    {
        public bool Allowed { get; set; } = true;
        public Task<bool> HasBranchAccessAsync(Guid organizationId, Guid branchId, string permission,
            string authorizationHeader, CancellationToken cancellationToken) => Task.FromResult(Allowed);
        public Task<Guid?> GetBranchDecisionAsync(Guid organizationId, Guid branchId, string permission,
            string authorizationHeader, CancellationToken cancellationToken)
        {
            Assert.Equal(ProductPermissions.OrderCancel, permission);
            return Task.FromResult<Guid?>(Allowed ? Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa") : null);
        }
    }
}
