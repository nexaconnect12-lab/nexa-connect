using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using NexaConnect.Infrastructure.Authentication;

namespace NexaConnect.UnitTests;

public sealed class AuthenticationConfigurationTests
{
    [Fact]
    public void Missing_authentication_section_fails_startup_configuration()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddNexaConnectApiAuthentication(configuration));

        Assert.Contains("Authentication", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Http_authority_is_rejected_when_https_metadata_is_required()
    {
        var configuration = CreateConfiguration(
            authority: "http://identity.example.test/realms/nexa",
            requireHttpsMetadata: true);
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddNexaConnectApiAuthentication(configuration));

        Assert.Contains("HTTPS", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Placeholder_authority_is_rejected()
    {
        var configuration = CreateConfiguration(
            authority: "https://identity.example.invalid/realms/nexa",
            requireHttpsMetadata: true);
        var services = new ServiceCollection();

        services.AddNexaConnectApiAuthentication(configuration);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<NexaAuthenticationOptions>>().Value);

        Assert.Contains("invalid", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Valid_configuration_registers_strict_token_validation_and_fallback_policy()
    {
        var configuration = CreateConfiguration(
            authority: "https://identity.example.test/realms/nexa",
            requireHttpsMetadata: true);
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddNexaConnectApiAuthentication(configuration);

        using var provider = services.BuildServiceProvider();
        var bearerOptions = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var authorizationOptions = provider
            .GetRequiredService<IOptions<AuthorizationOptions>>()
            .Value;

        Assert.Equal("https://identity.example.test/realms/nexa", bearerOptions.Authority);
        Assert.Equal(NexaAuthenticationDefaults.ApiAudience, bearerOptions.Audience);
        Assert.True(bearerOptions.TokenValidationParameters.ValidateIssuer);
        Assert.True(bearerOptions.TokenValidationParameters.ValidateAudience);
        Assert.True(bearerOptions.TokenValidationParameters.ValidateLifetime);
        Assert.True(bearerOptions.TokenValidationParameters.ValidateIssuerSigningKey);
        Assert.True(bearerOptions.TokenValidationParameters.RequireExpirationTime);
        Assert.NotNull(authorizationOptions.FallbackPolicy);
        Assert.Contains(
            authorizationOptions.FallbackPolicy.Requirements,
            requirement => requirement is DenyAnonymousAuthorizationRequirement);
        Assert.True(authorizationOptions.GetPolicy(NexaAuthorizationPolicies.SystemAdministrator) is not null);
        Assert.True(authorizationOptions.GetPolicy(NexaAuthorizationPolicies.ReportViewer) is not null);
    }

    [Fact]
    public void Production_rejects_missing_or_http_only_listener_configuration()
    {
        var environment = new TestHostEnvironment("Production");
        var missing = new ConfigurationBuilder().Build();
        Assert.Throws<InvalidOperationException>(
            () => NexaConnect.Infrastructure.Authentication.AuthenticationServiceCollectionExtensions.EnsureProductionHttps(missing, environment));

        var httpOnly = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ASPNETCORE_URLS"] = "http://*:8080" })
            .Build();
        Assert.Throws<InvalidOperationException>(
            () => NexaConnect.Infrastructure.Authentication.AuthenticationServiceCollectionExtensions.EnsureProductionHttps(httpOnly, environment));
    }

    [Fact]
    public void Production_accepts_https_listener_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ASPNETCORE_URLS"] = "https://*:8443" })
            .Build();

        NexaConnect.Infrastructure.Authentication.AuthenticationServiceCollectionExtensions.EnsureProductionHttps(
            configuration, new TestHostEnvironment("Production"));
    }

    private static IConfiguration CreateConfiguration(string authority, bool requireHttpsMetadata)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Authority"] = authority,
                ["Authentication:Audience"] = NexaAuthenticationDefaults.ApiAudience,
                ["Authentication:RequireHttpsMetadata"] = requireHttpsMetadata.ToString(),
                ["Authentication:ClockSkewSeconds"] = "30"
            })
            .Build();
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "NexaConnect.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
