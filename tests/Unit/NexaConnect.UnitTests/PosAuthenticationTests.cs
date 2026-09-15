using System.Net;
using System.Text;
using NexaConnect.POS;

namespace NexaConnect.UnitTests;

public sealed class PosAuthenticationTests
{
    [Fact]
    public async Task Refresh_rotates_credentials_without_extending_absolute_session()
    {
        DateTimeOffset absoluteExpiry = DateTimeOffset.UtcNow.AddHours(8);
        var initial = new PosTokenSet("old-access", "old-refresh", DateTimeOffset.UtcNow.AddSeconds(10),
            "Bearer", DateTimeOffset.UtcNow.AddHours(-2), absoluteExpiry);
        var store = new TokenStore(initial);
        using var authentication = new PosAuthentication(PosCheckoutIntegrationTests.Configuration(),
            new Handler(HttpStatusCode.OK,
                "{\"access_token\":\"new-access\",\"refresh_token\":\"new-refresh\",\"expires_in\":300,\"token_type\":\"Bearer\"}"),
            store);

        PosTokenSet refreshed = await authentication.RefreshAsync();

        Assert.Equal("new-access", refreshed.AccessToken);
        Assert.Equal("new-refresh", refreshed.RefreshToken);
        Assert.Equal(absoluteExpiry, refreshed.SessionExpiresAtUtc);
        Assert.Same(refreshed, store.Saved);
        Assert.False(store.Deleted);
    }

    [Fact]
    public async Task Rejected_refresh_clears_persisted_credentials_and_requires_sign_in()
    {
        var initial = new PosTokenSet("old-access", "old-refresh", DateTimeOffset.UtcNow.AddSeconds(-1),
            "Bearer", DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(9));
        var store = new TokenStore(initial);
        using var authentication = new PosAuthentication(PosCheckoutIntegrationTests.Configuration(),
            new Handler(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}"), store);

        await Assert.ThrowsAsync<PosReauthenticationRequiredException>(
            () => authentication.RefreshAsync());

        Assert.True(authentication.RequiresInteractiveSignIn);
        Assert.Equal(string.Empty, authentication.CurrentToken!.AccessToken);
        Assert.Null(authentication.CurrentToken.RefreshToken);
        Assert.NotNull(authentication.CurrentToken.LockedAtUtc);
        Assert.Same(authentication.CurrentToken, store.Saved);
    }

    [Fact]
    public void Persisted_lock_marker_requires_interactive_sign_in_after_restart()
    {
        var marker = new PosTokenSet(string.Empty, null, DateTimeOffset.MinValue, "Bearer",
            null, null, DateTimeOffset.UtcNow.AddMinutes(-1));
        var store = new TokenStore(marker);

        using var authentication = new PosAuthentication(PosCheckoutIntegrationTests.Configuration(),
            new Handler(HttpStatusCode.OK, "{}"), store);

        Assert.True(authentication.RequiresInteractiveSignIn);
        Assert.Same(marker, authentication.CurrentToken);
    }

    [Fact]
    public void Lock_replaces_online_credentials_with_persisted_nonsecret_marker()
    {
        var initial = new PosTokenSet("access", "refresh", DateTimeOffset.UtcNow.AddMinutes(5),
            "Bearer", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(10));
        var store = new TokenStore(initial);
        using var authentication = new PosAuthentication(PosCheckoutIntegrationTests.Configuration(),
            new Handler(HttpStatusCode.OK, "{}"), store);

        authentication.RequireInteractiveSignIn();

        Assert.True(authentication.RequiresInteractiveSignIn);
        Assert.Equal(string.Empty, store.Saved!.AccessToken);
        Assert.Null(store.Saved.RefreshToken);
        Assert.NotNull(store.Saved.LockedAtUtc);
    }

    private sealed class TokenStore(PosTokenSet? initial) : IPosTokenStore
    {
        public PosTokenSet? Saved { get; private set; }
        public bool Deleted { get; private set; }
        public PosTokenSet? Load() => initial;
        public void Save(PosTokenSet token) => Saved = token;
        public void Delete() => Deleted = true;
    }

    private sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal("refresh_token", (await request.Content!.ReadAsStringAsync(cancellationToken))
                .Split('&').Select(value => value.Split('=', 2)).Single(value => value[0] == "grant_type")[1]);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }
}
