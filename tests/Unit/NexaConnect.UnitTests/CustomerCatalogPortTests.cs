using System.Net;
using NexaConnect.Contracts.Platform;
using NexaConnect.CustomerBff.Infrastructure.Catalog;

namespace NexaConnect.UnitTests;

public sealed class CustomerCatalogPortTests
{
    [Fact]
    public async Task Catalog_adapter_forwards_bearer_and_validated_tenant_headers()
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://catalog.test/") };
        var adapter = new HttpCustomerCatalogPort(client);
        var tenant = new TenantContext("subject-1", Guid.NewGuid(), "nexa_connect");

        using HttpResponseMessage response = await adapter.GetMenuAsync(tenant, Guid.NewGuid(), "access-token", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Bearer access-token", handler.Authorization);
        Assert.Equal(tenant.OrganizationId.ToString("D"), handler.OrganizationId);
        Assert.Equal("nexa_connect", handler.ApplicationCode);
        Assert.Equal("customer", handler.PortalRequest);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? Authorization { get; private set; }
        public string? OrganizationId { get; private set; }
        public string? ApplicationCode { get; private set; }
        public string? PortalRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            OrganizationId = request.Headers.GetValues(TenantContextHeaders.OrganizationId).Single();
            ApplicationCode = request.Headers.GetValues(TenantContextHeaders.ApplicationCode).Single();
            PortalRequest = request.Headers.GetValues(TenantContextHeaders.PortalRequest).Single();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
