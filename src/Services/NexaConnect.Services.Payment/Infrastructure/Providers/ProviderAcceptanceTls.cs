using System.Net.Security;
using System.Security.Cryptography;

namespace NexaConnect.Services.Payment.Infrastructure.Providers;

public static class ProviderAcceptanceTls
{
    public static HttpClientHandler CreateHandler(string? certificateSha256, string baseUrl, bool testing)
    {
        if (string.IsNullOrWhiteSpace(certificateSha256)) return new HttpClientHandler();
        if (!testing || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != "https" || uri.Host != "127.0.0.1"
            || certificateSha256.Length != 64 || certificateSha256.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidOperationException("Simulator certificate pin requires Testing, IPv4 loopback HTTPS, and a SHA256 fingerprint.");
        byte[] expected = Convert.FromHexString(certificateSha256);
        var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = (request, certificate, _, errors) =>
            request.RequestUri?.Host == "127.0.0.1" && certificate is not null
            && DateTime.UtcNow >= certificate.NotBefore.ToUniversalTime()
            && DateTime.UtcNow <= certificate.NotAfter.ToUniversalTime()
            && (errors & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) == 0
            && CryptographicOperations.FixedTimeEquals(expected, certificate.GetCertHash(HashAlgorithmName.SHA256));
        return handler;
    }
}
