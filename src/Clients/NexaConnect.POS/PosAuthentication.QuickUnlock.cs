using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using NexaConnect.Contracts.POS;

namespace NexaConnect.POS;

public sealed partial class PosAuthentication
{
    private readonly ICashierPinPort _pinPort;
    private CashierPinStatusV1? _pinStatus;
    private PosTokenSet? _quickUnlockToken;
    private DateTimeOffset? _lastFullLoginUtc;
    internal TimeProvider QuickUnlockClock { get; set; } = TimeProvider.System;
    private void ClearQuickUnlock()
    {
        // Clears process capability only. Login/logout never delete the central credential.
        _pinStatus = null; _quickUnlockToken = null; _lastFullLoginUtc = null;
    }
    private bool Live(PosTokenSet? token, DateTimeOffset now) => token is not null && HasOperator(token)
        && token.LockedAtUtc is null && token.ExpiresAtUtc > now && !PosSessionPolicy.IsAbsoluteExpired(token, now);
    private bool Resumable(PosTokenSet? token) => token is not null && HasOperator(token) && token.LockedAtUtc is null
        && !string.IsNullOrWhiteSpace(token.RefreshToken) && !PosSessionPolicy.IsAbsoluteExpired(token, QuickUnlockClock.GetUtcNow());
    public bool CanEnrollSessionPin
    {
        get { lock (_sync) return !disposed && _configuration.EnablePinUnlock && !_requiresInteractiveSignIn && _pending is null
            && Live(CurrentToken, QuickUnlockClock.GetUtcNow()) && _lastFullLoginUtc is {} login
            && QuickUnlockClock.GetUtcNow() - login is var elapsed && elapsed >= TimeSpan.Zero && elapsed <= TimeSpan.FromMinutes(2); }
    }
    public bool HasSessionPin { get { lock (_sync) return _pinStatus?.State == "active"; } }
    public bool CanQuickUnlock
    {
        get { lock (_sync) return !disposed && _configuration.EnablePinUnlock && _requiresInteractiveSignIn
            && _pinStatus?.State == "active" && Resumable(_quickUnlockToken)
            && CurrentToken is {} marker && SameOperator(marker, _quickUnlockToken!); }
    }
    public async Task LoadPinStatusAsync(CancellationToken ct = default)
    {
        using var correlation = NexaConnect.Observability.CorrelationContext.Push(IdentityCorrelation());
        PosTokenSet? token;
        lock (_sync) { token = CurrentToken; if (disposed || !_configuration.EnablePinUnlock || _requiresInteractiveSignIn || !Live(token, QuickUnlockClock.GetUtcNow())) return; }
        var status = await PinCall(() => _pinPort.StatusAsync(token!.AccessToken, ct));
        lock (_sync) { if (!disposed && !_requiresInteractiveSignIn && ReferenceEquals(CurrentToken, token)) _pinStatus = status; }
    }
    public async Task<bool> EnrollSessionPinAsync(string pin, string? currentPin = null, CancellationToken ct = default)
    {
        using var correlation = NexaConnect.Observability.CorrelationContext.Push(IdentityCorrelation());
        PosTokenSet token;
        lock (_sync) { if (!CanEnrollSessionPin) throw new InvalidOperationException("Recent full sign-in required."); token = CurrentToken!; }
        await LoadPinStatusAsync(ct);
        long version;
        lock (_sync)
        {
            if (!CanEnrollSessionPin || !ReferenceEquals(CurrentToken, token) || _pinStatus is null) return false;
            version = _pinStatus.Version;
        }
        var result = await PinCall(() => _pinPort.ChangeAsync(token.AccessToken, pin, currentPin, version, ct));
        lock (_sync)
        {
            if (disposed || _requiresInteractiveSignIn || !ReferenceEquals(CurrentToken, token)) return false;
            _pinStatus = result.Status; SafeEvent(result.Accepted ? "cashier-pin-changed" : "cashier-pin-change-denied", null);
            return result.Accepted;
        }
    }
    public void LockForQuickUnlock()
    {
        lock (_sync)
        {
            var token = CurrentToken;
            bool eligible = _configuration.EnablePinUnlock && HasSessionPin && !disposed && !_requiresInteractiveSignIn && _pending is null && Resumable(token);
            RequireInteractiveSignIn(); // Only a nonsecret marker is persisted while locked.
            if (eligible) _quickUnlockToken = token;
            SafeEvent("workspace-locked", null);
        }
    }
    internal bool LockedSessionRenewalDue
    {
        get
        {
            lock (_sync)
                return _pending is null && CanQuickUnlock && PosSessionPolicy.ShouldRefresh(_quickUnlockToken!,
                    QuickUnlockClock.GetUtcNow(), TimeSpan.FromSeconds(_configuration.TokenRefreshLeadSeconds));
        }
    }

