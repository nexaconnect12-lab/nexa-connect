using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NexaConnect.POS;

// Non-secret presentation preference; never an authorization or financial setting.
public sealed class PosUiPreferences(string directory)
{
    private readonly string path = Path.Combine(Path.GetFullPath(directory), "input.json");
    private sealed record Settings(int Version, bool TouchFirst, string? Density = null);

    public static PosUiPreferences ForTerminal(PosClientConfiguration configuration, string? localApplicationData = null)
    {
        var key = $"{configuration.OrganizationId:N}|{configuration.RestaurantId:N}|{configuration.BranchId:N}|{configuration.StoreId:N}|{configuration.TerminalId:N}";
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return new(Path.Combine(localApplicationData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexaConnect", "Preferences", hash));
    }

    public bool LoadTouchFirst() => Load().TouchFirst;

    public PosUiOptions Load()
    {
        try
        {
            PosInstallationPolicy.AssertNoLinks(path);
            if (!File.Exists(path)) return new();
            using var stream = File.OpenRead(path);
            byte[] bytes = new byte[1025];
            int length = 0;
            while (length < bytes.Length)
            {
                int count = stream.Read(bytes.AsSpan(length));
                if (count == 0) break;
                length += count;
            }
            if (length is <= 0 or > 1024) return new();
            var settings = JsonSerializer.Deserialize<Settings>(bytes.AsSpan(0, length));
            return settings is { Version: 1 } ? new(settings.TouchFirst,
                settings.Density == nameof(PosDisplayDensity.Touch) ? PosDisplayDensity.Touch : PosDisplayDensity.Standard) : new();
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public void SaveTouchFirst(bool enabled) => Save(Load() with { TouchFirst = enabled });

    public void Save(PosUiOptions options)
    {
        if (!Enum.IsDefined(options.Density)) throw new ArgumentOutOfRangeException(nameof(options));
        PosInstallationPolicy.AssertNoLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(new Settings(1, options.TouchFirst, options.Density.ToString())));
            PosInstallationPolicy.AssertNoLinks(path);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
