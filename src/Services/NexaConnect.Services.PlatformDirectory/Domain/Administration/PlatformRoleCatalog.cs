namespace NexaConnect.Services.PlatformDirectory.Domain.Administration;

public static class PlatformRoleCatalog
{
    public static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> PermissionsByRole =
        new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal)
        {
            ["platform-owner"] = ["platform.users.manage", "platform.roles.manage", "platform.directory.manage", "platform.audit.read", "platform.summary.read", "platform.support.manage"],
            ["platform-admin"] = ["platform.users.manage", "platform.roles.manage", "platform.directory.manage", "platform.audit.read", "platform.summary.read", "platform.support.manage"],
            ["platform-support"] = ["platform.summary.read", "platform.support.request"],
            ["platform-auditor"] = ["platform.audit.read", "platform.summary.read"]
        };

    public static IReadOnlyCollection<string> NormalizeAndValidate(IEnumerable<string>? roles)
    {
        string[] normalized = (roles ?? []).Select(role => role?.Trim() ?? string.Empty)
            .Where(role => role.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (normalized.Any(role => !PermissionsByRole.ContainsKey(role)))
            throw new ArgumentException("One or more platform roles are invalid.");
        return normalized;
    }
}
