namespace NexaConnect.Contracts.POS;

public sealed record ManualRefundSaleV1(Guid OrganizationId, Guid RestaurantId, Guid BranchId,
    Guid OrderId, Guid SettlementId, string Method, decimal Amount, string Currency, DateTimeOffset PaidAtUtc);
public sealed record ManualRefundPreviewV1(Guid OrderId, string Method, string Currency, decimal PaidAmount, decimal AvailableAmount);

public sealed record ApproveManualRefundV1(Guid OperationId, decimal Amount, string Reason, string CashierSubject, Guid CashierShiftId = default);
public sealed record ManualRefundCustodyV1(Guid TerminalId, Guid ShiftId, Guid? CashSessionId);
public sealed record CompleteManualRefundV1(ManualRefundCustodyV1 Custody, bool TransferVerified, string? TransferReference);
public sealed record ManualRefundStatusV1(Guid OperationId, Guid OrderId, string Method, string Currency,
    decimal Amount, string Reason, string Status, DateTimeOffset ApprovedAtUtc, DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc, bool Replayed, DateTimeOffset? ReleasedAtUtc = null);
public sealed record AssignedManualRefundsV1(IReadOnlyList<ManualRefundStatusV1> Items, bool MoreAvailable);
public sealed record ManualRefundAvailabilityV1(Guid TerminalId, Guid ShiftId);
public sealed record ManualRefundAvailabilityResultV1(bool Available);
public sealed record ManualRefundCashierV1(Guid ShiftId, string Subject, string TerminalCode, string ShiftNumber, bool CashDrawerOpen);
public sealed record ManualRefundCashiersV1(IReadOnlyList<ManualRefundCashierV1> Items);
