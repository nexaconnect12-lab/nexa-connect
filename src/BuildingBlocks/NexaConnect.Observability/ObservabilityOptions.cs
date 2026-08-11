namespace NexaConnect.Observability;

public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    public bool OtlpEnabled { get; set; }
    public string? OtlpEndpoint { get; set; }
    public string? ServiceVersion { get; set; }
}
