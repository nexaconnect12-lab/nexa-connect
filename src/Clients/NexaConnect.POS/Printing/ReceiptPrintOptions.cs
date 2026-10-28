using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NexaConnect.POS;

public enum ReceiptPaper { Standard, Roll80 }
public sealed record ReceiptPrintOptions(string? PrinterName = null, ReceiptPaper Paper = ReceiptPaper.Standard)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Paper) || PrinterName is not null && (string.IsNullOrWhiteSpace(PrinterName)
            || PrinterName.Length > 256 || PrinterName.Any(char.IsControl))) throw new InvalidDataException("Invalid printer preference.");
    }
}

// Non-secret, current-user terminal preferences, separate from input and financial state.
public sealed class ReceiptPrinterPreferences(string directory)
{
    private readonly string path = Path.Combine(Path.GetFullPath(directory), "printing.json");
    private sealed record Stored(int Version, string? PrinterName, string Paper);
    public static ReceiptPrinterPreferences ForTerminal(PosClientConfiguration c, string? root = null)
    {
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{c.OrganizationId:N}|{c.RestaurantId:N}|{c.BranchId:N}|{c.StoreId:N}|{c.TerminalId:N}")));
        return new(Path.Combine(root ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexaConnect", "Preferences", hash));
    }
    public ReceiptPrintOptions Load()
    {
        try
        {
            PosInstallationPolicy.AssertNoLinks(path);
            if (!File.Exists(path)) return new();
            using var stream = File.OpenRead(path); byte[] bytes = new byte[4097]; int length = 0;
            while (length < bytes.Length) { int count = stream.Read(bytes.AsSpan(length)); if (count == 0) break; length += count; }
            if (length is <= 0 or > 4096) return new();
            var stored = JsonSerializer.Deserialize<Stored>(bytes.AsSpan(0, length));
            if (stored is null || stored.Version != 1 || !Enum.TryParse<ReceiptPaper>(stored.Paper, out var paper)) return new();
            var options = new ReceiptPrintOptions(stored.PrinterName, paper); options.Validate(); return options;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException) { return new(); }
    }
    public void Save(ReceiptPrintOptions options)
    {
        options.Validate(); PosInstallationPolicy.AssertNoLinks(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new Stored(1, options.PrinterName, options.Paper.ToString()));
            if (bytes.Length > 4096) throw new InvalidDataException("Printer preference exceeds its size limit.");
            File.WriteAllBytes(temporary, bytes); PosInstallationPolicy.AssertNoLinks(path); File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
