using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NexaConnect.Services.Media.Infrastructure;

namespace NexaConnect.UnitTests;

public sealed class MediaContentSafetyTests
{
    [Theory]
    [InlineData("image/png", new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })]
    [InlineData("image/jpeg", new byte[] { 255, 216, 255, 224 })]
    [InlineData("image/webp", new byte[] { 82, 73, 70, 70, 0, 0, 0, 0, 87, 69, 66, 80 })]
    public async Task Declared_image_type_requires_matching_magic(string type, byte[] content)
    {
        var scanner = Scanner();
        Assert.True((await scanner.InspectAsync(content, type, default)).Safe);
        Assert.False((await scanner.InspectAsync(content, type == "image/png" ? "image/jpeg" : "image/png", default)).Safe);
    }

    private static ClamAvMediaContentSafety Scanner() => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["MediaSafety:MalwareScanEnabled"] = "false" }).Build(),
        NullLogger<ClamAvMediaContentSafety>.Instance);
}
