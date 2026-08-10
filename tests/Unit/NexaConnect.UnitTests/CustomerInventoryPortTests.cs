using System.Net;
using NexaConnect.Contracts.Platform;
using NexaConnect.CustomerBff.Infrastructure.Inventory;

namespace NexaConnect.UnitTests;

public sealed class CustomerInventoryPortTests
{
    [Fact]
    public async Task Inventory_adapter_forwards_bearer_and_tenant_context()
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://inventory.test/") };
        var adapter = new HttpCustomerInventoryPort(client);
        var tenant = new TenantContext("subject-1", Guid.NewGuid(), "nexa_connect");
        Guid branchId = Guid.NewGuid();

        using HttpResponseMessage response = await adapter.GetStockAsync(tenant, branchId, "access-token", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"/api/inventory/v1/branches/{branchId:D}/stock", handler.Path);
        Assert.Equal("Bearer access-token", handler.Authorization);
        Assert.Equal(tenant.OrganizationId.ToString("D"), handler.OrganizationId);
        Assert.Equal("nexa_connect", handler.ApplicationCode);
        Assert.Equal("customer", handler.PortalRequest);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? Authorization { get; private set; }
        public string? OrganizationId { get; private set; }
        public string? ApplicationCode { get; private set; }
        public string? PortalRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri?.PathAndQuery;
            Authorization = request.Headers.Authorization?.ToString();
            OrganizationId = request.Headers.GetValues(TenantContextHeaders.OrganizationId).Single();
            ApplicationCode = request.Headers.GetValues(TenantContextHeaders.ApplicationCode).Single();
            PortalRequest = request.Headers.GetValues(TenantContextHeaders.PortalRequest).Single();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
