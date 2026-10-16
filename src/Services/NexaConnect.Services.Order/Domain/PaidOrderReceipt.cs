namespace NexaConnect.Services.Order.Domain;

// Versioned ordinary sales receipt; not a fiscal invoice or a payment credential.
public sealed record PaidOrderReceipt(int Version, string ReceiptNumber, Guid OrderId,
    Guid OrganizationId, Guid RestaurantId, Guid BranchId, string OrderNumber,
    DateTimeOffset PaidAtUtc, string Currency, string Tender, IReadOnlyList<ReceiptLine> Lines,
    OrderPricing? Pricing, decimal SubtotalAmount, decimal ServiceChargeAmount, decimal TaxAmount, decimal TotalAmount)
{
    public static PaidOrderReceipt Create(OrderAggregate order, DateTimeOffset paidAtUtc, string tender)
    {
        if (order.Status != OrderStatus.Paid) throw new InvalidOperationException("Only a paid order can issue a receipt.");
        if (paidAtUtc == default || string.IsNullOrWhiteSpace(tender) || tender.Length > 64)
            throw new ArgumentException("Receipt completion time and tender are required.");
        return new(1, $"R-{order.Id:N}".ToUpperInvariant(), order.Id, order.OrganizationId,
            order.RestaurantId, order.BranchId, order.OrderNumber, paidAtUtc.ToUniversalTime(), order.Currency,
            tender == "cash_manual" ? "cash" : tender,
            Array.AsReadOnly(order.Lines.Select(l => new ReceiptLine(l.ProductId, l.Name, l.UnitPrice, l.Quantity, l.Total)).ToArray()),
            order.Pricing, order.Pricing?.SubtotalAmount ?? order.TotalAmount,
            order.Pricing?.ServiceChargeAmount ?? 0, order.Pricing?.TaxAmount ?? 0, order.TotalAmount);
    }
}
public sealed record ReceiptLine(Guid ProductId, string Name, decimal UnitPrice, int Quantity, decimal Total);
