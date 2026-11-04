namespace NexaConnect.Services.POS.Domain.CashierPins;

// The verifier is opaque to the domain. No PIN or verifier is exposed in transport snapshots.
public sealed class CashierPinCredential
{
    public string State { get; private set; } = "unset";
    public long Version { get; private set; }
    public string? ProtectedVerifier { get; private set; }
    public int FailedAttempts { get; private set; }
    public DateTimeOffset? LockedUntilUtc { get; private set; }
    public static CashierPinCredential Restore(string state, long version, string? verifier, int failures, DateTimeOffset? lockedUntil)
        => new() { State = state, Version = version, ProtectedVerifier = verifier, FailedAttempts = failures, LockedUntilUtc = lockedUntil };
    public static bool ValidPin(string? pin) => pin is { Length: 6 } && pin.All(char.IsAsciiDigit);
    public bool IsLocked(DateTimeOffset now) => LockedUntilUtc > now;
    public bool Verify(long expectedVersion, DateTimeOffset now, Func<string, bool> matches)
    {
        if (Version != expectedVersion || State != "active" || ProtectedVerifier is null || IsLocked(now)) return false;
        if (LockedUntilUtc is not null) { FailedAttempts = 0; LockedUntilUtc = null; }
        if (matches(ProtectedVerifier)) { FailedAttempts = 0; LockedUntilUtc = null; return true; }
        if (++FailedAttempts >= 5) LockedUntilUtc = now.AddMinutes(15);
        return false;
    }
    public bool Change(long expectedVersion, string protectedVerifier, DateTimeOffset now, Func<string, bool> matchesCurrent)
    {
        if (Version != expectedVersion || State == "revoked" || IsLocked(now)) return false;
        if (State == "active" && !Verify(expectedVersion, now, matchesCurrent)) return false;
        ProtectedVerifier = protectedVerifier; State = "active"; Version++;
        FailedAttempts = 0; LockedUntilUtc = null; return true;
    }
    public bool Administer(long expectedVersion, string action)
    {
        if (Version != expectedVersion || action is not ("reset" or "revoke")) return false;
        State = action == "reset" ? "unset" : "revoked";
        ProtectedVerifier = null; Version++; FailedAttempts = 0; LockedUntilUtc = null; return true;
    }
}
