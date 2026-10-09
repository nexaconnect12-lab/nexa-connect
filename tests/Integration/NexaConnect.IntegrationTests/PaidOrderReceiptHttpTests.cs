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
public sealed class PaidOrderReceiptHttpTests : IClassFixture<RestaurantWorkflowServiceFixture>
{
    private readonly RestaurantWorkflowServiceFixture fixture;
    public PaidOrderReceiptHttpTests(RestaurantWorkflowServiceFixture fixture) => this.fixture=fixture;

    [Fact]
    public async Task Receipt_requires_live_read_permission_and_scopes_even_trusted_workload_requests()
    {
        var authorization = new ReceiptAuthorization();
        using var host = fixture.Order.WithWebHostBuilder(b=>b.ConfigureServices(services=>
        {
            services.RemoveAll<IOrderTenantAuthorizer>();
            services.AddSingleton<IOrderTenantAuthorizer>(authorization);
        }));
        using var client = host.CreateClient();
        var repository = host.Services.GetRequiredService<InMemoryOrderApplicationService>();
        var order=OrderAggregate.Create(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),[new(Guid.NewGuid(),"Tea",40,1,"bar")],"THB");
        order.Submit(); order.MarkInventoryReserved(); order.MarkKitchenAccepted();
        await repository.SaveAsync(order,default);
        async Task<HttpResponseMessage> Read(Guid organization,Guid branch,string bearer="Bearer customer-token")
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,$"/api/order/v1/orders/{order.Id}/receipt?branchId={branch}");
            request.Headers.Add(TenantContextHeaders.OrganizationId,organization.ToString());
            request.Headers.Add(TenantContextHeaders.ApplicationCode,"nexa_connect");
            request.Headers.TryAddWithoutValidation("Authorization",bearer);
            return await client.SendAsync(request);
        }
        using(var unpaid=await Read(order.OrganizationId,order.BranchId)) Assert.Equal(HttpStatusCode.NotFound,unpaid.StatusCode);
        order.MarkPaid(); order.IssueReceipt(DateTimeOffset.UtcNow,"cash");
        using(var paid=await Read(order.OrganizationId,order.BranchId))
        {
            Assert.Equal(HttpStatusCode.OK,paid.StatusCode);
            Assert.True(paid.Headers.CacheControl!.NoStore);
            Assert.Equal(order.Receipt!.ReceiptNumber,(await paid.Content.ReadFromJsonAsync<PaidOrderReceipt>())!.ReceiptNumber);
        }
        using(var otherTenant=await Read(Guid.NewGuid(),order.BranchId)) Assert.Equal(HttpStatusCode.NotFound,otherTenant.StatusCode);
        using(var otherBranch=await Read(order.OrganizationId,Guid.NewGuid())) Assert.Equal(HttpStatusCode.NotFound,otherBranch.StatusCode);
        authorization.Allowed=false;
        using(var revoked=await Read(order.OrganizationId,order.BranchId)) Assert.Equal(HttpStatusCode.Forbidden,revoked.StatusCode);
        using(var anonymous=await Read(order.OrganizationId,order.BranchId,"Bearer unauthenticated")) Assert.Equal(HttpStatusCode.Unauthorized,anonymous.StatusCode);
        using(var workload=await client.GetAsync($"/api/order/v1/orders/{order.Id}/receipt?branchId={order.BranchId}")) Assert.Equal(HttpStatusCode.Forbidden,workload.StatusCode);
    }
    private sealed class ReceiptAuthorization : IOrderTenantAuthorizer
    {
        public bool Allowed {get;set;}=true;
        public Task<bool> HasBranchAccessAsync(Guid organizationId,Guid branchId,string permission,string authorizationHeader,CancellationToken cancellationToken)
        {
            Assert.Equal(ProductPermissions.OrderRead,permission);
            return Task.FromResult(Allowed);
        }
    }
}
