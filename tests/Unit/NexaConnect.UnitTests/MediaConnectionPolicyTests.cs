using NexaConnect.CustomerBff;

namespace NexaConnect.UnitTests;

public sealed class MediaConnectionPolicyTests
{
    [Theory]
    [InlineData("http://storage.example.com",true)]
    [InlineData("http://127.0.0.1:12345",false)]
    [InlineData("https://storage.example.com/path",false)]
    [InlineData("https://storage.example.com/",false)]
    [InlineData("https://u:p@storage.example.com",false)]
    [InlineData("https://storage.example.com?q=x",false)]
    [InlineData("https://storage.example.com#x",false)]
    [InlineData("https://*.example.com",false)]
    public void Invalid_or_unsafe_origin_is_rejected(string origin,bool development) =>
        Assert.Throws<InvalidOperationException>(()=>MediaConnectionPolicy.ValidateOrigin(origin,development));

    [Fact]
    public void Only_staff_policy_gets_the_single_configured_origin()
    {
        var origin=MediaConnectionPolicy.ValidateOrigin("https://storage.example.com",false);
        Assert.Contains("connect-src 'self' https://storage.example.com;",MediaConnectionPolicy.ContentSecurityPolicy(false,origin));
        Assert.DoesNotContain("storage.example.com",MediaConnectionPolicy.ContentSecurityPolicy(true,origin));
        Assert.Contains("connect-src 'self';",MediaConnectionPolicy.ContentSecurityPolicy(false,null));
        Assert.Equal("http://127.0.0.1:12345",MediaConnectionPolicy.ValidateOrigin("http://127.0.0.1:12345",true));
        Assert.Null(MediaConnectionPolicy.ValidateOrigin(null,false));
    }
}
