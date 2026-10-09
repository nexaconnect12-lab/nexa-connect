namespace NexaConnect.Contracts.Reporting;

public sealed record SourceBarrierCommand(Guid SettlementId,Guid OperationId,SourceFenceCommand Fence);
public sealed record SourceBarrierRequest(SourceBarrierCommand Command,string Phase,Guid? DecisionId=null);
public sealed record SourceBarrierProof(SourceBarrierCommand Command,string Phase,Guid? DecisionId,DateTimeOffset ChangedAtUtc,long LateWorkCount);
