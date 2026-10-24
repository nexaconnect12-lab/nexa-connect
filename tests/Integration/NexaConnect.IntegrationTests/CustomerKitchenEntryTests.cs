extern alias CUSTOMERBFF;

using System.Net;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace NexaConnect.IntegrationTests;

public sealed class CustomerKitchenEntryTests
{
    [Theory]
    [InlineData("/kitchen")]
    [InlineData("/kitchen/")]
    public async Task Published_spa_fallback_serves_direct_kitchen_entry(string path)
    {
        await using var factory = new EntryFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Synthetic Customer SPA", await response.Content.ReadAsStringAsync());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        // An anonymous SPA shell grants no access to tickets.
        using var queue = await client.GetAsync("/bff/customer/kitchen/branches/11111111-1111-1111-1111-111111111111/tickets");
        Assert.Equal(HttpStatusCode.Unauthorized, queue.StatusCode);
        Assert.Null(queue.Headers.Location);
    }

    [Theory]
    [InlineData("/kitchen", "/kitchen")]
    [InlineData("/kitchen?display=wall#queue", "/kitchen?display=wall#queue")]
    [InlineData("https://external.test/kitchen", "/")]
    [InlineData("//external.test/kitchen", "/")]
    public async Task Customer_login_protects_local_destination_in_oidc_state(string destination, string expected)
    {
        await using var factory = new EntryFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        using var response = await client.GetAsync($"/bff/customer/login?returnUrl={Uri.EscapeDataString(destination)}");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var redirect = response.Headers.Location!;
        Assert.Equal("identity.test", redirect.Host);
        var state = QueryHelpers.ParseQuery(redirect.Query)["state"].ToString();
        var options = factory.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get("CustomerOidc");
        var properties = options.StateDataFormat.Unprotect(state);
        Assert.NotNull(properties);
        Assert.Equal(expected, properties.RedirectUri);
    }

    private sealed class EntryFactory : WebApplicationFactory<CUSTOMERBFF::Program>
    {
        private readonly string webRoot = Path.Combine(Path.GetTempPath(), $"nexa-kitchen-entry-{Guid.NewGuid():N}");
        public EntryFactory()
        {
            Directory.CreateDirectory(webRoot);
            File.WriteAllText(Path.Combine(webRoot, "index.html"), "<!doctype html><title>Synthetic Customer SPA</title>");
        }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseWebRoot(webRoot);
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bff:Authority"] = "https://identity.test/realms/test",
                ["Bff:ClientId"] = "nexaconnect-web-bff",
                ["Bff:ClientSecret"] = "synthetic-test-only",
                ["Bff:RequireHttpsMetadata"] = "true"
            }));
            builder.ConfigureServices(services => services.PostConfigure<OpenIdConnectOptions>("CustomerOidc", options =>
            {
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(new()
                {
                    Issuer = "https://identity.test/realms/test",
                    AuthorizationEndpoint = "https://identity.test/realms/test/protocol/openid-connect/auth"
                });
            }));
        }
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            // Delete only this factory's exact generated temporary webroot.
            Directory.Delete(webRoot, recursive: true);
        }
    }
}
