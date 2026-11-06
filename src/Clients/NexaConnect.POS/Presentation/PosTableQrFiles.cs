using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace NexaConnect.POS;

internal sealed record PosTableQrChoice(string TableCode, DateTimeOffset ExpiresAtUtc);

// Only administrator-provisioned portal exports are read from this local folder.
internal sealed class PosTableQrFiles(string directory, Guid branchId)
{
    internal const string Extension = ".nexa-table-qr.json";
    private const int Size = 384;
    private const int MaxFileBytes = 64 * 1024;
    private sealed record Export(int Version, Guid BranchId, string TableCode, DateTimeOffset ExpiresAtUtc, string Png);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal IReadOnlyList<PosTableQrChoice> List(DateTimeOffset now)
    {
        PosInstallationPolicy.AssertNoLinks(directory);
        if (!Directory.Exists(directory)) return [];
        var files = Directory.EnumerateFiles(directory, "*" + Extension).Take(101).ToArray();
        if (files.Length > 100) throw new InvalidDataException("Too many table QR files.");
        var result = new List<PosTableQrChoice>();
        foreach (var path in files)
        {
            try
            {
                var entry = Read(path, now);
                result.Add(new(entry.TableCode, entry.ExpiresAtUtc));
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException) { }
        }
        return result.OrderBy(choice => choice.TableCode, StringComparer.Ordinal).ToArray();
    }

    internal BitmapSource Load(PosTableQrChoice choice, DateTimeOffset now)
    {
        if (!ValidCode(choice.TableCode)) throw new InvalidDataException("Invalid table code.");
        var entry = Read(Path.Combine(directory, choice.TableCode + Extension), now);
        if (entry.ExpiresAtUtc != choice.ExpiresAtUtc) throw new InvalidDataException("Table QR file changed.");
        byte[] png = Convert.FromBase64String(entry.Png);
        if (png.Length < 33 || !png.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})
            || !png.AsSpan(12, 4).SequenceEqual("IHDR"u8)
            || BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16, 4)) != Size
            || BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20, 4)) != Size)
            throw new InvalidDataException("Invalid table QR image.");
        using var stream = new MemoryStream(png, writable: false);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count != 1 || decoder.Frames[0].PixelWidth != Size || decoder.Frames[0].PixelHeight != Size)
            throw new InvalidDataException("Invalid table QR image.");
        var image = decoder.Frames[0];
        image.Freeze();
        return image;
    }

    private Export Read(string path, DateTimeOffset now)
    {
        PosInstallationPolicy.AssertNoLinks(path);
        using var stream = File.OpenRead(path);
        if (stream.Length is <= 0 or > MaxFileBytes) throw new InvalidDataException("Invalid table QR file size.");
        byte[] bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        var entry = JsonSerializer.Deserialize<Export>(bytes, JsonOptions);
        if (entry is not { Version: 1, Png.Length: > 0 } || entry.BranchId != branchId || !ValidCode(entry.TableCode)
            || Path.GetFileName(path) != entry.TableCode + Extension
            || entry.ExpiresAtUtc <= now || entry.ExpiresAtUtc > now.AddDays(31))
            throw new InvalidDataException("Table QR file is invalid, expired, or belongs to another branch.");
        return entry;
    }

    private static bool ValidCode(string? code) => code is { Length: > 0 and <= 32 }
        && Regex.IsMatch(code, @"\A[A-Z0-9_-]+\z", RegexOptions.CultureInvariant);
}
