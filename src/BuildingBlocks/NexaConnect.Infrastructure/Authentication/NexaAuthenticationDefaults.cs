namespace NexaConnect.Infrastructure.Authentication;

public static class NexaAuthenticationDefaults
{
    public const string ConfigurationSection = "Authentication";
    public const string ApiAudience = "nexaconnect-api";
    public const string RealmRolesClaim = "roles";
    public const string SubjectClaim = "sub";
    public const string UsernameClaim = "preferred_username";
}
