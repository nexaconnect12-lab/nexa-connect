using System.Text.Json;
using System.IO;

namespace NexaConnect.POS;

public sealed record PosClientConfiguration(
    string Authority,
    string ClientId,
    string RedirectUri,
    string Scopes,
    string PosApi,
    Guid BranchId,
    Guid StoreId,
    Guid TerminalId)
{
    public static PosClientConfiguration Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;
        JsonElement identity = root.GetProperty("Identity");
        JsonElement services = root.GetProperty("Services");
        return new PosClientConfiguration(
            identity.GetProperty("Authority").GetString() ?? throw new InvalidDataException("Identity:Authority is required."),
            identity.GetProperty("ClientId").GetString() ?? throw new InvalidDataException("Identity:ClientId is required."),
            identity.GetProperty("RedirectUri").GetString() ?? throw new InvalidDataException("Identity:RedirectUri is required."),
            identity.GetProperty("Scopes").GetString() ?? "openid profile email nexaconnect-api",
            services.GetProperty("PosApi").GetString() ?? throw new InvalidDataException("Services:PosApi is required."),
            ParseGuid(root, "Pos", "BranchId"),
            ParseGuid(root, "Pos", "StoreId"),
            ParseGuid(root, "Pos", "TerminalId"));
    }

    private static Guid ParseGuid(JsonElement root, string section, string name) =>
        Guid.TryParse(root.GetProperty(section).GetProperty(name).GetString(), out Guid value)
            ? value
            : Guid.Empty;
}
