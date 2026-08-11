namespace NexaConnect.Infrastructure.Authentication;

public static class NexaAuthenticationDefaults
{
    public const string ConfigurationSection = "Authentication";
    public const string ApiAudience = "nexaconnect-api";
    public const string RealmRolesClaim = "roles";
    public const string SubjectClaim = "sub";
    public const string UsernameClaim = "preferred_username";
}

public static class NexaAuthorizationPolicies
{
    public const string SystemAdministrator = "SystemAdministrator";
    public const string PlatformAdministrator = "PlatformAdministrator";
    public const string PlatformSupport = "PlatformSupport";
    public const string PlatformAuditReader = "PlatformAuditReader";
    public const string PlatformUser = "PlatformUser";
    public const string ReportViewer = "ReportViewer";
    public const string PosWorkload = "PosWorkload";
    public const string ServiceWorkload = "ServiceWorkload";
}
