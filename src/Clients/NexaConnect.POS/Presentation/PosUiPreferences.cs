using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NexaConnect.POS;

// Non-secret presentation preference; never an authorization or financial setting.
public sealed class PosUiPreferences(string directory)
{
    private readonly string inputPath = Path.Combine(Path.GetFullPath(directory), "input.json");
    private readonly string displayPath = Path.Combine(Path.GetFullPath(directory), "displays.json");
    private sealed record Settings(int Version, bool TouchFirst, string? Density = null);
    private sealed record DisplaySettings(int Version, PosDisplayRoles Roles);

    public static PosUiPreferences ForTerminal(PosClientConfiguration configuration, string? localApplicationData = null)
    {
        var key = $"{configuration.OrganizationId:N}|{configuration.RestaurantId:N}|{configuration.BranchId:N}|{configuration.StoreId:N}|{configuration.TerminalId:N}";
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return new(Path.Combine(localApplicationData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexaConnect", "Preferences", hash));
    }

    public bool LoadTouchFirst() => Load().TouchFirst;

    public PosUiOptions Load()
    {
        var settings = Read<Settings>(inputPath);
        return settings is { Version: 1 } ? new(settings.TouchFirst,
            settings.Density == nameof(PosDisplayDensity.Touch) ? PosDisplayDensity.Touch : PosDisplayDensity.Standard) : new();
    }

    public PosDisplayRoles? LoadDisplayRoles() => Read<DisplaySettings>(displayPath) is { Version: 1, Roles.IsValid: true } saved
        ? saved.Roles : null;

    public void SaveDisplayRoles(PosDisplayRoles? roles)
    {
        if (roles is null)
        {
            PosInstallationPolicy.AssertNoLinks(displayPath);
            if (Directory.Exists(Path.GetDirectoryName(displayPath))) File.Delete(displayPath);
            return;
        }
        if (!roles.IsValid) throw new InvalidDataException("Invalid local display preferences.");
        Write(displayPath, new DisplaySettings(1, roles));
    }

    private static T? Read<T>(string path) where T : class
    {
        try
        {
            PosInstallationPolicy.AssertNoLinks(path);
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            byte[] bytes = new byte[1025];
            int length = 0;
            while (length < bytes.Length)
            {
                int count = stream.Read(bytes.AsSpan(length));
                if (count == 0) break;
                length += count;
            }
            return length is > 0 and <= 1024 ? JsonSerializer.Deserialize<T>(bytes.AsSpan(0, length)) : null;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public void SaveTouchFirst(bool enabled) => Save(Load() with { TouchFirst = enabled });

    public void Save(PosUiOptions options)
    {
        if (!Enum.IsDefined(options.Density)) throw new ArgumentOutOfRangeException(nameof(options));
        Write(inputPath, new Settings(1, options.TouchFirst, options.Density.ToString()));
    }

    private static void Write<T>(string path, T settings)
    {
        PosInstallationPolicy.AssertNoLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(settings));
            PosInstallationPolicy.AssertNoLinks(path);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
