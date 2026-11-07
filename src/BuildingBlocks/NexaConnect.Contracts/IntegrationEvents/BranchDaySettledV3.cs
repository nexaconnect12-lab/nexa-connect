namespace NexaConnect.Contracts.IntegrationEvents;

public sealed record SettlementManualRefundsV3(Guid ManifestId,Guid RevisionEpoch,long SourceRevision,
    [property:System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.WriteAsString|System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)] decimal CashAmount,
    [property:System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.WriteAsString|System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)] decimal PromptPayAmount,
    int CompletedCount,string? EvidenceVersion);

// Preserve V1/V2 historical publications; only new settlements carry exact manual refund proof.
public sealed record BranchDaySettledV3(Guid EventId,Guid CorrelationId,DateTimeOffset OccurredAtUtc,
    BranchDaySettledV2 Settlement,SettlementManualRefundsV3 ManualRefunds) : IIntegrationEvent;
