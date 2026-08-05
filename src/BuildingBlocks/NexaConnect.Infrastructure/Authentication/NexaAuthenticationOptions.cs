namespace NexaConnect.Infrastructure.Authentication;

public sealed class NexaAuthenticationOptions
{
    public string Authority { get; init; } = string.Empty;

    public string Audience { get; init; } = NexaAuthenticationDefaults.ApiAudience;

    public bool RequireHttpsMetadata { get; init; } = true;

    public int ClockSkewSeconds { get; init; } = 30;
}
