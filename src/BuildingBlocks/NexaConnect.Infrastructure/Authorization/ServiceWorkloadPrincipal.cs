using System.Security.Claims;

namespace NexaConnect.Infrastructure.Authorization;

public static class ServiceWorkloadPrincipal
{
    private static readonly HashSet<string> AllowedClients = new(StringComparer.Ordinal)
    {
        "nexaconnect-pos-service",
        "nexaconnect-catalog-service",
        "nexaconnect-order-service",
        "nexaconnect-inventory-service",
        "nexaconnect-payment-service"
    };

    public static bool IsTrusted(ClaimsPrincipal principal) =>
        principal.FindFirst("azp")?.Value is { } clientId && AllowedClients.Contains(clientId);
}
