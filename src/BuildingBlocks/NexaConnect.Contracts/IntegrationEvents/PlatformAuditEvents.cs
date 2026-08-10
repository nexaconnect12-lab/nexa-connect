namespace NexaConnect.Contracts.IntegrationEvents;

public sealed record PlatformAuditEventV1(
    Guid EventId,
    Guid CorrelationId,
    DateTimeOffset OccurredAtUtc,
    string SubjectId,
    Guid? OrganizationId,
    string Action,
    string ResourceType,
    string ResourceId,
    string Outcome) : IIntegrationEvent;
