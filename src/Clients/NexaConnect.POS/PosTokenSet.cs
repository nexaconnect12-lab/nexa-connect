namespace NexaConnect.POS;

public sealed record PosTokenSet(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset ExpiresAtUtc,
    string TokenType,
    DateTimeOffset? SessionStartedAtUtc = null,
    DateTimeOffset? SessionExpiresAtUtc = null,
    DateTimeOffset? LockedAtUtc = null);
