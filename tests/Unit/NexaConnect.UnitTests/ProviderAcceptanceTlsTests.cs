using NexaConnect.Services.Payment.Infrastructure.Providers;

namespace NexaConnect.UnitTests;

public sealed class ProviderAcceptanceTlsTests
{
    [Theory]
    [InlineData(false, "https://127.0.0.1:1234/")]
    [InlineData(true, "https://example.com/")]
    [InlineData(true, "http://127.0.0.1:1234/")]
    public void Pin_is_rejected_outside_testing_loopback_https(bool testing, string url) =>
        Assert.Throws<InvalidOperationException>(() => ProviderAcceptanceTls.CreateHandler(new string('A', 64), url, testing));

    [Fact]
    public void Default_keeps_platform_certificate_validation()
    {
        using var handler = ProviderAcceptanceTls.CreateHandler(null, "https://example.com/", false);
        Assert.Null(handler.ServerCertificateCustomValidationCallback);
    }

    [Fact]
    public void Testing_pin_rejects_different_certificate_and_hostname_errors()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=127.0.0.1", rsa,
            System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
        using var message = new HttpRequestMessage(HttpMethod.Get, "https://127.0.0.1/");
        using var handler = ProviderAcceptanceTls.CreateHandler(certificate.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256), message.RequestUri!.ToString(), true);
        Assert.True(handler.ServerCertificateCustomValidationCallback!(message, certificate, null, System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.False(handler.ServerCertificateCustomValidationCallback!(message, certificate, null, System.Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch));
        using var wrong = ProviderAcceptanceTls.CreateHandler(new string('A', 64), message.RequestUri.ToString(), true);
        Assert.False(wrong.ServerCertificateCustomValidationCallback!(message, certificate, null, System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors));
    }
}
