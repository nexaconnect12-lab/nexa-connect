namespace NexaConnect.POS;

public sealed partial class PosAuthentication
{
    private PosSessionPin? _sessionPin;
    private PosTokenSet? _quickUnlockToken;
    private DateTimeOffset? _lastFullLoginUtc;
    internal TimeProvider QuickUnlockClock { get; set; } = TimeProvider.System;
    private void ClearQuickUnlock()
    {
        _sessionPin?.Dispose(); _sessionPin = null; _quickUnlockToken = null; _lastFullLoginUtc = null;
    }
    private bool Live(PosTokenSet? token, DateTimeOffset now) => token is not null && HasOperator(token)
        && token.LockedAtUtc is null && token.ExpiresAtUtc > now && !PosSessionPolicy.IsAbsoluteExpired(token, now);
    public bool CanEnrollSessionPin
    {
        get { lock (_sync) return !disposed && _configuration.EnablePinUnlock && !_requiresInteractiveSignIn
            && Live(CurrentToken, QuickUnlockClock.GetUtcNow()) && _lastFullLoginUtc is {} login
            && QuickUnlockClock.GetUtcNow() - login is var elapsed && elapsed >= TimeSpan.Zero && elapsed <= TimeSpan.FromMinutes(2); }
    }
    public bool HasSessionPin { get { lock (_sync) return _sessionPin?.Available == true; } }
    public bool CanQuickUnlock
    {
        get { lock (_sync) return !disposed && _configuration.EnablePinUnlock && _requiresInteractiveSignIn
            && _sessionPin?.Available == true && Live(_quickUnlockToken, QuickUnlockClock.GetUtcNow())
            && CurrentToken is {} marker && SameOperator(marker, _quickUnlockToken!); }
    }
    public void EnrollSessionPin(string pin)
    {
        lock (_sync)
        {
            if (!CanEnrollSessionPin) throw new InvalidOperationException("Full sign-in within the last two minutes is required to set a session PIN.");
            var next = new PosSessionPin(pin);
            _sessionPin?.Dispose(); _sessionPin = next;
            SafeEvent("session-pin-enrolled", null);
        }
    }
    public void LockForQuickUnlock()
    {
        lock (_sync)
        {
            var token = CurrentToken;
            bool eligible = _configuration.EnablePinUnlock && _sessionPin?.Available == true
                && !disposed && !_requiresInteractiveSignIn && _pending is null && Live(token, QuickUnlockClock.GetUtcNow());
            // Persist only the existing locked identity marker; bearer credentials stay private in memory.
            RequireInteractiveSignIn();
            if (eligible) _quickUnlockToken = token;
            SafeEvent("workspace-locked", null);
        }
    }
    public bool TryResumeWithSessionPin(string pin)
    {
        lock (_sync)
        {
            if (!CanQuickUnlock || _pending is not null) return false;
            if (!_sessionPin!.Verify(pin))
            {
                SafeEvent("session-pin-denied", null);
                if (!_sessionPin.Available) _quickUnlockToken = null;
                return false;
            }
            if (!CanQuickUnlock) { _quickUnlockToken = null; return false; }
            var token = _quickUnlockToken!;
            _tokenStore.Save(token); // Restore authority only after persistence succeeds.
            CurrentToken = token; _quickUnlockToken = null; _requiresInteractiveSignIn = false;
            SafeEvent("session-pin-resumed", null);
            return true;
        }
    }
}
