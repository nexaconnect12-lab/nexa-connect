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

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains("/bff/customer/login", response.Headers.Location?.ToString());
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
