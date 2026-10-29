using System.Text.Json;
using System.IO;

namespace NexaConnect.POS;

public sealed record PosClientConfiguration(
    string Authority,
    string ClientId,
    string RedirectUri,
    string Scopes,
    string PosApi,
    string OrderApi,
    Guid RestaurantId,
    Guid OrganizationId,
    string Currency,
    string PaymentMethod,
    string? PromptPayQrImagePath,
    Guid BranchId,
    Guid StoreId,
    Guid TerminalId,
    string CatalogApi = "",
    int SessionIdleTimeoutMinutes = 5,
    int SessionAbsoluteTimeoutHours = 10,
    int TokenRefreshLeadSeconds = 60,
    bool EnableOmiseTestCheckout = false,
    string PaymentApi = "",
    string MediaApi = "",
    bool UseIsolatedLocalPilotStorage = false)
{
    public void ValidateCheckout()
    {
        if (ClientId != "nexaconnect-pos" || RedirectUri != "nexaconnect-pos://oauth/callback")
            throw new InvalidDataException("The POS public client and registered callback must match the installed protocol.");
        foreach (var (name, value) in new[] { ("Authority", Authority), ("PosApi", PosApi), ("OrderApi", OrderApi), ("CatalogApi", CatalogApi) }
            .Concat(string.IsNullOrWhiteSpace(PaymentApi) ? [] : [("PaymentApi", PaymentApi)])
            .Concat(string.IsNullOrWhiteSpace(MediaApi) ? [] : [("MediaApi", MediaApi)]))
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new InvalidDataException($"{name} requires an HTTPS URL (HTTP is allowed only on loopback).");
        if (new[] { OrganizationId, RestaurantId, BranchId, StoreId, TerminalId }.Any(id => id == Guid.Empty))
            throw new InvalidDataException("Configure valid organization, restaurant, branch, store and terminal identifiers.");
        if (Currency != "THB" || PaymentMethod is not ("cash_manual" or "promptpay_manual" or "card_omise_test"))
            throw new InvalidDataException("Cashier checkout requires a supported THB payment method.");
        if (PaymentMethod == "card_omise_test" && !EnableOmiseTestCheckout
            || EnableOmiseTestCheckout && new[] { Authority, PosApi, OrderApi, CatalogApi, PaymentApi }
                .Where(value=>!string.IsNullOrWhiteSpace(value)).Any(value => !new Uri(value).IsLoopback))
            throw new InvalidDataException("Omise test checkout requires explicit enablement and loopback service APIs.");
        if (SessionIdleTimeoutMinutes is < 1 or > 60)
            throw new InvalidDataException("Session:IdleTimeoutMinutes must be between 1 and 60.");
        if (SessionAbsoluteTimeoutHours is < 1 or > 24)
            throw new InvalidDataException("Session:AbsoluteTimeoutHours must be between 1 and 24.");
        if (TokenRefreshLeadSeconds is < 15 or > 240)
            throw new InvalidDataException("Session:TokenRefreshLeadSeconds must be between 15 and 240.");
        if (UseIsolatedLocalPilotStorage && (PaymentMethod is not ("cash_manual" or "promptpay_manual")
            || !System.Text.RegularExpressions.Regex.IsMatch(new Uri(Authority).AbsolutePath,
                "\\A/realms/nexa-review-it-[a-f0-9]{32}\\z")
            || new[] { Authority, PosApi, OrderApi, CatalogApi, PaymentApi, MediaApi }
                .Where(value => !string.IsNullOrWhiteSpace(value)).Any(value => !new Uri(value).IsLoopback)))
            throw new InvalidDataException("Isolated pilot storage requires an owned loopback draft realm and manual tender.");
    }

    public bool AllowsCheckoutMethod(string method) => method == PaymentMethod
        || EnableOmiseTestCheckout && (PaymentMethod is "cash_manual" or "promptpay_manual") && method == "card_omise_test";

    internal string GetStorageDirectory(string? localApplicationData = null)
    {
        ValidateCheckout();
        string root = localApplicationData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string path = UseIsolatedLocalPilotStorage
            ? Path.Combine(root, "NexaConnect", "POS-Pilots", Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                    $"{OrganizationId:N}|{BranchId:N}|{StoreId:N}|{TerminalId:N}"))))
            : Path.Combine(root, "NexaConnect", "POS");
        // A profile may never redirect protected state through a junction or symlink.
        for (string? cursor = Path.GetFullPath(path); cursor is not null; cursor = Path.GetDirectoryName(cursor))
            if ((File.Exists(cursor) || Directory.Exists(cursor))
                && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("POS storage paths cannot traverse links or junctions.");
        return path;
    }

    public static PosClientConfiguration Load(string? configurationPath = null)
    {
        string path = configurationPath ?? Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;
        JsonElement identity = root.GetProperty("Identity");
        JsonElement services = root.GetProperty("Services");
        JsonElement session = root.TryGetProperty("Session", out JsonElement configuredSession)
            ? configuredSession
            : default;
        var configuration = new PosClientConfiguration(
            identity.GetProperty("Authority").GetString() ?? throw new InvalidDataException("Identity:Authority is required."),
            identity.GetProperty("ClientId").GetString() ?? throw new InvalidDataException("Identity:ClientId is required."),
            identity.GetProperty("RedirectUri").GetString() ?? throw new InvalidDataException("Identity:RedirectUri is required."),
            identity.GetProperty("Scopes").GetString() ?? "openid profile email nexaconnect-api",
            services.GetProperty("PosApi").GetString() ?? throw new InvalidDataException("Services:PosApi is required."),
            services.GetProperty("OrderApi").GetString() ?? throw new InvalidDataException("Services:OrderApi is required."),
            ParseGuid(root, "Pos", "RestaurantId"),
            ParseGuid(root, "Pos", "OrganizationId"),
            root.GetProperty("Pos").GetProperty("Currency").GetString() ?? "USD",
            root.GetProperty("Pos").GetProperty("PaymentMethod").GetString() ?? "cash",
            root.GetProperty("Pos").TryGetProperty("PromptPayQrImagePath", out JsonElement qrPath)
                ? qrPath.GetString()
                : null,
            ParseGuid(root, "Pos", "BranchId"),
            ParseGuid(root, "Pos", "StoreId"),
            ParseGuid(root, "Pos", "TerminalId"),
            services.TryGetProperty("CatalogApi", out var catalog) ? catalog.GetString() ?? "" : "",
            GetInt32(session, "IdleTimeoutMinutes", 5),
            GetInt32(session, "AbsoluteTimeoutHours", 10),
            GetInt32(session, "TokenRefreshLeadSeconds", 60),
            root.GetProperty("Pos").TryGetProperty("EnableOmiseTestCheckout", out var cardEnabled) && cardEnabled.GetBoolean(),
            services.TryGetProperty("PaymentApi", out var payment) ? payment.GetString() ?? "" : "",
            services.TryGetProperty("MediaApi", out var media) ? media.GetString() ?? "" : "",
            root.GetProperty("Pos").TryGetProperty("UseIsolatedLocalPilotStorage", out var isolated) && isolated.GetBoolean());
        configuration.ValidateCheckout();
        return configuration;
    }

    private static Guid ParseGuid(JsonElement root, string section, string name) =>
        Guid.TryParse(root.GetProperty(section).GetProperty(name).GetString(), out Guid value)
            ? value
            : Guid.Empty;

    private static int GetInt32(JsonElement section, string name, int fallback) =>
        section.ValueKind == JsonValueKind.Object && section.TryGetProperty(name, out JsonElement value)
            ? value.GetInt32()
            : fallback;
}
