namespace NexaConnect.Services.Reporting.Domain;

public sealed record SaleFinancialFact(Guid SourceEventId, Guid OrganizationId, Guid RestaurantId, Guid BranchId,
    Guid OrderId, Guid PaymentId, string PaymentOrigin, string Method, string Currency, string Channel,
    string ServiceType, DateTimeOffset OrderedAtUtc, DateTimeOffset PaidAtUtc, string ReceiptNumber,
    decimal SubtotalAmount, decimal ServiceChargeAmount, decimal TaxAmount, decimal TotalAmount)
{
    // JSON preserves decimal scale; equal commercial amounts must hash identically on redelivery.
    public SaleFinancialFact Canonicalize()
    {
        Validate();
        static decimal Amount(decimal value) => decimal.Parse(value.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);
        return this with { OrderedAtUtc = OrderedAtUtc.ToUniversalTime(), PaidAtUtc = PaidAtUtc.ToUniversalTime(),
            SubtotalAmount = Amount(SubtotalAmount), ServiceChargeAmount = Amount(ServiceChargeAmount),
            TaxAmount = Amount(TaxAmount), TotalAmount = Amount(TotalAmount) };
    }

    public void Validate()
    {
        if (new[] { SourceEventId, OrganizationId, RestaurantId, BranchId, OrderId, PaymentId }.Any(id => id == Guid.Empty))
            throw new ArgumentException("Sale financial identities are required.");
        if (PaymentOrigin is not ("payment_intent" or "manual_settlement")
            || string.IsNullOrWhiteSpace(Method) || Method.Length > 64 || Method.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_'))
            || (PaymentOrigin == "manual_settlement") != (Method is "cash" or "promptpay_manual"))
            throw new ArgumentException("Sale tender evidence is invalid.");
        if (Currency.Length != 3 || Currency.Any(c => c is < 'A' or > 'Z'))
            throw new ArgumentException("Sale currency is invalid.");
        if (new[] { Channel, ServiceType, ReceiptNumber }.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 80 || s.Any(char.IsControl)))
            throw new ArgumentException("Sale classification or receipt is invalid.");
        if (OrderedAtUtc == default || PaidAtUtc == default) throw new ArgumentException("Sale timestamps are required.");
        if (new[] { SubtotalAmount, ServiceChargeAmount, TaxAmount, TotalAmount }
            .Any(v => v < 0 || v > 999999999999999.9999m || decimal.Round(v, 4) != v)
            || SubtotalAmount + ServiceChargeAmount + TaxAmount != TotalAmount)
            throw new ArgumentException("Sale amounts are invalid.");
    }
}
