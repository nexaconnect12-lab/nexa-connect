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
    string CatalogApi = "")
{
    public void ValidateCheckout()
    {
        foreach (var (name, value) in new[] { ("Authority", Authority), ("PosApi", PosApi), ("OrderApi", OrderApi), ("CatalogApi", CatalogApi) })
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new InvalidDataException($"{name} requires an HTTPS URL (HTTP is allowed only on loopback).");
        if (new[] { OrganizationId, RestaurantId, BranchId, StoreId, TerminalId }.Any(id => id == Guid.Empty))
            throw new InvalidDataException("Configure valid organization, restaurant, branch, store and terminal identifiers.");
        if (Currency != "THB" || PaymentMethod is not ("cash_manual" or "promptpay_manual"))
            throw new InvalidDataException("Cashier checkout requires THB and cash_manual or promptpay_manual.");
    }

    public static PosClientConfiguration Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;
        JsonElement identity = root.GetProperty("Identity");
        JsonElement services = root.GetProperty("Services");
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
            services.TryGetProperty("CatalogApi", out var catalog) ? catalog.GetString() ?? "" : "");
        configuration.ValidateCheckout();
        return configuration;
    }

    private static Guid ParseGuid(JsonElement root, string section, string name) =>
        Guid.TryParse(root.GetProperty(section).GetProperty(name).GetString(), out Guid value)
            ? value
            : Guid.Empty;
}
