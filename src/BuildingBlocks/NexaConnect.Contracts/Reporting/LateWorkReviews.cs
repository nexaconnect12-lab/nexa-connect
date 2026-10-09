namespace NexaConnect.Contracts.Reporting;

public sealed record LateWorkScope(EndOfDayWindow Window,Guid SettlementId);
public sealed record SourceLateReviewRequest(LateWorkScope Scope,LateReviewCommand Command);
public sealed record LateReviewCommand(Guid WorkId,Guid OperationId,long ExpectedVersion,string Decision,string ReasonCode);
public sealed record LateReviewEntry(long Version,string Status,string Decision,string ReasonCode,DateTimeOffset ReviewedAtUtc);
public sealed record LateWorkItem(Guid WorkId,string EventType,DateTimeOffset ReceivedAtUtc,DateTimeOffset? OccurredAtUtc,
    string CustodyReason,IReadOnlyDictionary<string,Guid> Records,long Version,string Status);
public sealed record LateWorkPage(LateWorkScope Scope,LateWorkItem[] Items,string? NextCursor,bool CanReview);
public sealed record LateWorkDetail(LateWorkScope Scope,LateWorkItem Item,Guid[] SettlementLinks,
    LateReviewEntry[] History,bool HistoryTruncated,bool CanReview);
public sealed record LateReviewResult(Guid OperationId,LateReviewEntry OperationDecision,LateWorkDetail Detail);
