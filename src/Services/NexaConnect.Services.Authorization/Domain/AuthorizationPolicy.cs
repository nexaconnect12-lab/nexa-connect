namespace NexaConnect.Services.Authorization.Domain;

public sealed record AuthorizationEvidence(string? MostSpecificOverride, bool RoleGranted, decimal? Limit);

public static class AuthorizationPolicy
{
    // An explicit override at the most specific active matching scope replaces role grants.
    // Unknown persisted effects fail closed. Limits never turn a denied permission into a grant.
    public static bool Grants(AuthorizationEvidence evidence, decimal? amount)
    {
        bool permission = evidence.MostSpecificOverride switch
        {
            "deny" => false,
            "allow" => true,
            null => evidence.RoleGranted,
            _ => false
        };
        return permission && (amount is null || (amount >= 0 && evidence.Limit is not null && amount <= evidence.Limit));
    }
}
