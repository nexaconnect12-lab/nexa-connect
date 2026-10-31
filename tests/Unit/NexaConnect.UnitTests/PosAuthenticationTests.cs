using System.Net;
using System.Text;
using System.Text.Json;
using NexaConnect.POS;

namespace NexaConnect.UnitTests;

public sealed class PosAuthenticationTests
{
    [Fact]
    public async Task Shutdown_cancels_pending_signin_without_deleting_original_owner()
    {
        using var f = new Fixture();
        await f.Login();
        var original = f.Store.Value;
        var signin = f.Auth.SignInAsync(true);
        f.Auth.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => signin);
        Assert.Same(original, f.Store.Value);
        await f.Callback(f.Uri!);
        Assert.Same(original, f.Store.Value);
    }

    [Fact]
    public async Task Shutdown_during_code_redemption_cannot_install_late_credentials()
    {
        using var f = new Fixture();
        f.Handler.DelayCode = true;
        var signin = f.Auth.SignInAsync();
        var callback = f.Callback(f.Uri!);
        await f.Handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        f.Auth.Dispose();
        f.Handler.Release.TrySetResult(); // Deliberately uncooperative handler returns after disposal.
        await callback.WaitAsync(TimeSpan.FromSeconds(3));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => signin);
        Assert.Null(f.Store.Value);
    }

    [Fact]
    public async Task Shutdown_cancels_refresh_and_gate_waiter_without_saving_or_disposed_gate_fault()
    {
        using var f = new Fixture();
        await f.Login();
        var original = f.Store.Value;
        f.Handler.DelayRefresh = true;
        var refresh = f.Auth.RefreshAsync();
        await f.Handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var waiter = f.Auth.RefreshAsync();
        f.Auth.Dispose();
        f.Handler.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        Assert.Same(original, f.Store.Value);
    }

    [Fact]
    public async Task Refresh_rotates_credentials_without_extending_absolute_session()
    {
        using var f = new Fixture();
        var initial = await f.Login();
        var renewed = await f.Auth.RefreshAsync();
        Assert.NotEqual(initial.AccessToken, renewed.AccessToken);
        Assert.Equal(initial.SessionStartedAtUtc, renewed.SessionStartedAtUtc);
        Assert.Equal(initial.SessionExpiresAtUtc, renewed.SessionExpiresAtUtc);
        Assert.Equal("cashier-a", renewed.Subject);
        Assert.Equal(2, f.Handler.IdentityReads);
    }

    [Fact]
    public async Task Idle_lock_preserves_operator_and_forces_login_to_resume()
    {
        using var f = new Fixture();
        await f.Login();
        f.Auth.RequireInteractiveSignIn();
        Locked(f);
        Assert.Equal("cashier-a", f.Store.Value!.Subject);
        await f.Login();
        Assert.Contains("prompt=login", f.Uri);
        Assert.False(f.Auth.RequiresInteractiveSignIn);
    }

    [Fact]
    public async Task Wrong_cashier_cannot_replace_original_marker_but_original_can_resume()
    {
        using var f = new Fixture();
        await f.Login();
        f.Auth.SetRetainedWorkGuard(() => true);
        f.Auth.RequireInteractiveSignIn();
        var marker = f.Store.Value;
        f.Handler.Subject = "cashier-b";
        await Assert.ThrowsAsync<PosSessionResumeException>(() => f.Login());
        Assert.Same(marker, f.Store.Value);
        Locked(f);
        f.Handler.Subject = "cashier-a";
        await f.Login();
        Assert.False(f.Auth.RequiresInteractiveSignIn);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Restart_retains_original_operator_and_requires_authentication(bool alreadyLocked)
    {
        using var f = new Fixture();
        await f.Login();
        if (alreadyLocked) f.Auth.RequireInteractiveSignIn();
        using var restart = new Fixture(f.Store);
        restart.Auth.SetRetainedWorkGuard(() => true);
        Locked(restart);
        restart.Handler.Subject = "cashier-b";
        await Assert.ThrowsAsync<PosSessionResumeException>(() => restart.Login());
        restart.Handler.Subject = "cashier-a";
        await restart.Login();
        Assert.Contains("prompt=login", restart.Uri);
    }

    [Fact]
    public async Task Cancellation_and_late_callback_leave_locked_state_unchanged()
    {
        using var f = new Fixture();
        await f.Login();
        f.Auth.RequireInteractiveSignIn();
        using var cancel = new CancellationTokenSource();
        var attempt = f.Auth.SignInAsync(cancellationToken: cancel.Token);
        string uri = f.Uri!;
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => attempt);
        await f.Callback(uri);
        Locked(f);
    }

    [Fact]
    public async Task Cancelled_redemption_cannot_overwrite_a_new_login()
    {
        using var f = new Fixture();
        await f.Login();
        f.Auth.RequireInteractiveSignIn();
        f.Handler.DelayCode = true;
        using var cancel = new CancellationTokenSource();
        var attempt = f.Auth.SignInAsync(cancellationToken: cancel.Token);
        var callback = f.Callback(f.Uri!);
        await f.Handler.Started.Task;
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => attempt);
        var resumed = await f.Login();
        f.Handler.Release.TrySetResult();
        await callback;
        Assert.Same(resumed, f.Store.Value);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    public async Task Rejected_refresh_preserves_operator_for_reauthentication(int status)
    {
        using var f = new Fixture();
        await f.Login();
        f.Handler.RefreshStatus = (HttpStatusCode)status;
        await Assert.ThrowsAsync<PosReauthenticationRequiredException>(() => f.Auth.RefreshAsync());
        Locked(f);
        Assert.Equal("cashier-a", f.Store.Value!.Subject);
    }

    [Fact]
    public async Task Refresh_identity_change_is_rejected()
    {
        using var f = new Fixture();
        await f.Login();
        f.Handler.Subject = "cashier-b";
        await Assert.ThrowsAsync<PosReauthenticationRequiredException>(() => f.Auth.RefreshAsync());
        Locked(f);
        Assert.Equal("cashier-a", f.Store.Value!.Subject);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(200)]
    public async Task Old_refresh_cannot_replace_or_relock_a_new_login(int status)
    {
        using var f = new Fixture();
        await f.Login();
        f.Handler.DelayRefresh = true;
        f.Handler.RefreshStatus = (HttpStatusCode)status;
        var refresh = f.Auth.RefreshAsync();
        await f.Handler.Started.Task;
        f.Auth.RequireInteractiveSignIn();
        var resumed = await f.Login();
        f.Handler.Release.TrySetResult();
        await refresh;
        Assert.Same(resumed, f.Store.Value);
        Assert.False(f.Auth.RequiresInteractiveSignIn);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"sub\":\"\"}")]
    [InlineData("{\"sub\":42}")]
    public async Task Malformed_identity_is_not_accepted(string body)
    {
        using var f = new Fixture();
        f.Handler.IdentityBody = body;
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Login());
        Assert.Null(f.Store.Value);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(503)]
    public async Task Revoked_or_unavailable_identity_keeps_resume_locked(int status)
    {
        using var f = new Fixture();
        await f.Login();
        f.Auth.RequireInteractiveSignIn();
        f.Handler.IdentityStatus = (HttpStatusCode)status;
        await Assert.ThrowsAsync<HttpRequestException>(() => f.Login());
        Locked(f);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("cashier-a", "https://other.example/realm")]
    public async Task Unbound_or_other_authority_recovery_blocks_before_browser(string? subject, string? authority)
    {
        var store = new Store { Value = new PosTokenSet("", null, DateTimeOffset.MinValue, "Bearer",
            LockedAtUtc: DateTimeOffset.UtcNow, Subject: subject, Authority: authority) };
        using var f = new Fixture(store);
        f.Auth.SetRetainedWorkGuard(() => true);
        await Assert.ThrowsAsync<PosSessionResumeException>(() => f.Auth.SignInAsync());
        Assert.Null(f.Uri);
        Assert.Equal(0, f.Handler.Requests);
        Locked(f);
    }

    [Fact]
    public async Task Signout_with_retained_work_is_rejected_and_clear_signout_allows_switch()
    {
        using var f = new Fixture();
        await f.Login();
        f.Auth.SetRetainedWorkGuard(() => true);
        Assert.Throws<PosSessionResumeException>(() => f.Auth.SignOut());
        f.Auth.SetRetainedWorkGuard(() => false);
        f.Auth.SignOut();
        f.Handler.Subject = "cashier-b";
        await f.Login();
        Assert.Equal("cashier-b", f.Store.Value!.Subject);
    }

    [Fact]
    public async Task Verified_userinfo_identity_uses_safe_correlation_not_decoded_access_claims()
    {
        using var f = new Fixture();
        using var scope = NexaConnect.Observability.CorrelationContext.Push("2b06b9ea-64c3-48a6-874c-3c14582edc36");
        var token = await f.Login();
        Assert.Equal("cashier-a", token.Subject);
        Assert.Equal(1, f.Handler.IdentityReads);
        Assert.All(f.Handler.Correlations, id => Assert.Equal("2b06b9ea-64c3-48a6-874c-3c14582edc36", id));
    }

    [Fact]
    public async Task Connectivity_failure_keeps_original_credentials_for_bounded_retry()
    {
        using var f = new Fixture();
        var original = await f.Login();
        f.Handler.RefreshStatus = HttpStatusCode.ServiceUnavailable;
        await Assert.ThrowsAsync<HttpRequestException>(() => f.Auth.RefreshAsync());
        Assert.Same(original, f.Store.Value);
        f.Handler.RefreshStatus = HttpStatusCode.OK;
        await f.Auth.RefreshAsync();
        Assert.False(f.Auth.RequiresInteractiveSignIn);
    }

    [Theory]
    [InlineData("order.flow:resume-01")]
    [InlineData("unsafe\r\nheader")]
    public async Task Correlation_preserves_valid_values_and_replaces_untrusted_values(string correlation)
    {
        using var f = new Fixture();
        using var scope = NexaConnect.Observability.CorrelationContext.Push(correlation);
        await f.Login();
        string propagated = f.Handler.Correlations.First();
        if (correlation.StartsWith("unsafe")) Assert.NotEqual(correlation, propagated);
        else Assert.Equal(correlation, propagated);
        Assert.All(f.Handler.Correlations, value => Assert.Equal(propagated, value));
    }

    [Fact]
    public async Task Lock_restart_and_resume_preserve_drawer_checkout_and_settlement_ids()
    {
        string path = Path.Combine(Path.GetTempPath(), "nexa-session-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var protector = new TestProtector();
            var local = new LocalPosStore(path, protector);
            using var f = new Fixture();
            await f.Login();
            var shift = new LocalShiftState(Guid.NewGuid(), "SESSION-TEST", DateTimeOffset.UtcNow);
            var cash = new LocalCashSessionState(Guid.NewGuid(), shift.ShiftId, DateTimeOffset.UtcNow);
            var checkout = PendingCheckout.Create(PosCheckoutIntegrationTests.Configuration(), [new(Guid.NewGuid(), 1)]);
            var settlement = new LocalPendingSettlementState(checkout.OrderId, 80m, "THB", checkout.SettlementKey,
                "cash", OutcomeUncertain: true);
            local.SaveActiveShift(shift);
            local.SaveCashSession(cash);
            local.SavePendingCheckout(checkout);
            local.SavePendingSettlement(settlement);
            f.Auth.SetRetainedWorkGuard(() => local.LoadActiveShift() is not null);
            f.Auth.RequireInteractiveSignIn();
            var persisted = new Store { Value = JsonSerializer.Deserialize<PosTokenSet>(JsonSerializer.Serialize(f.Store.Value)) };
            using var restart = new Fixture(persisted);
            var recovered = new LocalPosStore(path, protector);
            restart.Auth.SetRetainedWorkGuard(() => recovered.LoadActiveShift() is not null);
            await restart.Login();
            Assert.Equal(shift, recovered.LoadActiveShift());
            Assert.Equal(cash, recovered.LoadCashSession());
            Assert.Equal(checkout.OrderId, recovered.LoadPendingCheckout()!.OrderId);
            Assert.Equal(checkout.SettlementKey, recovered.LoadPendingCheckout()!.SettlementKey);
            Assert.Equal(settlement, recovered.LoadPendingSettlement());
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
    }

    private sealed class TestProtector : ILocalPayloadProtector
    {
        public byte[] Protect(byte[] value) => value.Select(b => (byte)(b ^ 0x5A)).ToArray();
        public byte[] Unprotect(byte[] value) => Protect(value);
    }

    private static void Locked(Fixture f)
    {
        Assert.True(f.Auth.RequiresInteractiveSignIn);
        Assert.Equal("", f.Auth.CurrentToken!.AccessToken);
        Assert.Null(f.Auth.CurrentToken.RefreshToken);
        Assert.NotNull(f.Auth.CurrentToken.LockedAtUtc);
    }

    private sealed class Fixture : IDisposable
    {
        public Store Store { get; }
        public Handler Handler { get; } = new();
        public PosAuthentication Auth { get; }
        public string? Uri { get; private set; }
        public Fixture(Store? store = null)
        {
            Store = store ?? new Store();
            Auth = new PosAuthentication(PosCheckoutIntegrationTests.Configuration(), Handler, Store, uri => Uri = uri);
        }
        public async Task<PosTokenSet> Login()
        {
            var attempt = Auth.SignInAsync();
            if (!attempt.IsCompleted) await Callback(Uri!);
            return await attempt;
        }
        public Task Callback(string uri)
        {
            string state = new Uri(uri).Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).Single(p => p[0] == "state")[1];
            return Auth.HandleCallbackAsync($"nexaconnect-pos://oauth/callback?code=test&state={state}");
        }
        public void Dispose() => Auth.Dispose();
    }
    private sealed class Store : IPosTokenStore
    {
        public PosTokenSet? Value { get; set; }
        public PosTokenSet? Load() => Value;
        public void Save(PosTokenSet token) => Value = token;
        public void Delete() => Value = null;
    }
    private sealed class Handler : HttpMessageHandler
    {
        public string Subject = "cashier-a";
        public string? IdentityBody;
        public HttpStatusCode RefreshStatus = HttpStatusCode.OK, IdentityStatus = HttpStatusCode.OK;
        public int IdentityReads, Requests;
        public bool DelayCode, DelayRefresh;
        public List<string> Correlations = [];
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            string correlation = request.Headers.GetValues("X-Correlation-ID").Single();
            Assert.True(correlation.Length <= 128);
            Correlations.Add(correlation);
            Assert.StartsWith(PosCheckoutIntegrationTests.Configuration().Authority, request.RequestUri!.AbsoluteUri);
            if (request.Method == HttpMethod.Get)
            {
                Assert.EndsWith("/protocol/openid-connect/userinfo", request.RequestUri.AbsoluteUri);
                Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
                Assert.False(string.IsNullOrWhiteSpace(request.Headers.Authorization.Parameter));
                IdentityReads++;
                return Response(IdentityStatus, IdentityBody ?? JsonSerializer.Serialize(new { sub = Subject }));
            }
            bool refresh = (await request.Content!.ReadAsStringAsync(cancellationToken)).Contains("grant_type=refresh_token");
            if ((refresh && DelayRefresh) || (!refresh && DelayCode))
            {
                DelayCode = false;
                Started.TrySetResult();
                await Release.Task;
            }
            return Response(refresh ? RefreshStatus : HttpStatusCode.OK, JsonSerializer.Serialize(new
            { access_token = "opaque-access-" + Requests, refresh_token = "refresh-" + Requests, expires_in = 300, token_type = "Bearer" }));
        }
        private static HttpResponseMessage Response(HttpStatusCode status, string body) => new(status)
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
