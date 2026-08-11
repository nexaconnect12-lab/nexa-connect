using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;

namespace NexaConnect.Infrastructure.Authentication;

public static class AuthenticationServiceCollectionExtensions
{
    public static void EnsureProductionHttps(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment() || environment.IsEnvironment("Testing") || environment.IsEnvironment("Test"))
        {
            return;
        }

        var addresses = new List<string>();
        string? urls = configuration["ASPNETCORE_URLS"] ?? configuration["DOTNET_URLS"] ?? configuration["urls"];
        if (!string.IsNullOrWhiteSpace(urls))
        {
            addresses.AddRange(urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        addresses.AddRange(configuration.GetSection("Kestrel:Endpoints").GetChildren()
            .Select(endpoint => endpoint["Url"])
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Cast<string>());

        if (addresses.Count == 0 || addresses.Any(address =>
            !Uri.TryCreate(address.Replace("*", "localhost").Replace("+", "localhost"), UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "Production services must expose HTTPS endpoints only. Configure ASPNETCORE_URLS or Kestrel:Endpoints with https:// addresses.");
        }

        string? certificatePath = configuration["Tls:CertificatePath"];
        string? certificatePassword = configuration["Tls:CertificatePassword"];
        ValidateCertificate(certificatePath, certificatePassword, "Tls");

        // Map the NexaConnect deployment contract to Kestrel's native certificate settings.
        // This keeps certificate paths/passwords out of application code while ensuring the
        // validated certificate is also the one Kestrel uses for the HTTPS listener.
        configuration["Kestrel:Certificates:Default:Path"] = certificatePath;
        configuration["Kestrel:Certificates:Default:Password"] = certificatePassword;
    }

    public static IServiceCollection AddNexaConnectDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        string applicationName)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (environment.IsDevelopment() || environment.IsEnvironment("Testing") || environment.IsEnvironment("Test"))
        {
            return services.AddNexaConnectDevelopmentDataProtection(environment, applicationName);
        }

        string keyDirectory = configuration["DataProtection:KeyDirectory"]
            ?? throw new InvalidOperationException("DataProtection:KeyDirectory is required outside Development.");
        if (!Directory.Exists(keyDirectory))
        {
            throw new InvalidOperationException($"DataProtection key directory does not exist: {keyDirectory}");
        }

        string? certificatePath = configuration["DataProtection:CertificatePath"];
        string? certificatePassword = configuration["DataProtection:CertificatePassword"];
        X509Certificate2 certificate = LoadCertificate(certificatePath, certificatePassword, "DataProtection");

        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
            .ProtectKeysWithCertificate(certificate)
            .SetApplicationName(configuration["DataProtection:ApplicationName"] ?? applicationName);
        return services;
    }

    public static IServiceCollection AddNexaConnectDevelopmentDataProtection(
        this IServiceCollection services, IHostEnvironment environment, string applicationName)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing") && !environment.IsEnvironment("Test"))
        {
            return services;
        }

