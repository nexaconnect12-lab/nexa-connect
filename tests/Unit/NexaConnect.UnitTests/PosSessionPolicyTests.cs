using NexaConnect.POS;

namespace NexaConnect.UnitTests;

public sealed class PosSessionPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Idle_timeout_locks_at_the_boundary()
    {
        Assert.False(PosSessionPolicy.IsIdleExpired(Now.AddMinutes(-4).AddSeconds(-59), Now,
            TimeSpan.FromMinutes(5)));
        Assert.True(PosSessionPolicy.IsIdleExpired(Now.AddMinutes(-5), Now,
            TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void Refresh_is_requested_before_expiry_and_after_resume()
    {
        PosTokenSet active = Token(Now.AddSeconds(61));
        PosTokenSet nearExpiry = Token(Now.AddSeconds(60));
        PosTokenSet expired = Token(Now.AddSeconds(-1));

        Assert.False(PosSessionPolicy.ShouldRefresh(active, Now, TimeSpan.FromSeconds(60)));
        Assert.True(PosSessionPolicy.ShouldRefresh(nearExpiry, Now, TimeSpan.FromSeconds(60)));
        Assert.True(PosSessionPolicy.ShouldRefresh(expired, Now, TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void Missing_refresh_or_absolute_boundary_requires_interactive_sign_in()
    {
        Assert.False(PosSessionPolicy.ShouldRefresh(Token(Now.AddMinutes(1)) with { RefreshToken = null },
            Now, TimeSpan.FromMinutes(1)));
        Assert.True(PosSessionPolicy.IsAbsoluteExpired(Token(Now.AddMinutes(1)) with
            { SessionExpiresAtUtc = Now }, Now));
        Assert.False(PosSessionPolicy.ShouldRefresh(Token(Now.AddMinutes(1)) with
            { SessionExpiresAtUtc = Now }, Now, TimeSpan.FromMinutes(1)));
    }

    [Theory]
    [InlineData(0, 10, 60)]
    [InlineData(5, 25, 60)]
    [InlineData(5, 10, 10)]
    public void Invalid_session_settings_are_rejected(int idleMinutes, int absoluteHours, int refreshSeconds)
    {
        PosClientConfiguration configuration = PosCheckoutIntegrationTests.Configuration() with
        {
            SessionIdleTimeoutMinutes = idleMinutes,
            SessionAbsoluteTimeoutHours = absoluteHours,
            TokenRefreshLeadSeconds = refreshSeconds
        };

        Assert.Throws<InvalidDataException>(configuration.ValidateCheckout);
    }

    private static PosTokenSet Token(DateTimeOffset accessExpiry) => new(
        "access", "refresh", accessExpiry, "Bearer", Now.AddHours(-1), Now.AddHours(9));
}
