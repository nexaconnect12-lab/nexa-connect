namespace NexaConnect.Contracts.POS;

public sealed record DrawerHistoryItemV1(Guid OperationId,string Cause,string? Reason,Guid? OrderId,
    Guid ShiftId,Guid CashSessionId,string Status,DateTimeOffset ReservedAtUtc,DateTimeOffset ExpiresAtUtc,DateTimeOffset? ReportedAtUtc);
public sealed record DrawerHistoryPageV1(Guid OrganizationId,Guid RestaurantId,Guid BranchId,Guid StoreId,Guid TerminalId,
    DateTimeOffset FromUtc,DateTimeOffset ToUtc,IReadOnlyList<DrawerHistoryItemV1> Items,string? NextCursor);
