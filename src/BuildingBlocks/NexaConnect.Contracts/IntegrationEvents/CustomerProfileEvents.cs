namespace NexaConnect.Contracts.IntegrationEvents;

public sealed record CustomerProfileCreatedV1(
    Guid EventId,
    Guid CorrelationId,
    DateTimeOffset OccurredAtUtc,
    Guid OrganizationId,
    Guid CustomerId,
    string Status,
    long ConcurrencyVersion,
    string RequestCorrelationId) : IIntegrationEvent;
