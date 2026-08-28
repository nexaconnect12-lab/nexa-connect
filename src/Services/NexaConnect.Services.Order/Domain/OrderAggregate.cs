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
        string? idempotencyKey = null)
    {
        Id = id;
        OrganizationId = organizationId;
        BranchId = branchId;
        this.lines = lines.ToList();
        Currency = currency;
        RestaurantId = restaurantId ?? organizationId;
        Channel = channel;
        ServiceType = serviceType;
        OrderNumber = orderNumber ?? id.ToString("N")[..12];
        IdempotencyKey = idempotencyKey;
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
    public Guid? PaymentIntentId { get; private set; }
    public OrderStatus Status { get; private set; }
    public IReadOnlyList<OrderLine> Lines => lines;
    public decimal TotalAmount => lines.Sum(line => line.Total);

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
        string? idempotencyKey = null)
    {
        if (lines.Count == 0) throw new ArgumentException("An order requires at least one line.", nameof(lines));
        if (lines.Any(line => line.Quantity <= 0 || line.UnitPrice < 0))
            throw new ArgumentException("Order lines must have a positive quantity and non-negative price.", nameof(lines));
        if (string.IsNullOrWhiteSpace(currency)) throw new ArgumentException("Currency is required.", nameof(currency));
        return new OrderAggregate(id, organizationId, branchId, lines, currency.ToUpperInvariant(), restaurantId, channel, serviceType, orderNumber, idempotencyKey);
    }

    public void Submit() => Transition(OrderStatus.Draft, OrderStatus.Submitted);
    public void MarkInventoryReserved() => Transition(OrderStatus.Submitted, OrderStatus.InventoryReserved);
    public void MarkKitchenAccepted() => Transition(OrderStatus.InventoryReserved, OrderStatus.KitchenAccepted);
    public void MarkPaid(Guid? paymentIntentId = null)
    {
        BindPaymentIntent(paymentIntentId);
        Transition(Status is OrderStatus.KitchenAccepted or OrderStatus.PaymentPending ? Status : OrderStatus.KitchenAccepted, OrderStatus.Paid);
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
