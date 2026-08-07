using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace NexaConnect.Infrastructure.Authentication;

public static class AuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddNexaConnectDevelopmentDataProtection(
        this IServiceCollection services, IHostEnvironment environment, string applicationName)
    {
        if (!environment.IsDevelopment())
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
                NexaAuthorizationPolicies.ReportViewer,
                policy => policy.RequireRole("report-viewer"));
            options.AddPolicy(
                NexaAuthorizationPolicies.PosWorkload,
                policy => policy.RequireClaim("azp", "nexaconnect-pos-service"));
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
