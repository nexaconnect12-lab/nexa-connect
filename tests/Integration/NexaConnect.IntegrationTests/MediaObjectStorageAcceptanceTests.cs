extern alias MEDIA;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using S3MediaObjectStorage = MEDIA::NexaConnect.Services.Media.Infrastructure.S3MediaObjectStorage;
using ClamAvMediaContentSafety = MEDIA::NexaConnect.Services.Media.Infrastructure.ClamAvMediaContentSafety;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;

namespace NexaConnect.IntegrationTests;

public sealed class MediaObjectStorageAcceptanceTests
{
    [ExternalAcceptanceFact("NEXA_CLAMAV_ACCEPTANCE")]
    public async Task Clamav_rejects_test_signature()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["MediaSafety:MalwareScanEnabled"] = "true", ["MediaSafety:ClamAvHost"] = "127.0.0.1", ["MediaSafety:ClamAvPort"] = "3310" }).Build();
        var safety = new ClamAvMediaContentSafety(configuration, NullLogger<ClamAvMediaContentSafety>.Instance);
        byte[] signature = Encoding.ASCII.GetBytes("X5O!P%@AP[4\\PZX54(P^)7CC)7}$EIC" + "AR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*");
        var result = await safety.InspectAsync(signature, "image/png", default);
        Assert.False(result.Safe); Assert.Equal("malware-detected", result.RejectionCategory);
    }

    [ExternalAcceptanceFact("NEXA_MINIO_ACCEPTANCE")]
    public async Task Minio_enforces_and_returns_sha256_checksum()
    {
        string secret = Environment.GetEnvironmentVariable("MINIO_ROOT_PASSWORD") ?? throw new InvalidOperationException("MINIO_ROOT_PASSWORD is required.");
        using var s3 = new AmazonS3Client(new BasicAWSCredentials("nexaconnect-local", secret), new AmazonS3Config { ServiceURL = "http://127.0.0.1:9100", ForcePathStyle = true });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["MediaStorage:Bucket"] = "nexaconnect-media", ["MediaStorage:ServiceUrl"] = "http://127.0.0.1:9100" }).Build();
        var storage = new S3MediaObjectStorage(s3, configuration);
        byte[] content = [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0];
        string checksum = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        string key = $"acceptance/{Guid.NewGuid():D}.png";

        string uploadUrl = await storage.CreateUploadUrlAsync(key, "image/png", content.Length, checksum, TimeSpan.FromMinutes(2), default);
        using var http = new HttpClient(); using var request = new HttpRequestMessage(HttpMethod.Put, uploadUrl) { Content = new ByteArrayContent(content) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        request.Headers.TryAddWithoutValidation("x-amz-checksum-sha256", Convert.ToBase64String(Convert.FromHexString(checksum)));
        using HttpResponseMessage upload = await http.SendAsync(request);
        Assert.True(upload.IsSuccessStatusCode, await upload.Content.ReadAsStringAsync());
        var info = await storage.InspectAsync(key, default);
        Assert.NotNull(info); Assert.Equal(content.Length, info.SizeBytes); Assert.Equal(checksum, info.ChecksumSha256);
        Assert.Equal(content, await storage.ReadAsync(key, content.Length, default));
        await storage.DeleteAsync(key, default);

        string badKey = $"acceptance/{Guid.NewGuid():D}-bad.png";
        string badUrl = await storage.CreateUploadUrlAsync(badKey, "image/png", content.Length, checksum, TimeSpan.FromMinutes(2), default);
        byte[] tampered = (byte[])content.Clone(); tampered[^1] = 1;
        using var badRequest = new HttpRequestMessage(HttpMethod.Put, badUrl) { Content = new ByteArrayContent(tampered) };
        badRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        badRequest.Headers.TryAddWithoutValidation("x-amz-checksum-sha256", Convert.ToBase64String(Convert.FromHexString(checksum)));
        using HttpResponseMessage badUpload = await http.SendAsync(badRequest);
        Assert.False(badUpload.IsSuccessStatusCode);
    }
}

public sealed class ExternalAcceptanceFactAttribute : FactAttribute
{
    public ExternalAcceptanceFactAttribute(string environmentVariable)
    {
        if (Environment.GetEnvironmentVariable(environmentVariable) != "1") Skip = $"{environmentVariable}=1 is required for this external acceptance test.";
    }
}
