using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using NexaConnect.Contracts.Platform;

namespace NexaConnect.CustomerBff;

public sealed class TenantSelectionCookie(IDataProtectionProvider protectionProvider)
{
    private readonly IDataProtector protector = protectionProvider.CreateProtector("nexaconnect.customer-bff.tenant-selection.v1");

    public string Protect(TenantContext context) => protector.Protect(JsonSerializer.Serialize(context));

    public TenantContext? Unprotect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            return JsonSerializer.Deserialize<TenantContext>(protector.Unprotect(value));
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            return null;
        }
    }
}
