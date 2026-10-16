using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using NexaConnect.Infrastructure.Authentication;

namespace NexaConnect.UnitTests;

public sealed class BffSessionCacheConfigurationTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("Test")]
    public void Local_environment_without_explicit_store_retains_memory_default(string environment)
    {
        var services = new ServiceCollection();
        services.AddNexaConnectBffSessionCache(new ConfigurationBuilder().Build(), new EnvironmentStub(environment));
        using var provider = services.BuildServiceProvider();
        Assert.IsType<MemoryDistributedCache>(provider.GetRequiredService<IDistributedCache>());
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void Explicit_redis_store_is_honored_including_local_restart_verification(string environment)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:BffSessionCache"] = "127.0.0.1:6379,abortConnect=false",
            ["BffSessionCache:InstanceName"] = "draft-test:"
        }).Build();
        var services = new ServiceCollection();
        services.AddNexaConnectBffSessionCache(configuration, new EnvironmentStub(environment));
        using var provider = services.BuildServiceProvider();
        Assert.IsAssignableFrom<RedisCache>(provider.GetRequiredService<IDistributedCache>());
    }

    [Fact]
    public void Production_without_durable_store_still_fails_closed()
    {
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddNexaConnectBffSessionCache(
            new ConfigurationBuilder().Build(), new EnvironmentStub("Production")));
    }

    private sealed class EnvironmentStub(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "cache-configuration-test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
