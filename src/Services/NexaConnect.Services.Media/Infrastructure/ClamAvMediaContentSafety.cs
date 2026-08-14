using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using NexaConnect.Services.Media.Application;

namespace NexaConnect.Services.Media.Infrastructure;

public sealed class ClamAvMediaContentSafety(IConfiguration configuration, ILogger<ClamAvMediaContentSafety> logger) : IMediaContentSafety
{
    public async Task<MediaSafetyResult> InspectAsync(byte[] content, string declaredContentType, CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("MediaSafety:MalwareScanEnabled")) return MatchesMagic(content, declaredContentType) ? new(true, null) : new(false, "type-signature-mismatch");

        string host = configuration["MediaSafety:ClamAvHost"] ?? throw new InvalidOperationException("MediaSafety:ClamAvHost is required.");
        int port = configuration.GetValue("MediaSafety:ClamAvPort", 3310);
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, cancellationToken);
        await using NetworkStream stream = client.GetStream();
        await stream.WriteAsync("zINSTREAM\0"u8.ToArray(), cancellationToken);
        const int chunkSize = 8192;
        byte[] length = new byte[4];
        for (int offset = 0; offset < content.Length; offset += chunkSize)
        {
            int count = Math.Min(chunkSize, content.Length - offset);
            BinaryPrimitives.WriteInt32BigEndian(length, count);
            await stream.WriteAsync(length, cancellationToken);
            await stream.WriteAsync(content.AsMemory(offset, count), cancellationToken);
        }
        Array.Clear(length); await stream.WriteAsync(length, cancellationToken); await stream.FlushAsync(cancellationToken);
        byte[] response = new byte[512]; int read = await stream.ReadAsync(response, cancellationToken);
        string verdict = Encoding.UTF8.GetString(response, 0, read).TrimEnd('\0', '\r', '\n');
        if (verdict.EndsWith(" OK", StringComparison.Ordinal)) return MatchesMagic(content, declaredContentType) ? new(true, null) : new(false, "type-signature-mismatch");
        if (verdict.Contains(" FOUND", StringComparison.Ordinal)) { logger.LogWarning("Media malware scan rejected content"); return new(false, "malware-detected"); }
        throw new InvalidOperationException("Malware scanner returned an invalid response.");
    }

    private static bool MatchesMagic(ReadOnlySpan<byte> content, string contentType) => contentType switch
    {
        "image/png" => content.Length >= 8 && content[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
        "image/jpeg" => content.Length >= 3 && content[0] == 0xff && content[1] == 0xd8 && content[2] == 0xff,
        "image/webp" => content.Length >= 12 && content[..4].SequenceEqual("RIFF"u8) && content.Slice(8, 4).SequenceEqual("WEBP"u8),
        _ => false
    };
}
