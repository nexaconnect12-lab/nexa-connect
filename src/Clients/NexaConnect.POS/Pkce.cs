using System.Security.Cryptography;

namespace NexaConnect.POS;

internal sealed record PkceRequest(string State, string Verifier, string Challenge);

internal static class Pkce
{
    public static PkceRequest Create()
    {
        byte[] verifierBytes = RandomNumberGenerator.GetBytes(32);
        string verifier = Base64Url(verifierBytes);
        string challenge = Base64Url(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier)));
        return new PkceRequest(Base64Url(RandomNumberGenerator.GetBytes(32)), verifier, challenge);
    }

    private static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
