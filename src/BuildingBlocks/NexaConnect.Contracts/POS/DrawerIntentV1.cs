namespace NexaConnect.Contracts.POS;

public sealed record DrawerIntentRequestV1(Guid OrganizationId,Guid RestaurantId,Guid BranchId,Guid StoreId,
    Guid TerminalId,Guid ShiftId,Guid CashSessionId,Guid OperationId,string Cause,Guid? OrderId,string? Reason);
public sealed record DrawerOutcomeRequestV1(Guid OrganizationId,string Outcome);
// MaySubmit is true only on the first committed reservation, never on a retry/read.
// Submitted is client-reported submission, not evidence of physical opening.
public sealed record DrawerIntentV1(Guid OperationId,string Status,bool MaySubmit,DateTimeOffset ReservedAtUtc,
    DateTimeOffset ExpiresAtUtc,DateTimeOffset? ReportedAtUtc);
