using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace NexaConnect.Infrastructure.Authentication;

public static class AuthenticationServiceCollectionExtensions
{
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
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ValidateIssuerSigningKey = true,
                    NameClaimType = NexaAuthenticationDefaults.UsernameClaim,
                    RoleClaimType = NexaAuthenticationDefaults.RealmRolesClaim,
                    ClockSkew = TimeSpan.FromSeconds(settings.ClockSkewSeconds)
                };
            });

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
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
