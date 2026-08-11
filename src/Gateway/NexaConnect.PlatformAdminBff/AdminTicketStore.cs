using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Distributed;

namespace NexaConnect.PlatformAdminBff;

public sealed class AdminTicketStore(IDistributedCache cache) : ITicketStore
{
    private static readonly TimeSpan TicketLifetime = TimeSpan.FromHours(8);

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        string key = $"nexa:platform-admin:{Guid.NewGuid():N}";
        await RenewAsync(key, ticket);
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket) => cache.SetAsync(key,
        TicketSerializer.Default.Serialize(ticket),
        new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TicketLifetime });

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        byte[]? value = await cache.GetAsync(key);
        return value is null ? null : TicketSerializer.Default.Deserialize(value);
    }

    public Task RemoveAsync(string key) => cache.RemoveAsync(key);
}
