using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using NexaConnect.Gateway;

namespace NexaConnect.IntegrationTests;

public sealed class GatewayAuthenticationTests : IClassFixture<GatewayAuthenticationFactory>
{
    private readonly GatewayAuthenticationFactory _factory;

    public GatewayAuthenticationTests(GatewayAuthenticationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("https://evil.example/", "/")]
    [InlineData("//evil.example/", "/")]
    [InlineData("/bff/pos", "/bff/pos")]
    public void Bff_return_url_is_local_only(string? candidate, string expected)
    {
        Assert.Equal(expected, BffReturnUrl.Normalize(candidate));
    }

    [Fact]
    public async Task Protected_endpoint_rejects_missing_token()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/identity/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unannotated_endpoint_is_protected_by_fallback_policy()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/weatherforecast");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Protected_endpoint_accepts_valid_token_and_returns_stable_subject()
    {
        using var client = CreateAuthenticatedClient(_factory.CreateToken());

        var response = await client.GetAsync("/api/identity/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(GatewayAuthenticationFactory.Subject, document.RootElement.GetProperty("subject").GetString());
    }

    [Fact]
    public async Task Protected_endpoint_rejects_expired_token()
    {
        var now = DateTime.UtcNow;
        using var client = CreateAuthenticatedClient(
            _factory.CreateToken(notBefore: now.AddMinutes(-10), expires: now.AddMinutes(-5)));

        var response = await client.GetAsync("/api/identity/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Protected_endpoint_rejects_wrong_audience()
    {
        using var client = CreateAuthenticatedClient(
            _factory.CreateToken(audience: "another-api"));

        var response = await client.GetAsync("/api/identity/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Role_protected_endpoint_rejects_insufficient_permission()
    {
        using var client = CreateAuthenticatedClient(_factory.CreateToken(roles: ["cashier"]));

        var response = await client.GetAsync("/api/identity/report-access");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Role_protected_endpoint_accepts_required_role()
    {
        using var client = CreateAuthenticatedClient(_factory.CreateToken(roles: ["report-viewer"]));

        var response = await client.GetAsync("/api/identity/report-access");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private HttpClient CreateAuthenticatedClient(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

public sealed class GatewayAuthenticationFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "https://identity.tests/realms/nexa-test";
    public const string Audience = "nexaconnect-api";
    public const string Subject = "550e8400-e29b-41d4-a716-446655440000";

    private readonly RSA _signingKey = RSA.Create(2048);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Authority"] = Issuer,
                ["Authentication:Audience"] = Audience,
                ["Authentication:RequireHttpsMetadata"] = "true",
                ["Authentication:ClockSkewSeconds"] = "0"
            });
        });
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                configuration.SigningKeys.Add(new RsaSecurityKey(_signingKey));

                options.ConfigurationManager =
                    new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                options.RequireHttpsMetadata = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = Issuer,
                    ValidateAudience = true,
                    ValidAudience = Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new RsaSecurityKey(_signingKey),
                    NameClaimType = "preferred_username",
                    RoleClaimType = "roles",
                    ClockSkew = TimeSpan.Zero
                };
            });
        });
    }

    public string CreateToken(
        string audience = Audience,
        DateTime? notBefore = null,
        DateTime? expires = null,
        IReadOnlyCollection<string>? roles = null)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new("sub", Subject),
            new("preferred_username", "integration-test-user")
        };

        claims.AddRange((roles ?? []).Select(role => new Claim("roles", role)));

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: audience,
            claims: claims,
            notBefore: notBefore ?? now.AddMinutes(-1),
            expires: expires ?? now.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new RsaSecurityKey(_signingKey),
                SecurityAlgorithms.RsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _signingKey.Dispose();
        }
    }
}
