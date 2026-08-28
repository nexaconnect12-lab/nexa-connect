extern alias PAYMENT;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using PaymentProgram = PAYMENT::PaymentProgram;

namespace NexaConnect.IntegrationTests;

public sealed class PaymentHealthEndpointTests
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task In_memory_payment_health_endpoints_are_anonymous_and_healthy(string path)
    {
        await using var factory = new WebApplicationFactory<PaymentProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "InMemory",
                    ["Authentication:Authority"] = "https://localhost:8080/realms/test",
                    ["Authentication:Audience"] = "nexaconnect-api",
                    ["Authentication:RequireHttpsMetadata"] = "false"
                }));
        });
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }
}
