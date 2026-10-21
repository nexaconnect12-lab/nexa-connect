using NexaConnect.POS;
using System.Text.Json;

namespace NexaConnect.UnitTests;

public sealed class PosSessionTokenStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "nexa-session-token-test-" + Guid.NewGuid().ToString("N"));

    [PosDpapiAcceptanceFact]
    public void Atomic_protected_marker_roundtrip_retains_operator_without_online_credentials()
    {
        var store = new WindowsTokenStore(directory);
        var token = new PosTokenSet("synthetic-access", "synthetic-refresh", DateTimeOffset.UtcNow.AddMinutes(5), "Bearer",
            Subject: "synthetic-cashier", Authority: "https://identity.example/realm");
        store.Save(token);
        Assert.Equal(token, new WindowsTokenStore(directory).Load());
        Assert.True(File.ReadAllBytes(Path.Combine(directory, "tokens.bin")).AsSpan().IndexOf("synthetic-access"u8) < 0);
        var marker = token with { AccessToken = "", RefreshToken = null, ExpiresAtUtc = DateTimeOffset.MinValue, LockedAtUtc = DateTimeOffset.UtcNow };
        store.Save(marker);
        Assert.Equal(marker, new WindowsTokenStore(directory).Load());
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [PosDpapiAcceptanceFact]
    public void Corrupt_protected_session_is_rejected_without_deleting_evidence()
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "tokens.bin");
        byte[] corrupt = [1, 2, 3, 4, 5];
        File.WriteAllBytes(path, corrupt);
        Assert.Throws<InvalidDataException>(() => new WindowsTokenStore(directory).Load());
        Assert.Equal(corrupt, File.ReadAllBytes(path));
    }

    [PosDpapiAcceptanceFact]
    public void Invalid_serialized_session_is_rejected_without_deleting_evidence()
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "tokens.bin");
        byte[] invalid = WindowsDataProtection.Protect(JsonSerializer.SerializeToUtf8Bytes<object?>(null));
        File.WriteAllBytes(path, invalid);
        Assert.Throws<InvalidDataException>(() => new WindowsTokenStore(directory).Load());
        Assert.Equal(invalid, File.ReadAllBytes(path));
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
}
