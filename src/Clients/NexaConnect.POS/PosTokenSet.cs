namespace NexaConnect.POS;

public sealed record PosTokenSet(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset ExpiresAtUtc,
    string TokenType);

