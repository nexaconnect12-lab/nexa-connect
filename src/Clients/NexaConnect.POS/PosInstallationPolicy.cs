using System.IO;
using System.Text.Json;

namespace NexaConnect.POS;

// Installer preflight only. No operational writes, token installation or schema migration.
internal static class PosInstallationPolicy
{
    internal const string MaintenanceMutex = "NexaConnect.POS.Maintenance";
    internal static void AssertManagedDirectory(string directory)
    {
        string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexaConnect", "Terminal");
        if (!Path.GetFullPath(directory).Equals(Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unexpected installation directory.");
        AssertNoLinks(directory);
    }
    internal static void AssertNoLinks(string path)
    {
        for (string? p = Path.GetFullPath(path); p is not null; p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Installer paths cannot traverse links or junctions.");
    }
    internal static PosClientConfiguration ReadProfile(string path)
    {
        var info = new FileInfo(path);
        if (info.Length is <= 0 or > 65536) throw new InvalidDataException("Invalid terminal profile size.");
        using var json = JsonDocument.Parse(File.ReadAllBytes(path));
        var allowed = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Identity"] = ["Authority", "ClientId", "RedirectUri", "Scopes"],
            ["Services"] = ["PosApi", "OrderApi", "CatalogApi", "PaymentApi", "MediaApi"],
            ["Session"] = ["IdleTimeoutMinutes", "AbsoluteTimeoutHours", "TokenRefreshLeadSeconds"],
            ["Pos"] = ["OrganizationId", "RestaurantId", "BranchId", "StoreId", "TerminalId", "Currency", "PaymentMethod", "EnableOmiseTestCheckout", "PromptPayQrImagePath"]
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in json.RootElement.EnumerateObject())
        {
            if (!seen.Add(section.Name) || !allowed.TryGetValue(section.Name, out var keys) || section.Value.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Unknown or duplicate terminal profile section.");
            var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in section.Value.EnumerateObject())
                if (!fields.Add(field.Name) || !keys.Contains(field.Name, StringComparer.Ordinal) || field.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    throw new InvalidDataException("Unknown or duplicate terminal profile setting.");
        }
        var c = PosClientConfiguration.Load(path);
        ValidateHosted(c);
        return c;
    }

    internal static void ValidateHosted(PosClientConfiguration c)
    {
        c.ValidateCheckout();
        if (c.UseIsolatedLocalPilotStorage)
            throw new InvalidDataException("Hosted installations cannot select local pilot storage.");
        foreach (var endpoint in new[] { c.Authority, c.PosApi, c.OrderApi, c.CatalogApi, c.PaymentApi, c.MediaApi }.Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            var uri = new Uri(endpoint);
            if (uri.Scheme != "https" || uri.IsLoopback || uri.Host.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Hosted terminal profiles require trusted non-loopback HTTPS endpoints.");
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(new Uri(c.Authority).AbsolutePath, "^/realms/[a-zA-Z0-9_-]+$"))
            throw new InvalidDataException("Configure the exact hosted realm issuer.");
        if (c.EnableOmiseTestCheckout || c.PaymentMethod is not ("cash_manual" or "promptpay_manual"))
            throw new InvalidDataException("This installer supports manual tender only.");
        if (!c.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal)
            .SetEquals(new[] { "openid", "profile", "email", "nexaconnect-api" }))
            throw new InvalidDataException("The terminal uses the existing public-client scopes only.");
        if (!string.IsNullOrEmpty(c.PromptPayQrImagePath) && !Path.IsPathFullyQualified(c.PromptPayQrImagePath))
            throw new InvalidDataException("Use an absolute managed public QR image path or leave it blank.");
    }

    internal static void AssertSameCustody(PosClientConfiguration previous, PosClientConfiguration next)
    {
        // Endpoint relocation is supported, identity realm/terminal/tender transfer is not.
        if (previous.Authority != next.Authority || previous.ClientId != next.ClientId || previous.RedirectUri != next.RedirectUri
            || previous.OrganizationId != next.OrganizationId || previous.RestaurantId != next.RestaurantId
            || previous.BranchId != next.BranchId || previous.StoreId != next.StoreId || previous.TerminalId != next.TerminalId
            || previous.Currency != next.Currency || previous.PaymentMethod != next.PaymentMethod || previous.EnableOmiseTestCheckout != next.EnableOmiseTestCheckout
            || previous.UseIsolatedLocalPilotStorage != next.UseIsolatedLocalPilotStorage)
            throw new InvalidDataException("Update changes terminal custody. Preserve recovery and reconcile with the original installation.");
    }

    internal static void Preflight(string proposedPath, string? previousPath, string? recoveryDirectory = null)
    {
        var proposed = ReadProfile(proposedPath);
        string recovery = recoveryDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexaConnect", "POS");
        if (previousPath is not null) AssertSameCustody(PosClientConfiguration.Load(previousPath), proposed);
        else if (Directory.Exists(recovery) && Directory.EnumerateFileSystemEntries(recovery).Any())
            throw new InvalidDataException("Retained state requires the original terminal configuration for migration review.");
        LocalPosDatabase.ValidateExistingScope(proposed, recovery);
        if (File.Exists(Path.Combine(recovery, "tokens.bin")))
        {
            var marker = new WindowsTokenStore(recovery).Load();
            if (marker is null || string.IsNullOrWhiteSpace(marker.Subject) || marker.Authority != proposed.Authority)
                throw new InvalidDataException("Retained authentication binding requires the original terminal and cashier.");
        }
        else if (LocalPosDatabase.HasRetainedWorkForInstallation(recovery))
            throw new InvalidDataException("Pending work without its original cashier binding cannot be upgraded safely.");
    }
}
