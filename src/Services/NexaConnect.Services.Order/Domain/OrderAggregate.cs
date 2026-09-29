namespace NexaConnect.Services.Order.Domain;

public enum OrderStatus
{
    Draft = 0,
    Submitted = 1,
    InventoryReserved = 2,
    KitchenAccepted = 3,
    Paid = 4,
    PaymentFailed = 5,
    Rejected = 6,
    PaymentPending = 7,
    PaymentReview = 8
}

public sealed record OrderLine(
    Guid ProductId,
    string Name,
    decimal UnitPrice,
    int Quantity,
    string PreparationStation)
{
    public decimal Total => UnitPrice * Quantity;
}

public sealed class OrderAggregate
{
    private readonly List<OrderLine> lines;

    private OrderAggregate(
        Guid id,
        Guid organizationId,
        Guid branchId,
        IReadOnlyCollection<OrderLine> lines,
        string currency,
        Guid? restaurantId = null,
        string channel = "pos",
        string serviceType = "takeaway",
        string? orderNumber = null,
        string? idempotencyKey = null,
        string? workflowPaymentMethod = null,
        Guid? workflowCorrelationId = null, OrderPricing? pricing = null, string? pricingFingerprint = null)
    {
        Id = id;
        OrganizationId = organizationId;
        BranchId = branchId;
        this.lines = lines.ToList();
        if (pricing is not null && (pricing != OrderPricing.Calculate(lines, currency, new(pricing.PolicyVersion, pricing.TaxPercent, pricing.TaxInclusive, pricing.ServiceChargePercent))
            || pricingFingerprint is null || pricingFingerprint.Length != 64))
            throw new ArgumentException("Invalid accepted pricing snapshot.");
        Pricing = pricing;
        PricingFingerprint = pricingFingerprint;
        Currency = currency;
        RestaurantId = restaurantId ?? organizationId;
        Channel = channel;
        ServiceType = serviceType;
        OrderNumber = orderNumber ?? id.ToString("N")[..12];
        IdempotencyKey = idempotencyKey;
        WorkflowPaymentMethod = workflowPaymentMethod;
        WorkflowCorrelationId = workflowCorrelationId;
        Status = OrderStatus.Draft;
    }

    public Guid Id { get; }
    public Guid OrganizationId { get; }
    public Guid RestaurantId { get; }
    public Guid BranchId { get; }
    public string Currency { get; }
    public string Channel { get; }
    public string ServiceType { get; }
    public string OrderNumber { get; }
    public string? IdempotencyKey { get; }
    public string? WorkflowPaymentMethod { get; }
    public Guid? WorkflowCorrelationId { get; }
    public Guid? PaymentIntentId { get; private set; }
    public OrderStatus Status { get; private set; }
    public IReadOnlyList<OrderLine> Lines => lines.AsReadOnly();
    public OrderPricing? Pricing { get; }
    public string? PricingFingerprint { get; }
    public PaidOrderReceipt? Receipt { get; private set; }
    public void IssueReceipt(DateTimeOffset paidAtUtc, string tender) => Receipt ??= PaidOrderReceipt.Create(this, paidAtUtc, tender);
    public void RestoreReceipt(PaidOrderReceipt? receipt)
    {
        if (receipt is null) return;
        if (Receipt is not null || Status != OrderStatus.Paid || receipt.OrderId != Id
            || receipt.OrganizationId != OrganizationId || receipt.RestaurantId != RestaurantId
            || receipt.BranchId != BranchId || receipt.Currency != Currency || receipt.TotalAmount != TotalAmount
            || receipt.Version != 1 || receipt.ReceiptNumber != $"R-{Id:N}".ToUpperInvariant()
            || receipt.OrderNumber != OrderNumber || receipt.PaidAtUtc == default || string.IsNullOrWhiteSpace(receipt.Tender)
            || receipt.SubtotalAmount != (Pricing?.SubtotalAmount ?? TotalAmount)
            || receipt.ServiceChargeAmount != (Pricing?.ServiceChargeAmount ?? 0)
            || receipt.TaxAmount != (Pricing?.TaxAmount ?? 0)
            || receipt.Pricing != Pricing
            || !receipt.Lines.SequenceEqual(lines.Select(line =>
                new ReceiptLine(line.ProductId, line.Name, line.UnitPrice, line.Quantity, line.Total))))
            throw new InvalidOperationException("Receipt does not match the paid order.");
        Receipt = receipt with { Lines = Array.AsReadOnly(receipt.Lines.ToArray()) };
    }
    public decimal TotalAmount => Pricing?.TotalAmount ?? lines.Sum(line => line.Total);

