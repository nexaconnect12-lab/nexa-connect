extern alias CUSTOMERBFF;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NexaConnect.Contracts.Platform;
using CUSTOMERBFF::NexaConnect.CustomerBff.Application.Kitchen;
using CUSTOMERBFF::NexaConnect.CustomerBff.Infrastructure.Kitchen;

namespace NexaConnect.IntegrationTests;

public sealed class CustomerKitchenAdapterTests
{
    [Theory]
    [InlineData(KitchenOperation.Queue)] [InlineData(KitchenOperation.Detail)] [InlineData(KitchenOperation.Transition)]
    public async Task Adapter_uses_fixed_routes_server_tenant_and_allowlisted_body(KitchenOperation operation)
    {
        var tenant = new TenantContext("operator", Guid.NewGuid(), "nexa_connect");
        Guid branch = Guid.NewGuid(), ticket = Guid.NewGuid();
        using var handler = new Handler(async message =>
        {
            Assert.Equal("Bearer server-token", message.Headers.Authorization!.ToString());
            Assert.Equal(tenant.OrganizationId.ToString(), Assert.Single(message.Headers.GetValues(TenantContextHeaders.OrganizationId)));
            Assert.Equal("nexa_connect", Assert.Single(message.Headers.GetValues(TenantContextHeaders.ApplicationCode)));
            Assert.Equal("customer", Assert.Single(message.Headers.GetValues(TenantContextHeaders.PortalRequest)));
            string root = $"/api/kitchen/v1/branches/{branch}/tickets";
            Assert.Equal(operation switch
            {
                KitchenOperation.Queue => root + "?limit=25&station=grill%26other%3Dvalue&cursor=cursor",
                KitchenOperation.Detail => root + $"/{ticket}",
                _ => root + $"/{ticket}/transitions"
            }, message.RequestUri!.PathAndQuery);
            if (operation == KitchenOperation.Transition)
            {
                Assert.Equal(HttpMethod.Post, message.Method);
                var body = await message.Content!.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(2, body.EnumerateObject().Count());
                Assert.Equal(9, body.GetProperty("expectedConcurrencyVersion").GetInt64());
            }
            else { Assert.Equal(HttpMethod.Get, message.Method); Assert.Null(message.Content); }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { }) };
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://kitchen.test/") };
        var port = new HttpCustomerKitchenPort(client, new Factory(client));
        using var response = await port.SendAsync(tenant, "server-token", operation,
            new(branch, ticket, "grill&other=value", 25, "cursor", new("Ready", 9)), default);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request); }
    private sealed class Factory(HttpClient client) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => client; }
}
