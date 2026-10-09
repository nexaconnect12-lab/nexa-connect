namespace NexaConnect.Services.Reporting.Domain;

public sealed record RefundFinancialFact(
    Guid SourceEventId,
    Guid OrganizationId,
    Guid RestaurantId,
    Guid BranchId,
    Guid OrderId,
    Guid PaymentIntentId,
    Guid RefundId,
    decimal Amount,
    string Currency,
    string ReasonCode,
    DateTimeOffset RefundedAtUtc,
    decimal CumulativeRefundedAmount,
    decimal CapturedAmount,
    string ReceiptNumber)
{
    private static readonly HashSet<string> Reasons =
    [
        "customer_request", "duplicate_charge", "item_unavailable", "service_issue", "other"
    ];

    public void Validate()
    {
        if (SourceEventId == Guid.Empty || OrganizationId == Guid.Empty || RestaurantId == Guid.Empty
            || BranchId == Guid.Empty || OrderId == Guid.Empty || PaymentIntentId == Guid.Empty || RefundId == Guid.Empty)
            throw new ArgumentException("Refund financial fact identifiers are required.");
        if (Amount <= 0 || Amount > 999999999999999.9999m || decimal.Round(Amount, 4) != Amount
            || CapturedAmount <= 0 || CapturedAmount > 999999999999999.9999m
            || decimal.Round(CapturedAmount, 4) != CapturedAmount || decimal.Round(CumulativeRefundedAmount, 4) != CumulativeRefundedAmount
            || CumulativeRefundedAmount < Amount || CumulativeRefundedAmount > CapturedAmount)
            throw new ArgumentException("Refund financial amounts are invalid.");
        if (string.IsNullOrWhiteSpace(Currency) || Currency.Length != 3 || Currency.Any(c => c is < 'A' or > 'Z'))
            throw new ArgumentException("Refund financial currency is invalid.");
        if (!Reasons.Contains(ReasonCode)) throw new ArgumentException("Refund financial reason is invalid.");
        if (RefundedAtUtc == default) throw new ArgumentException("Refund financial timestamp is required.");
        if (string.IsNullOrWhiteSpace(ReceiptNumber) || ReceiptNumber.Length is < 4 or > 80 || ReceiptNumber.Any(char.IsControl))
            throw new ArgumentException("Refund receipt number is invalid.");
    }
}