    internal async Task MaintainLockedSessionAsync(CancellationToken ct)
    {
        using var correlation = NexaConnect.Observability.CorrelationContext.Push(IdentityCorrelation());
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
        ct = cancellation.Token;
        await _refreshGate.WaitAsync(ct);
        try
        {
            PosTokenSet original;
            lock (_sync)
            {
                if (!LockedSessionRenewalDue) return;
                original = _quickUnlockToken!;
            }
            await RenewLockedSessionAsync(original, "pin-session-keepalive", ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested) { SafeEvent("cashier-pin-keepalive-unavailable", null); throw; }
        finally { _refreshGate.Release(); }
    }

    private async Task<PosTokenSet?> RenewLockedSessionAsync(PosTokenSet original, string operation, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _configuration.Authority.TrimEnd('/') + "/protocol/openid-connect/token")
        { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["client_id"] = _configuration.ClientId, ["refresh_token"] = original.RefreshToken! }) };
        using var response = await SendIdentityAsync(request, operation, ct);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            lock (_sync)
            {
                ct.ThrowIfCancellationRequested();
                if (ReferenceEquals(_quickUnlockToken, original) && _pending is null) RequireInteractiveSignIn();
            }
            return null;
        }
        response.EnsureSuccessStatusCode();
        using var json = await response.Content.ReadFromJsonAsync<JsonDocument>(ct) ?? throw new System.IO.InvalidDataException("Empty renewal.");
        PosTokenSet renewed;
        try
        {
            renewed = await BindOperatorAsync(ReadToken(json.RootElement, original.RefreshToken,
                original.SessionStartedAtUtc!.Value, original.SessionExpiresAtUtc!.Value), ct);
        }
        catch (HttpRequestException error) when (error.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            lock (_sync)
            {
                ct.ThrowIfCancellationRequested();
                if (ReferenceEquals(_quickUnlockToken, original) && _pending is null) RequireInteractiveSignIn();
            }
            return null;
        }
        lock (_sync)
        {
            ct.ThrowIfCancellationRequested();
            if (!CanQuickUnlock || !ReferenceEquals(_quickUnlockToken, original) || _pending is not null) return null;
            if (!SameOperator(original, renewed) || !Resumable(renewed)) { RequireInteractiveSignIn(); return null; }
            _quickUnlockToken = renewed;
            return renewed;
        }
    }

    public async Task<bool> TryResumeWithSessionPinAsync(string pin, CancellationToken ct = default)
    {
        using var correlation = NexaConnect.Observability.CorrelationContext.Push(IdentityCorrelation());
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
        ct = cancellation.Token;
        await _refreshGate.WaitAsync(ct);
        try
        {
            PosTokenSet original;
            lock (_sync) { if (!CanQuickUnlock || _pending is not null) return false; original = _quickUnlockToken!; }
            var renewed = await RenewLockedSessionAsync(original, "pin-session-renewal", ct);
            if (renewed is null) return false;
            var status = await _pinPort.StatusAsync(renewed.AccessToken, ct);
            var result = await _pinPort.VerifyAsync(renewed.AccessToken, pin, status.Version, ct);
            lock (_sync)
            {
                ct.ThrowIfCancellationRequested();
                if (disposed || !CanQuickUnlock || !ReferenceEquals(_quickUnlockToken, renewed) || _pending is not null) return false;
                _pinStatus = result.Status;
                if (!result.Accepted || !Resumable(renewed) || !Live(renewed, QuickUnlockClock.GetUtcNow())) { SafeEvent("cashier-pin-denied", null); return false; }
                _tokenStore.Save(renewed);
                CurrentToken = renewed; _quickUnlockToken = null; _requiresInteractiveSignIn = false;
                SafeEvent("cashier-pin-resumed", null); return true;
            }
        }
        catch (Exception) when (!ct.IsCancellationRequested) { SafeEvent("cashier-pin-unavailable", null); throw; }
        finally { _refreshGate.Release(); }
    }
    private async Task<T> PinCall<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch { SafeEvent("cashier-pin-service-unavailable", null); throw; }
    }
}
