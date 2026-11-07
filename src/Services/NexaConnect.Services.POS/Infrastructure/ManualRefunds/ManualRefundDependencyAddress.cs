namespace NexaConnect.Services.POS.Infrastructure.ManualRefunds;

internal static class ManualRefundDependencyAddress
{
    internal static Uri Resolve(IConfiguration configuration, IHostEnvironment environment, string service, string path)
    {
        bool isolated = environment.IsDevelopment() || environment.IsEnvironment("Testing");
        if (!Uri.TryCreate(configuration[$"Services:{service}"], UriKind.Absolute, out var address)
            || address.UserInfo.Length != 0 || address.Query.Length != 0 || address.Fragment.Length != 0
            || (address.Scheme != Uri.UriSchemeHttps && !(isolated && address.Scheme == Uri.UriSchemeHttp && address.IsLoopback)))
            throw new HttpRequestException("Manual refund dependency address is invalid.");
        return new Uri(address.AbsoluteUri.TrimEnd('/') + "/" + path);
    }
}
