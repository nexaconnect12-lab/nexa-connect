using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using NexaConnect.Services.POS.Application.CashierPins;
using NexaConnect.Services.POS.Domain.CashierPins;

namespace NexaConnect.Services.POS.Infrastructure.Identity;

public sealed class CashierPinProtector(IDataProtectionProvider provider) : ICashierPinProtector
{
    private IDataProtector Protection(CashierPinKey key) => provider.CreateProtector("NexaConnect.POS.CashierPin.v1", key.OrganizationId.ToString("N"), key.Issuer, key.Subject);
    public string Create(CashierPinKey key, string pin)
    {
        if (!CashierPinCredential.ValidPin(pin)) throw new ArgumentException("Six digits required.");
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, 600_000, HashAlgorithmName.SHA256, 32);
        try { return Protection(key).Protect($"1:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}"); }
        finally { CryptographicOperations.ZeroMemory(hash); CryptographicOperations.ZeroMemory(salt); }
    }
    public bool Matches(CashierPinKey key, string protectedVerifier, string pin)
    {
        if (!CashierPinCredential.ValidPin(pin)) return false;
        string[] fields = Protection(key).Unprotect(protectedVerifier).Split(':');
        if (fields.Length != 3 || fields[0] != "1") throw new CryptographicException("Unknown PIN verifier format.");
        byte[] salt = Convert.FromBase64String(fields[1]), expected = Convert.FromBase64String(fields[2]);
        if (salt.Length != 16 || expected.Length != 32) throw new CryptographicException("Invalid PIN verifier.");
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, 600_000, HashAlgorithmName.SHA256, 32);
        try { return CryptographicOperations.FixedTimeEquals(expected, hash); }
        finally { CryptographicOperations.ZeroMemory(hash); CryptographicOperations.ZeroMemory(expected); CryptographicOperations.ZeroMemory(salt); }
    }
}
