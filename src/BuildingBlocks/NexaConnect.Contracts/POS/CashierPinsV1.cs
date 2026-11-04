namespace NexaConnect.Contracts.POS;

public sealed record CashierPinScopeV1(Guid BranchId, Guid StoreId, Guid TerminalId);
public sealed record CashierPinStatusV1(string State, long Version, DateTimeOffset? LockedUntilUtc);
public sealed record CashierPinChangeV1(CashierPinScopeV1 Scope, string NewPin, string? CurrentPin, long ExpectedVersion, Guid OperationId);
public sealed record CashierPinVerifyV1(CashierPinScopeV1 Scope, string Pin, long ExpectedVersion, Guid AttemptId);
public sealed record CashierPinAdminV1(CashierPinScopeV1 Scope, string Subject, long ExpectedVersion, Guid OperationId, string Action);
public sealed record CashierPinStaffV1(CashierPinScopeV1 Scope, string Subject);
public sealed record CashierPinResultV1(bool Accepted, CashierPinStatusV1 Status);
