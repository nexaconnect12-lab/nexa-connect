namespace NexaConnect.Contracts.Reporting;

// Version-one transport contracts; owning services retain their financial policy and persistence.
public sealed record SourceFenceCommand(Guid OperationId,EndOfDayWindow Window,Guid ApprovalId,Guid SealId,DateTimeOffset ExpiresAtUtc);
public sealed record SourceDayFence(Guid OperationId,EndOfDayWindow Window,Guid ApprovalId,Guid SealId,
    DateTimeOffset ExpiresAtUtc,bool Cancelled,bool Active,SourceFinancialRevision? AcquiredRevision);
