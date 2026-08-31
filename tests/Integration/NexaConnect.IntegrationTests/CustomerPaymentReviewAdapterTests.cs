extern alias CUSTOMERBFF;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NexaConnect.Contracts.Platform;
using CUSTOMERBFF::NexaConnect.CustomerBff.Application.PaymentReviews;
using CUSTOMERBFF::NexaConnect.CustomerBff.Infrastructure.Orders;

namespace NexaConnect.IntegrationTests;

public sealed class CustomerPaymentReviewAdapterTests
{
    [Theory]
    [InlineData(ReviewOperation.List)]
    [InlineData(ReviewOperation.Detail)]
    [InlineData(ReviewOperation.History)]
    [InlineData(ReviewOperation.Access)]
    [InlineData(ReviewOperation.Resolve)]
    public async Task Adapter_uses_fixed_routes_and_server_owned_tenant_and_token(ReviewOperation operation)
    {
        var tenant=new TenantContext("operator",Guid.NewGuid(),"nexa_connect");Guid id=Guid.NewGuid();
        using var handler=new Handler(async request=>
        {
            Assert.Equal("Bearer server-token",request.Headers.Authorization!.ToString());
            Assert.Equal(tenant.OrganizationId.ToString(),Assert.Single(request.Headers.GetValues(TenantContextHeaders.OrganizationId)));
            Assert.Equal("nexa_connect",Assert.Single(request.Headers.GetValues(TenantContextHeaders.ApplicationCode)));
            Assert.Equal("customer",Assert.Single(request.Headers.GetValues(TenantContextHeaders.PortalRequest)));
            string expected=operation switch
            {
                ReviewOperation.List=>$"?organizationId={tenant.OrganizationId}&branchId={id}&limit=100",
                ReviewOperation.Access=>$"/branches/{id}/access?organizationId={tenant.OrganizationId}",
                ReviewOperation.History=>$"/{id}/history?organizationId={tenant.OrganizationId}",
                ReviewOperation.Detail=>$"/{id}?organizationId={tenant.OrganizationId}",
                _=>$"/{id}/resolve",
            };
            Assert.Equal("/api/order/v1/payment-reviews"+expected,request.RequestUri!.PathAndQuery);
            if(operation==ReviewOperation.Resolve)
            {
                Assert.Equal(HttpMethod.Post,request.Method);
                var body=await request.Content!.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(4,body.EnumerateObject().Count());Assert.Equal(tenant.OrganizationId,body.GetProperty("organizationId").GetGuid());
                Assert.Equal(9,body.GetProperty("expectedConcurrencyVersion").GetInt64());
            }
            else {Assert.Equal(HttpMethod.Get,request.Method);Assert.Null(request.Content);}
            return new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new{})};
        });
        using var client=new HttpClient(handler){BaseAddress=new Uri("https://order.test/")};
        var adapter=new HttpCustomerPaymentReviewPort(client,new ClientFactory(client));
        using var response=await adapter.SendAsync(tenant,"server-token",operation,id,new("escalate","investigate",9),default);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
    }
    private sealed class Handler(Func<HttpRequestMessage,Task<HttpResponseMessage>> send):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>send(request);}
    private sealed class ClientFactory(HttpClient client):IHttpClientFactory
    {public HttpClient CreateClient(string name)=>client;}
}