    public static OrderAggregate Create(
        Guid id,
        Guid organizationId,
        Guid branchId,
        IReadOnlyCollection<OrderLine> lines,
        string currency,
        Guid? restaurantId = null,
        string channel = "pos",
        string serviceType = "takeaway",
        string? orderNumber = null,
        string? idempotencyKey = null,
        string? workflowPaymentMethod = null,
        Guid? workflowCorrelationId = null, OrderPricing? pricing = null, string? pricingFingerprint = null)
    {
        if (lines.Count == 0) throw new ArgumentException("An order requires at least one line.", nameof(lines));
        if (lines.Any(line => line.Quantity <= 0 || line.UnitPrice < 0))
            throw new ArgumentException("Order lines must have a positive quantity and non-negative price.", nameof(lines));
        if (string.IsNullOrWhiteSpace(currency)) throw new ArgumentException("Currency is required.", nameof(currency));
        return new OrderAggregate(id, organizationId, branchId, lines, currency.ToUpperInvariant(), restaurantId, channel, serviceType, orderNumber, idempotencyKey, workflowPaymentMethod, workflowCorrelationId, pricing, pricingFingerprint);
    }

    public void Submit() => Transition(OrderStatus.Draft, OrderStatus.Submitted);
    public void MarkInventoryReserved() => Transition(OrderStatus.Submitted, OrderStatus.InventoryReserved);
    public void MarkKitchenAccepted() => Transition(OrderStatus.InventoryReserved, OrderStatus.KitchenAccepted);
    public void MarkPaid(Guid? paymentIntentId = null)
    {
        BindPaymentIntent(paymentIntentId);
        Transition(Status is OrderStatus.KitchenAccepted or OrderStatus.PaymentPending ? Status : OrderStatus.KitchenAccepted, OrderStatus.Paid);
    }
    public void MarkManuallyPaid(ManualTenderSettlement settlement)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        if (settlement.OrderId != Id || settlement.OrganizationId != OrganizationId || settlement.BranchId != BranchId
            || settlement.Amount != TotalAmount || !string.Equals(settlement.Currency, Currency, StringComparison.Ordinal))
            throw new InvalidOperationException("Manual tender does not match this order.");
        if (PaymentIntentId is not null)
            throw new InvalidOperationException("An order bound to a provider payment intent cannot be manually settled.");
        MarkPaid();
        IssueReceipt(settlement.OccurredAtUtc, settlement.Method == ManualTenderMethod.Cash ? "cash" : "promptpay_manual");
    }
    public void MarkPaymentPending(Guid? paymentIntentId = null)
    {
        BindPaymentIntent(paymentIntentId);
        Transition(OrderStatus.KitchenAccepted, OrderStatus.PaymentPending);
    }
    public void MarkPaymentFailed() => Transition(Status is OrderStatus.KitchenAccepted or OrderStatus.PaymentPending ? Status : OrderStatus.KitchenAccepted, OrderStatus.PaymentFailed);
    public void MarkPaymentReview() => Transition(OrderStatus.PaymentPending, OrderStatus.PaymentReview);
    public void ResolvePaymentReviewAsVoided() => Transition(OrderStatus.PaymentReview, OrderStatus.PaymentFailed);
    public void ResumePaymentPending() => Transition(OrderStatus.PaymentReview, OrderStatus.PaymentPending);
    public void Reject() => Status = OrderStatus.Rejected;

    public void RestorePaymentIntent(Guid? paymentIntentId) => BindPaymentIntent(paymentIntentId);

    private void BindPaymentIntent(Guid? paymentIntentId)
    {
        if (paymentIntentId is null) return;
        if (paymentIntentId == Guid.Empty) throw new ArgumentException("Payment intent must not be empty.", nameof(paymentIntentId));
        if (PaymentIntentId is { } existing && existing != paymentIntentId)
            throw new InvalidOperationException($"Order {Id} is already bound to another payment intent.");
        PaymentIntentId = paymentIntentId;
    }

    private void Transition(OrderStatus expected, OrderStatus next)
    {
        if (Status != expected)
            throw new InvalidOperationException($"Order {Id} cannot transition from {Status} to {next}.");
        Status = next;
    }
}
