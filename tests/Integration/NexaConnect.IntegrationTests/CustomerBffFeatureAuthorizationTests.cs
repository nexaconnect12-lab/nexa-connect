extern alias CUSTOMERBFF;

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace NexaConnect.IntegrationTests;

public sealed class CustomerBffFeatureAuthorizationTests
{
    [Theory]
    [InlineData("users")]
    [InlineData("configuration")]
    [InlineData("branches")]
    [InlineData("reports")]
    [InlineData("media")]
    [InlineData("activity")]
    [InlineData("unknown")]
    public async Task Feature_status_requires_a_customer_session(string feature)
    {
        await using var factory = new CustomerBffFactory();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using HttpResponseMessage response = await client.GetAsync($"/bff/customer/features/{feature}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData("GET", "/bff/customer/memberships")]
    [InlineData("PUT", "/bff/customer/memberships/customer-subject")]
    public async Task Membership_routes_require_a_customer_session(string method, string path)
    {
        await using var factory = new CustomerBffFactory();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect=false, BaseAddress=new Uri("https://localhost") });
        using var request=new HttpRequestMessage(new HttpMethod(method),path);
        if(method=="PUT") request.Content=System.Net.Http.Json.JsonContent.Create(new{status="active"});
        using HttpResponseMessage response=await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode); Assert.Null(response.Headers.Location);
    }

    private sealed class CustomerBffFactory : WebApplicationFactory<CUSTOMERBFF::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Bff:Authority"] = "https://identity.test/realms/test",
                    ["Bff:ClientId"] = "nexaconnect-web-bff",
                    ["Bff:ClientSecret"] = "test-secret",
                    ["Bff:RequireHttpsMetadata"] = "true",
                    ["Services:PlatformDirectory"] = "https://directory.test/",
                    ["Services:Catalog"] = "https://catalog.test/",
                    ["Services:Inventory"] = "https://inventory.test/",
                    ["Services:Order"] = "https://order.test/"
                }));
        }
    }
}
