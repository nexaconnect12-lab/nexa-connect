using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace NexaConnect.Services.Payment.Infrastructure.Webhooks;

public static class OmiseWebhookSignature
{
    public static bool ValidSecret(string? secret)
    {
        try { return Convert.FromBase64String(secret ?? "").Length is >= 16 and <= 128; }
        catch (FormatException) { return false; }
    }
    public static bool Verify(ReadOnlySpan<byte> body, string timestamp, string signatures, string secret, DateTimeOffset now)
    {
        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds)
            || Math.Abs((decimal)now.ToUnixTimeSeconds() - seconds) > 300 || signatures.Length > 129 || !ValidSecret(secret))
            return false;
        byte[] prefix = Encoding.UTF8.GetBytes(timestamp + ".");
        byte[] payload = new byte[prefix.Length + body.Length];
        prefix.CopyTo(payload, 0); body.CopyTo(payload.AsSpan(prefix.Length));
        byte[] key = Convert.FromBase64String(secret);
        byte[] expected;
        try { expected = HMACSHA256.HashData(key, payload); }
        finally { CryptographicOperations.ZeroMemory(key); }
        string[] candidates = signatures.Split(',');
        if (candidates.Length is < 1 or > 2) return false;
        bool matches = false;
        foreach (string candidate in candidates)
        {
            if (candidate.Length != 64) continue;
            try { matches |= CryptographicOperations.FixedTimeEquals(Convert.FromHexString(candidate), expected); }
            catch (FormatException) { }
        }
        return matches;
    }
}
