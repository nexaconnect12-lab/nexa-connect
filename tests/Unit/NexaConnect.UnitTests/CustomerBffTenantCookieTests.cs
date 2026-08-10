using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using NexaConnect.Contracts.Platform;
using NexaConnect.CustomerBff;

namespace NexaConnect.UnitTests;

public sealed class CustomerBffTenantCookieTests
{
    [Fact]
    public void Tenant_cookie_round_trips_and_rejects_tampering()
    {
        string keyDirectory = Path.Combine(Path.GetTempPath(), $"customer-bff-keys-{Guid.NewGuid():N}");
        Directory.CreateDirectory(keyDirectory);
        using ServiceProvider services = new ServiceCollection()
            .AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
            .SetApplicationName("customer-bff-tests")
            .Services.BuildServiceProvider();
        IDataProtectionProvider provider = services.GetRequiredService<IDataProtectionProvider>();
        var cookie = new TenantSelectionCookie(provider);
        var tenant = new TenantContext("subject-1", Guid.NewGuid(), "nexa_connect");

        string protectedValue = cookie.Protect(tenant);

        TenantContext? restored = cookie.Unprotect(protectedValue);
        Assert.Equal(tenant, restored);
        Assert.Null(cookie.Unprotect(protectedValue + "tampered"));
        Assert.Null(cookie.Unprotect(null));
        Directory.Delete(keyDirectory, recursive: true);
    }
}
