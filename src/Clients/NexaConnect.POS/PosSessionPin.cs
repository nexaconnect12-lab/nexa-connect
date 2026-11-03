using System.Security.Cryptography;

namespace NexaConnect.POS;

// Process/session lifetime only. A restart destroys both this verifier and its unlock capability.
internal sealed class PosSessionPin : IDisposable
{
    private readonly byte[] salt = RandomNumberGenerator.GetBytes(16);
    private byte[] hash;
    private int failures;
    public PosSessionPin(string pin)
    {
        if (!Valid(pin)) throw new ArgumentException("Use exactly six digits.");
        hash = Derive(pin);
    }
    public static bool Valid(string pin) => pin.Length == 6 && pin.All(c => c is >= '0' and <= '9');
    private byte[] Derive(string pin) => Rfc2898DeriveBytes.Pbkdf2(pin, salt, 600_000, HashAlgorithmName.SHA256, 32);
    public bool Available => hash.Length != 0 && failures < 5;
    public bool Verify(string pin)
    {
        if (!Available) return false;
        bool matches = false;
        if (Valid(pin))
        {
            byte[] candidate = Derive(pin);
            try { matches = CryptographicOperations.FixedTimeEquals(candidate, hash); }
            finally { CryptographicOperations.ZeroMemory(candidate); }
        }
        if (matches) { failures = 0; return true; }
        if (++failures >= 5) Dispose();
        return false;
    }
    public void Dispose() { CryptographicOperations.ZeroMemory(hash); hash = []; CryptographicOperations.ZeroMemory(salt); }
}
