using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using NexaConnect.Infrastructure.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

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
        AuthorizationPolicy platformAdmin = Assert.IsType<AuthorizationPolicy>(
            authorizationOptions.GetPolicy(NexaAuthorizationPolicies.PlatformAdministrator));
        RolesAuthorizationRequirement platformRoles = Assert.Single(
            platformAdmin.Requirements.OfType<RolesAuthorizationRequirement>());
        Assert.Equal(["platform-owner", "platform-admin"], platformRoles.AllowedRoles);
        Assert.NotNull(authorizationOptions.GetPolicy(NexaAuthorizationPolicies.PlatformSupport));
        Assert.NotNull(authorizationOptions.GetPolicy(NexaAuthorizationPolicies.PlatformAuditReader));
        Assert.NotNull(authorizationOptions.GetPolicy(NexaAuthorizationPolicies.PlatformUser));
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
        string certificatePath = Path.Combine(Path.GetTempPath(), $"nexaconnect-{Guid.NewGuid():N}.pfx");
        using (RSA key = RSA.Create(2048))
        {
            var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Pfx, "test-password"));
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_URLS"] = "https://*:8443",
                ["Tls:CertificatePath"] = certificatePath,
                ["Tls:CertificatePassword"] = "test-password"
            })
            .Build();

        try
        {
            NexaConnect.Infrastructure.Authentication.AuthenticationServiceCollectionExtensions.EnsureProductionHttps(
                configuration, new TestHostEnvironment("Production"));
            Assert.Equal(certificatePath, configuration["Kestrel:Certificates:Default:Path"]);
            Assert.Equal("test-password", configuration["Kestrel:Certificates:Default:Password"]);
        }
        finally
        {
            File.Delete(certificatePath);
        }
    }

    [Fact]
    public void Production_data_protection_requires_a_persistent_key_store_and_certificate()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        Assert.Throws<InvalidOperationException>(() => services.AddNexaConnectDataProtection(
            configuration, new TestHostEnvironment("Production"), "test"));
    }

    [Fact]
    public void Production_data_protection_uses_the_configured_store_and_certificate()
    {
        string keyDirectory = Path.Combine(Path.GetTempPath(), $"nexaconnect-keys-{Guid.NewGuid():N}");
        string certificatePath = Path.Combine(Path.GetTempPath(), $"nexaconnect-dp-{Guid.NewGuid():N}.pfx");
        Directory.CreateDirectory(keyDirectory);
        using (RSA key = RSA.Create(2048))
        {
            var request = new CertificateRequest("CN=nexaconnect-data-protection", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Pfx, "test-password"));
        }

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DataProtection:KeyDirectory"] = keyDirectory,
                    ["DataProtection:CertificatePath"] = certificatePath,
                    ["DataProtection:CertificatePassword"] = "test-password",
                    ["DataProtection:ApplicationName"] = "test-service"
                })
                .Build();
            var services = new ServiceCollection();

            services.AddNexaConnectDataProtection(configuration, new TestHostEnvironment("Production"), "test");
            using var provider = services.BuildServiceProvider();
            IDataProtector protector = provider.GetRequiredService<IDataProtectionProvider>().CreateProtector("test");
            Assert.Equal("payload", protector.Unprotect(protector.Protect("payload")));
        }
        finally
        {
            Directory.Delete(keyDirectory, recursive: true);
            File.Delete(certificatePath);
        }
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
