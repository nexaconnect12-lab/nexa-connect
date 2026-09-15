namespace NexaConnect.POS;

public static class PosSessionPolicy
{
    public static bool IsIdleExpired(DateTimeOffset lastActivityUtc, DateTimeOffset nowUtc,
        TimeSpan idleTimeout) => nowUtc - lastActivityUtc >= idleTimeout;

    public static bool IsAbsoluteExpired(PosTokenSet token, DateTimeOffset nowUtc) =>
        token.SessionExpiresAtUtc is null || token.SessionExpiresAtUtc <= nowUtc;

    public static bool ShouldRefresh(PosTokenSet token, DateTimeOffset nowUtc, TimeSpan refreshLead) =>
        !IsAbsoluteExpired(token, nowUtc) &&
        !string.IsNullOrWhiteSpace(token.RefreshToken) &&
        token.ExpiresAtUtc - nowUtc <= refreshLead;
}