        string keyDirectory = Path.Combine(
            environment.ContentRootPath, ".runstate", "data-protection-keys", applicationName);
        Directory.CreateDirectory(keyDirectory);
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
            .SetApplicationName(applicationName);
        return services;
    }

    private static void ValidateCertificate(string? path, string? password, string section)
    {
        _ = LoadCertificate(path, password, section);
    }

    private static X509Certificate2 LoadCertificate(string? path, string? password, string section)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException($"{section}:CertificatePath is required outside Development.");
        if (!File.Exists(path))
            throw new InvalidOperationException($"{section} certificate file does not exist: {path}");
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException($"{section}:CertificatePassword is required outside Development.");

        try
        {
            var certificate = X509CertificateLoader.LoadPkcs12FromFile(path, password, X509KeyStorageFlags.EphemeralKeySet);
            if (!certificate.HasPrivateKey)
                throw new InvalidOperationException($"{section} certificate must contain a private key: {path}");
            return certificate;
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException($"{section} certificate could not be loaded: {path}", exception);
        }
    }

    public static IServiceCollection AddNexaConnectApiAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var settings = configuration
            .GetRequiredSection(NexaAuthenticationDefaults.ConfigurationSection)
            .Get<NexaAuthenticationOptions>()
            ?? throw new InvalidOperationException("Authentication configuration is required.");

        Validate(settings);

        services
            .AddOptions<NexaAuthenticationOptions>()
            .Bind(configuration.GetRequiredSection(NexaAuthenticationDefaults.ConfigurationSection))
            .Validate(
                options => Uri.TryCreate(options.Authority, UriKind.Absolute, out var configuredAuthority)
                    && !configuredAuthority.Host.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase),
                "Authentication:Authority still contains an invalid deployment value.")
            .ValidateOnStart();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = settings.Authority;
                options.Audience = settings.Audience;
                options.RequireHttpsMetadata = settings.RequireHttpsMetadata;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidAudience = settings.Audience,
                    ValidAudiences = new[] { settings.Audience },
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ValidateIssuerSigningKey = true,
                    NameClaimType = NexaAuthenticationDefaults.UsernameClaim,
                    RoleClaimType = NexaAuthenticationDefaults.RealmRolesClaim,
                    ClockSkew = TimeSpan.FromSeconds(settings.ClockSkewSeconds)
                };
                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("NexaConnect.JwtBearer");
                        logger.LogError(context.Exception, "JWT authentication failed for {Method} {Path}", context.Request.Method, context.Request.Path);
                        return Task.CompletedTask;
                    },
                    OnChallenge = context =>
                    {
                        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("NexaConnect.JwtBearer");
                        logger.LogWarning("JWT challenge for {Method} {Path}: {Error} {Description}", context.Request.Method, context.Request.Path, context.Error, context.ErrorDescription);
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
            options.AddPolicy(
                NexaAuthorizationPolicies.SystemAdministrator,
                policy => policy.RequireRole("system-admin"));
            options.AddPolicy(
                NexaAuthorizationPolicies.PlatformAdministrator,
                policy => policy.RequireRole("platform-owner", "platform-admin"));
            options.AddPolicy(
                NexaAuthorizationPolicies.PlatformSupport,
                policy => policy.RequireRole("platform-owner", "platform-admin", "platform-support"));
            options.AddPolicy(
                NexaAuthorizationPolicies.PlatformAuditReader,
                policy => policy.RequireRole("platform-owner", "platform-admin", "platform-auditor"));
            options.AddPolicy(
                NexaAuthorizationPolicies.PlatformUser,
                policy => policy.RequireRole("platform-owner", "platform-admin", "platform-support", "platform-auditor"));
            options.AddPolicy(
                NexaAuthorizationPolicies.ReportViewer,
                policy => policy.RequireRole("report-viewer"));
            options.AddPolicy(
                NexaAuthorizationPolicies.PosWorkload,
                policy => policy.RequireClaim("azp", "nexaconnect-pos-service"));
            options.AddPolicy(
                NexaAuthorizationPolicies.ServiceWorkload,
                policy => policy.RequireClaim("azp", "nexaconnect-pos-service", "nexaconnect-catalog-service",
                    "nexaconnect-order-service", "nexaconnect-inventory-service", "nexaconnect-payment-service"));
        });

        return services;
    }

    private static void Validate(NexaAuthenticationOptions settings)
    {
        if (!Uri.TryCreate(settings.Authority, UriKind.Absolute, out var authority))
        {
            throw new InvalidOperationException("Authentication:Authority must be an absolute URI.");
        }

        if (settings.RequireHttpsMetadata && authority.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "Authentication:Authority must use HTTPS when RequireHttpsMetadata is true.");
        }

        if (string.IsNullOrWhiteSpace(settings.Audience))
        {
            throw new InvalidOperationException("Authentication:Audience is required.");
        }

        if (settings.ClockSkewSeconds is < 0 or > 300)
        {
            throw new InvalidOperationException(
                "Authentication:ClockSkewSeconds must be between 0 and 300 seconds.");
        }
    }
}
