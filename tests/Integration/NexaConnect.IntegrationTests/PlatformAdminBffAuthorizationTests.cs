extern alias PLATFORMADMIN;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace NexaConnect.IntegrationTests;

public sealed class PlatformAdminBffAuthorizationTests
{
    [Fact]
    public async Task Proxy_content_is_replayable()
    {
        const string json = """{"code":"phase2-test","name":"Phase 2 Test"}""";
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json; charset=utf-8";
        context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        using HttpContent content = await PLATFORMADMIN::ReplayableProxyContent.CreateAsync(context.Request);
        string firstSend = await content.ReadAsStringAsync();
        string secondSend = await content.ReadAsStringAsync();

        Assert.Equal(json, firstSend);
        Assert.Equal(json, secondSend);
        Assert.Equal("application/json; charset=utf-8", content.Headers.ContentType?.ToString());
    }

    [Theory]
    [InlineData("POST", "/bff/platform-admin/organizations")]
    [InlineData("POST", "/bff/platform-admin/products")]
    [InlineData("PATCH", "/bff/platform-admin/organizations/11111111-1111-1111-1111-111111111111")]
    [InlineData("PUT", "/bff/platform-admin/organizations/11111111-1111-1111-1111-111111111111/members/customer-sub")]
    [InlineData("PUT", "/bff/platform-admin/organizations/11111111-1111-1111-1111-111111111111/products")]
    [InlineData("POST", "/bff/platform-admin/support-elevations")]
    [InlineData("GET", "/bff/platform-admin/support-elevations/effective?organizationId=11111111-1111-1111-1111-111111111111&applicationCode=nexa_connect")]
    [InlineData("GET", "/bff/platform-admin/support-elevations/22222222-2222-2222-2222-222222222222")]
    [InlineData("POST", "/bff/platform-admin/support-elevations/22222222-2222-2222-2222-222222222222/approve")]
    [InlineData("POST", "/bff/platform-admin/support-elevations/22222222-2222-2222-2222-222222222222/revoke")]
    public async Task Mutation_proxies_require_platform_admin_session(string method, string path)
    {
        await using var factory = new PlatformAdminFactory();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new { })
        };

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/bff/platform-admin/login", response.Headers.Location?.AbsolutePath, StringComparison.Ordinal);
    }

    private sealed class PlatformAdminFactory : WebApplicationFactory<PLATFORMADMIN::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Bff:Authority"] = "https://identity.test/realms/test",
                    ["Bff:ClientId"] = "platform-admin-bff",
                    ["Bff:ClientSecret"] = "test-secret",
                    ["Bff:RequireHttpsMetadata"] = "true",
                    ["Services:PlatformDirectory"] = "https://directory.test/"
                }));
        }
    }
}
