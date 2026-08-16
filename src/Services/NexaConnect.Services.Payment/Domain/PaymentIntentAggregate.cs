namespace NexaConnect.Services.Payment.Domain;

public sealed class PaymentIntentAggregate
{
    private static readonly HashSet<string> PaymentMethods = ["cash", "card", "wallet", "bank_transfer", "other"];

    private PaymentIntentAggregate(Guid id, Guid organizationId, Guid restaurantId, Guid branchId, Guid orderId,
        string idempotencyKey, decimal amount, string currency, string paymentMethod, DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        RestaurantId = restaurantId;
        BranchId = branchId;
        OrderId = orderId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Currency = currency;
        PaymentMethod = paymentMethod;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }
    public Guid OrganizationId { get; }
    public Guid RestaurantId { get; }
    public Guid BranchId { get; }
    public Guid OrderId { get; }
    public string IdempotencyKey { get; }
    public decimal Amount { get; }
    public string Currency { get; }
    public string PaymentMethod { get; }
    public string Status => "pending";
    public DateTimeOffset CreatedAtUtc { get; }

    public static PaymentIntentAggregate Create(Guid organizationId, Guid restaurantId, Guid branchId, Guid orderId,
        string idempotencyKey, decimal amount, string currency, string paymentMethod, DateTimeOffset createdAtUtc)
    {
        if (organizationId == Guid.Empty || restaurantId == Guid.Empty || branchId == Guid.Empty || orderId == Guid.Empty
            || string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Trim().Length > 200 || amount <= 0
            || amount > 999999999999999.9999m || decimal.Round(amount, 4) != amount
            || string.IsNullOrWhiteSpace(currency) || string.IsNullOrWhiteSpace(paymentMethod))
            throw new ArgumentException("Organization, restaurant, branch, order, idempotency key, positive amount, currency, and payment method are required.");
        string normalizedCurrency = currency.Trim().ToUpperInvariant();
        string normalizedMethod = paymentMethod.Trim().ToLowerInvariant();
        if (normalizedCurrency.Length != 3 || normalizedCurrency.Any(character => character is < 'A' or > 'Z'))
            throw new ArgumentException("Currency must be a three-letter ISO code.");
        if (!PaymentMethods.Contains(normalizedMethod))
            throw new ArgumentException("Payment method must be cash, card, wallet, bank_transfer, or other.");
        return new PaymentIntentAggregate(Guid.NewGuid(), organizationId, restaurantId, branchId, orderId,
            idempotencyKey.Trim(), amount, normalizedCurrency, normalizedMethod, createdAtUtc);
    }
}
