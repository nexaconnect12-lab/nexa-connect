using System.Security.Claims;

namespace NexaConnect.Infrastructure.Authorization;

public static class ServiceWorkloadPrincipal
{
    public static bool IsClientCredentials(ClaimsPrincipal principal, string clientId) =>
        principal.Identity?.IsAuthenticated == true &&
        principal.FindFirst("azp")?.Value == clientId &&
        principal.FindFirst("preferred_username")?.Value == "service-account-" + clientId;
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
