namespace NexaConnect.Contracts.Reporting;

// Version-one read contracts. Each source owns its query and authorization.
public sealed record EndOfDayWindow(Guid OrganizationId, Guid RestaurantId, Guid BranchId,
    DateTimeOffset FromUtc, DateTimeOffset ToUtc);
public sealed record TenderTotal(string Method, string Currency, decimal Amount);
public sealed record OrderDaySummary(EndOfDayWindow Window, DateTimeOffset ObservedAtUtc,
    decimal GrossSales, int CompletedOrders, int UnresolvedOrders, int EvidenceGaps,
    IReadOnlyList<TenderTotal> Tenders, IReadOnlyList<string> Currencies, string? EvidenceVersion = null);
public sealed record PaymentDaySummary(EndOfDayWindow Window, DateTimeOffset ObservedAtUtc,
    decimal CompletedRefunds, int UnresolvedPayments, int UnresolvedRefunds, int EvidenceGaps,
    IReadOnlyList<string> Currencies, string? EvidenceVersion = null);
public sealed record PosDaySummary(EndOfDayWindow Window, DateTimeOffset ObservedAtUtc,
    int OpenShifts, int OpenCashSessions, int PendingCashReviews, decimal CashVariance,
    IReadOnlyList<string> Currencies, string? EvidenceVersion = null,decimal LateCashCorrectionAdjustment=0,int LateCashCorrections=0);
