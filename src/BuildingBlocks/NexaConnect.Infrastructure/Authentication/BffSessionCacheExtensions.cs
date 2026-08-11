using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NexaConnect.Infrastructure.Authentication;

public static class BffSessionCacheExtensions
{
    public static IServiceCollection AddNexaConnectBffSessionCache(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (environment.IsDevelopment() || environment.IsEnvironment("Testing") || environment.IsEnvironment("Test"))
            return services.AddDistributedMemoryCache();

        string? connectionString = configuration.GetConnectionString("BffSessionCache");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:BffSessionCache is required outside Development.");
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = connectionString;
            options.InstanceName = configuration["BffSessionCache:InstanceName"] ?? "nexa:bff:";
        });
        return services;
    }
}
