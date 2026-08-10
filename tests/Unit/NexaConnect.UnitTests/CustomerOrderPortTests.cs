using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NexaConnect.Contracts.Platform;
using NexaConnect.CustomerBff.Application.Orders;
using NexaConnect.CustomerBff.Infrastructure.Orders;

namespace NexaConnect.UnitTests;

public sealed class CustomerOrderPortTests
{
    [Fact]
    public async Task Order_adapter_overrides_client_tenant_ids_and_forwards_context()
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://order.test/") };
        var adapter = new HttpCustomerOrderPort(client);
        var tenant = new TenantContext("subject-1", Guid.NewGuid(), "nexa_connect");
        Guid branchId = Guid.NewGuid();
        var request = new CustomerPlaceOrderRequest(
            Guid.NewGuid(), "USD", "card", "order-key", [new CustomerOrderLine(Guid.NewGuid(), 1)]);

        using HttpResponseMessage response = await adapter.PlaceAsync(tenant, branchId, request, "access-token", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Bearer access-token", handler.Authorization);
        Assert.Equal(tenant.OrganizationId.ToString("D"), handler.OrganizationId);
        Assert.Equal("customer", handler.PortalRequest);
        using JsonDocument payload = JsonDocument.Parse(handler.Body!);
        Assert.Equal(tenant.OrganizationId, payload.RootElement.GetProperty("organizationId").GetGuid());
        Assert.Equal(branchId, payload.RootElement.GetProperty("branchId").GetGuid());
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? Authorization { get; private set; }
        public string? OrganizationId { get; private set; }
        public string? PortalRequest { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            OrganizationId = request.Headers.GetValues(TenantContextHeaders.OrganizationId).Single();
            PortalRequest = request.Headers.GetValues(TenantContextHeaders.PortalRequest).Single();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
